using System;
using System.Collections.Generic;

namespace RouterPilot.Models;

public sealed class RouterPilotBackupManifest
{
    public int FormatVersion { get; init; } = 1;
    public string ApplicationVersion { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; }
    public string? SourceMachineName { get; init; }
    public List<RouterPilotBackupFile> Files { get; init; } = [];
}

public sealed class RouterPilotBackupFile
{
    public string FileName { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
}

public sealed record BackupInspection(
    string ArchivePath,
    RouterPilotBackupManifest Manifest,
    IReadOnlyList<string> AvailableFiles);

public sealed record BackupOperationResult(
    bool Succeeded,
    string Message,
    string? BackupPath = null,
    long? BackupSizeBytes = null)
{
    public static BackupOperationResult Success(string message, string? path = null, long? sizeBytes = null) =>
        new(true, message, path, sizeBytes);

    public static BackupOperationResult Failure(string message) =>
        new(false, message);
}
