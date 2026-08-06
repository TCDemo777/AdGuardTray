using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RouterPilot.Models;

namespace RouterPilot.Services;

public sealed class BackupRestoreService : IBackupRestoreService
{
    private const int CurrentFormatVersion = 1;
    private const long MaximumArchiveBytes = 50L * 1024 * 1024;
    private const long MaximumFileBytes = 10L * 1024 * 1024;
    private static readonly string[] SupportedFiles =
    [
        "settings.json",
        "notifications.json",
        "client-profiles.json",
        "adguard-service-schedules.json"
    ];

    private static readonly HashSet<string> SupportedFileSet =
        new(SupportedFiles, StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly ApplicationDataPathProvider _paths;
    private readonly IRouterManagerProvider _routerManagerProvider;
    private readonly NotificationService _notificationService;
    private readonly AdGuardServiceScheduleService _scheduleService;

    public BackupRestoreService(
        ApplicationDataPathProvider paths,
        IRouterManagerProvider routerManagerProvider,
        NotificationService notificationService,
        AdGuardServiceScheduleService scheduleService)
    {
        _paths = paths;
        _routerManagerProvider = routerManagerProvider;
        _notificationService = notificationService;
        _scheduleService = scheduleService;
        BackupFolder = Path.Combine(paths.CurrentPath, "Backups");
    }

    public string BackupFolder { get; }

    public async Task<BackupOperationResult> CreateBackupAsync(
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            return BackupOperationResult.Failure("Choose a backup destination.");

        try
        {
            string fullDestination = Path.GetFullPath(destinationPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
            string temporaryPath = fullDestination + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                await _notificationService.FlushAsync(cancellationToken).ConfigureAwait(false);
                await _scheduleService.FlushAsync(cancellationToken).ConfigureAwait(false);
                RouterPilotBackupManifest manifest = await WriteArchiveAsync(
                    temporaryPath,
                    cancellationToken).ConfigureAwait(false);

                _ = await InspectAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, fullDestination, overwrite: true);

                return BackupOperationResult.Success(
                    $"Backup created with {manifest.Files.Count} file(s).",
                    fullDestination,
                    new FileInfo(fullDestination).Length);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return BackupOperationResult.Failure("Backup cancelled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return BackupOperationResult.Failure("RouterPilot could not create the backup.");
        }
    }

    public async Task<BackupInspection> InspectAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            throw new InvalidDataException("The selected backup file was not found.");

        FileInfo file = new(archivePath);
        if (file.Length > MaximumArchiveBytes)
            throw new InvalidDataException("The selected backup is too large.");

        await using FileStream stream = new(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read, leaveOpen: false);

        Dictionary<string, ZipArchiveEntry> entries = ValidateArchiveEntries(archive);
        if (!entries.Remove("manifest.json", out ZipArchiveEntry? manifestEntry))
            throw new InvalidDataException("The backup manifest is missing.");

        RouterPilotBackupManifest manifest = await ReadManifestAsync(
            manifestEntry,
            cancellationToken).ConfigureAwait(false);
        ValidateManifest(manifest, entries);

        foreach (RouterPilotBackupFile item in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ZipArchiveEntry entry = entries[item.FileName];
            string actualHash = await ComputeHashAsync(entry, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualHash, item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The backup hash for {item.FileName} does not match.");
        }

        return new BackupInspection(
            Path.GetFullPath(archivePath),
            manifest,
            manifest.Files.Select(item => item.FileName).ToArray());
    }

    public async Task<BackupOperationResult> RestoreAsync(
        BackupInspection inspection,
        IReadOnlyCollection<string> selectedFiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        selectedFiles ??= Array.Empty<string>();

        HashSet<string> selected = new(selectedFiles, StringComparer.Ordinal);
        if (selected.Count == 0)
            return BackupOperationResult.Failure("Select at least one item to restore.");
        if (!selected.All(inspection.AvailableFiles.Contains) || !selected.All(SupportedFileSet.Contains))
            return BackupOperationResult.Failure("The selected backup contains an unsupported item.");

        try
        {
            _ = await InspectAsync(inspection.ArchivePath, cancellationToken).ConfigureAwait(false);
            string preRestorePath = Path.Combine(
                BackupFolder,
                "PreRestore_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + ".rpb");
            BackupOperationResult preRestore = await CreateBackupAsync(preRestorePath, cancellationToken)
                .ConfigureAwait(false);
            if (!preRestore.Succeeded)
                return BackupOperationResult.Failure("RouterPilot could not create the required pre-restore backup.");

            string stagingFolder = Path.Combine(
                _paths.CurrentPath,
                ".restore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingFolder);
            try
            {
                Dictionary<string, byte[]> replacements = await ReadSelectedFilesAsync(
                    inspection.ArchivePath,
                    selected,
                    cancellationToken).ConfigureAwait(false);
                await ReplaceFilesAtomicallyAsync(replacements, stagingFolder, cancellationToken)
                    .ConfigureAwait(false);
                await ReloadRestoredServicesAsync(selected).ConfigureAwait(false);
            }
            finally
            {
                if (Directory.Exists(stagingFolder))
                    Directory.Delete(stagingFolder, recursive: true);
            }

            string restartNote = selected.Contains("client-profiles.json")
                ? " Restart RouterPilot to apply restored client profiles."
                : string.Empty;
            return BackupOperationResult.Success(
                $"Restored {selected.Count} item(s). A pre-restore backup was saved.{restartNote}",
                preRestorePath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return BackupOperationResult.Failure("Restore cancelled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return BackupOperationResult.Failure("RouterPilot could not restore the selected backup. Original files were preserved.");
        }
    }

    private async Task<RouterPilotBackupManifest> WriteArchiveAsync(
        string archivePath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.CurrentPath);
        List<(string Name, byte[] Contents)> files = [];
        foreach (string name in SupportedFiles)
        {
            string path = Path.Combine(_paths.CurrentPath, name);
            if (File.Exists(path))
                files.Add((name, await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)));
        }

        RouterPilotBackupManifest manifest = new()
        {
            FormatVersion = CurrentFormatVersion,
            ApplicationVersion = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown",
            CreatedUtc = DateTimeOffset.UtcNow,
            SourceMachineName = Environment.MachineName,
            Files = files.Select(file => new RouterPilotBackupFile
            {
                FileName = file.Name,
                Sha256 = Convert.ToHexString(SHA256.HashData(file.Contents))
            }).ToList()
        };

        await using FileStream output = new(
            archivePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            useAsync: true);
        using ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach ((string name, byte[] contents) in files)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            await using Stream entryStream = entry.Open();
            await entryStream.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
        }

        ZipArchiveEntry manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        await using Stream manifestStream = manifestEntry.Open();
        await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return manifest;
    }

    private static Dictionary<string, ZipArchiveEntry> ValidateArchiveEntries(ZipArchive archive)
    {
        Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.Ordinal);
        long totalLength = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) ||
                !string.Equals(entry.FullName, entry.Name, StringComparison.Ordinal) ||
                entry.Name.Contains("..", StringComparison.Ordinal) ||
                entry.Length > MaximumFileBytes ||
                !entries.TryAdd(entry.Name, entry))
            {
                throw new InvalidDataException("The backup archive contains an unsafe entry.");
            }

            totalLength += entry.Length;
            if (totalLength > MaximumArchiveBytes ||
                (entry.Name != "manifest.json" && !SupportedFileSet.Contains(entry.Name)))
            {
                throw new InvalidDataException("The backup archive contains an unsupported entry.");
            }
        }

        return entries;
    }

    private static async Task<RouterPilotBackupManifest> ReadManifestAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using Stream stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<RouterPilotBackupManifest>(
                   stream,
                   JsonOptions,
                   cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidDataException("The backup manifest is invalid.");
    }

    private static void ValidateManifest(
        RouterPilotBackupManifest manifest,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        if (manifest.FormatVersion <= 0 || manifest.FormatVersion > CurrentFormatVersion)
            throw new InvalidDataException("This backup uses an unsupported format version.");
        if (manifest.Files.Count != entries.Count ||
            manifest.Files.Any(item => string.IsNullOrWhiteSpace(item.FileName) ||
                                       string.IsNullOrWhiteSpace(item.Sha256) ||
                                       !SupportedFileSet.Contains(item.FileName)) ||
            manifest.Files.Select(item => item.FileName).Distinct(StringComparer.Ordinal).Count() != manifest.Files.Count ||
            manifest.Files.Any(item => !entries.ContainsKey(item.FileName)))
        {
            throw new InvalidDataException("The backup manifest does not match the archive contents.");
        }
    }

    private static async Task<string> ComputeHashAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using Stream stream = entry.Open();
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            hash.AppendData(buffer, 0, read);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task<Dictionary<string, byte[]>> ReadSelectedFilesAsync(
        string archivePath,
        IReadOnlySet<string> selected,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read);
        Dictionary<string, ZipArchiveEntry> entries = ValidateArchiveEntries(archive);
        if (!entries.Remove("manifest.json", out ZipArchiveEntry? manifestEntry))
            throw new InvalidDataException("The backup manifest is missing.");

        RouterPilotBackupManifest manifest = await ReadManifestAsync(manifestEntry, cancellationToken)
            .ConfigureAwait(false);
        ValidateManifest(manifest, entries);
        Dictionary<string, string> expectedHashes = manifest.Files.ToDictionary(
            item => item.FileName,
            item => item.Sha256,
            StringComparer.Ordinal);

        Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        foreach (string name in selected)
        {
            string actualHash = await ComputeHashAsync(entries[name], cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualHash, expectedHashes[name], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The backup hash for {name} does not match.");

            await using Stream input = entries[name].Open();
            using MemoryStream output = new();
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            files.Add(name, output.ToArray());
        }
        return files;
    }

    private async Task ReplaceFilesAtomicallyAsync(
        IReadOnlyDictionary<string, byte[]> replacements,
        string stagingFolder,
        CancellationToken cancellationToken)
    {
        Dictionary<string, byte[]?> originals = replacements.Keys.ToDictionary(
            name => name,
            name =>
            {
                string path = Path.Combine(_paths.CurrentPath, name);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            },
            StringComparer.Ordinal);
        List<string> replaced = [];

        try
        {
            foreach ((string name, byte[] contents) in replacements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string target = Path.Combine(_paths.CurrentPath, name);
                string temporary = Path.Combine(stagingFolder, name + ".tmp");
                await File.WriteAllBytesAsync(temporary, contents, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(contents)),
                        Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(temporary, cancellationToken).ConfigureAwait(false))),
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Staged restore data could not be verified.");
                }

                File.Move(temporary, target, overwrite: true);
                replaced.Add(name);
            }
        }
        catch
        {
            foreach (string name in replaced)
            {
                string target = Path.Combine(_paths.CurrentPath, name);
                if (originals[name] is { } original)
                    await File.WriteAllBytesAsync(target, original, CancellationToken.None).ConfigureAwait(false);
                else if (File.Exists(target))
                    File.Delete(target);
            }

            throw;
        }
    }

    private async Task ReloadRestoredServicesAsync(IReadOnlySet<string> selected)
    {
        if (selected.Contains("settings.json"))
            _routerManagerProvider.Invalidate();
        if (selected.Contains("notifications.json"))
            await _notificationService.ReloadAsync().ConfigureAwait(false);
        if (selected.Contains("adguard-service-schedules.json"))
            await _scheduleService.ReloadAsync().ConfigureAwait(false);
    }
}
