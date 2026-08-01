using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using AdGuardTray.Models;

namespace AdGuardTray.Services;

public sealed class UpdateService : IDisposable
{
    public const string ReleasesPageUrl =
        "https://github.com/TCDemo777/AdGuardTray/releases";
    private const string ReleasesApiUrl =
        "https://api.github.com/repos/TCDemo777/AdGuardTray/releases?per_page=20";
    private static readonly TimeSpan CheckFrequency = TimeSpan.FromDays(1);

    private readonly SettingsService _settingsService;
    private readonly NotificationService _notificationService;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private bool _disposed;

    public UpdateService(
        SettingsService settingsService,
        NotificationService notificationService)
    {
        _settingsService = settingsService;
        _notificationService = notificationService;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("RouterPilot", CurrentVersion));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public string CurrentVersion => GetCurrentVersion();

    public ReleaseInfo? LatestRelease { get; private set; }

    public DateTimeOffset? LastSuccessfulCheck =>
        _settingsService.Load().LastSuccessfulUpdateCheckUtc;

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        bool manual,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _checkGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppSettings settings = _settingsService.Load();
            if (!manual && !settings.AutomaticallyCheckForUpdates)
            {
                return Result(UpdateCheckStatus.Skipped,
                    "Automatic update checks are disabled.");
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (!manual && settings.LastSuccessfulUpdateCheckUtc is { } last &&
                now - last < CheckFrequency)
            {
                return Result(UpdateCheckStatus.Skipped,
                    "The daily update check has already completed.", last);
            }

            try
            {
                using HttpResponseMessage response = await _httpClient
                    .GetAsync(ReleasesApiUrl, HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Forbidden ||
                    response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    return Result(UpdateCheckStatus.Unavailable,
                        "GitHub rate limiting prevented the update check.");
                }
                response.EnsureSuccessStatusCode();
                await using Stream stream = await response.Content
                    .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using JsonDocument document = await JsonDocument
                    .ParseAsync(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                LatestRelease = document.RootElement.EnumerateArray()
                    .Where(release => !GetBoolean(release, "draft"))
                    .Where(release => settings.IncludePrereleaseUpdates ||
                                      !GetBoolean(release, "prerelease"))
                    .Select(ParseRelease)
                    .Where(release => SemanticVersion.TryParse(release.Version, out _))
                    .OrderByDescending(release =>
                        SemanticVersion.Parse(release.Version))
                    .FirstOrDefault();

                settings.LastSuccessfulUpdateCheckUtc = now;
                settings.LatestVersionSeen = LatestRelease?.Version ?? string.Empty;
                _settingsService.Save(settings);

                if (LatestRelease is null)
                    return Result(UpdateCheckStatus.UpToDate,
                        "No eligible GitHub release was found.", now);

                bool newer = SemanticVersion.Parse(LatestRelease.Version) >
                             SemanticVersion.Parse(CurrentVersion);
                if (!newer)
                    return Result(UpdateCheckStatus.UpToDate,
                        "RouterPilot is up to date.", now);

                if (!string.Equals(settings.LastNotifiedUpdateVersion,
                        LatestRelease.Version, StringComparison.OrdinalIgnoreCase))
                {
                    bool added = await _notificationService.AddAsync(new AppNotification
                    {
                        Title = $"RouterPilot {LatestRelease.Version} is available.",
                        Message = "Open GitHub Releases to view the release notes and downloads.",
                        Severity = NotificationSeverity.Information,
                        Category = NotificationCategory.System,
                        ActionTarget = LatestRelease.ReleaseNotesUrl?.AbsoluteUri ?? ReleasesPageUrl,
                        DeduplicationKey = "RouterPilotUpdate-" + LatestRelease.Version
                    });
                    if (added)
                    {
                        settings.LastNotifiedUpdateVersion = LatestRelease.Version;
                        _settingsService.Save(settings);
                    }
                }

                return Result(UpdateCheckStatus.UpdateAvailable,
                    $"RouterPilot {LatestRelease.Version} is available.", now);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Result(UpdateCheckStatus.Unavailable,
                    "The GitHub update check timed out.");
            }
            catch (HttpRequestException)
            {
                return Result(UpdateCheckStatus.Unavailable,
                    "GitHub Releases is currently unavailable.");
            }
            catch (JsonException)
            {
                return Result(UpdateCheckStatus.Unavailable,
                    "GitHub returned an unreadable release response.");
            }
        }
        finally
        {
            _checkGate.Release();
        }
    }

    private UpdateCheckResult Result(UpdateCheckStatus status, string message,
        DateTimeOffset? checkedAt = null) => new()
    {
        Status = status,
        CurrentVersion = CurrentVersion,
        LatestRelease = LatestRelease,
        CheckedAt = checkedAt ?? LastSuccessfulCheck,
        Message = message
    };

    private static ReleaseInfo ParseRelease(JsonElement release)
    {
        string tag = GetString(release, "tag_name");
        var assets = release.TryGetProperty("assets", out JsonElement assetArray)
            ? assetArray.EnumerateArray().Select(asset => new ReleaseAssetInfo(
                GetString(asset, "name"),
                new Uri(GetString(asset, "browser_download_url")),
                asset.TryGetProperty("size", out JsonElement size) ? size.GetInt64() : 0,
                GetString(asset, "content_type"))).ToArray()
            : Array.Empty<ReleaseAssetInfo>();
        return new ReleaseInfo
        {
            Tag = tag,
            Version = SemanticVersion.Normalize(tag),
            PublishedAt = release.TryGetProperty("published_at", out JsonElement published) &&
                          published.TryGetDateTimeOffset(out DateTimeOffset date) ? date : null,
            ReleaseNotesUrl = Uri.TryCreate(GetString(release, "html_url"),
                UriKind.Absolute, out Uri? url) ? url : null,
            IsPrerelease = GetBoolean(release, "prerelease"),
            Assets = assets
        };
    }

    private static string GetCurrentVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string value = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "0.0.0";
        int metadata = value.IndexOf('+');
        return SemanticVersion.Normalize(metadata >= 0 ? value[..metadata] : value);
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) ? value.GetString() ?? "" : "";

    private static bool GetBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
        _checkGate.Dispose();
    }

    private readonly record struct SemanticVersion(
        int Major, int Minor, int Patch, int Revision, string Prerelease)
        : IComparable<SemanticVersion>
    {
        public static string Normalize(string value) =>
            value.Trim().TrimStart('v', 'V');

        public static bool TryParse(string value, out SemanticVersion version)
        {
            version = default;
            string normalized = Normalize(value);
            string[] metadataParts = normalized.Split('+', 2);
            string[] prereleaseParts = metadataParts[0].Split('-', 2);
            string[] numbers = prereleaseParts[0].Split('.');
            if (numbers.Length is < 2 or > 4 ||
                !int.TryParse(numbers[0], out int major) ||
                !int.TryParse(numbers[1], out int minor) ||
                (numbers.Length > 2 && !int.TryParse(numbers[2], out _)) ||
                (numbers.Length > 3 && !int.TryParse(numbers[3], out _)))
                return false;
            version = new SemanticVersion(major, minor,
                numbers.Length > 2 ? int.Parse(numbers[2]) : 0,
                numbers.Length > 3 ? int.Parse(numbers[3]) : 0,
                prereleaseParts.Length > 1 ? prereleaseParts[1] : "");
            return true;
        }

        public static SemanticVersion Parse(string value) =>
            TryParse(value, out SemanticVersion version) ? version : default;

        public int CompareTo(SemanticVersion other)
        {
            int result = Major.CompareTo(other.Major);
            if (result == 0) result = Minor.CompareTo(other.Minor);
            if (result == 0) result = Patch.CompareTo(other.Patch);
            if (result == 0) result = Revision.CompareTo(other.Revision);
            if (result != 0) return result;
            if (Prerelease.Length == 0) return other.Prerelease.Length == 0 ? 0 : 1;
            if (other.Prerelease.Length == 0) return -1;
            string[] leftParts = Prerelease.Split('.');
            string[] rightParts = other.Prerelease.Split('.');
            for (int index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
            {
                if (index >= leftParts.Length) return -1;
                if (index >= rightParts.Length) return 1;
                bool leftNumeric = int.TryParse(leftParts[index], out int leftNumber);
                bool rightNumeric = int.TryParse(rightParts[index], out int rightNumber);
                if (leftNumeric && rightNumeric)
                {
                    int numericResult = leftNumber.CompareTo(rightNumber);
                    if (numericResult != 0) return numericResult;
                    continue;
                }
                if (leftNumeric != rightNumeric) return leftNumeric ? -1 : 1;
                int textResult = string.Compare(leftParts[index], rightParts[index],
                    StringComparison.OrdinalIgnoreCase);
                if (textResult != 0) return textResult;
            }
            return 0;
        }

        public static bool operator >(SemanticVersion left, SemanticVersion right) =>
            left.CompareTo(right) > 0;
        public static bool operator <(SemanticVersion left, SemanticVersion right) =>
            left.CompareTo(right) < 0;
    }
}
