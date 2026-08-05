namespace RouterPilot.Models;

public sealed class ReleaseInfo
{
    public string Tag { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; init; }
    public Uri? ReleaseNotesUrl { get; init; }
    public bool IsPrerelease { get; init; }
}

public enum UpdateCheckStatus { UpdateAvailable, UpToDate, Skipped, Unavailable }

public sealed class UpdateCheckResult
{
    public UpdateCheckStatus Status { get; init; }
    public string CurrentVersion { get; init; } = string.Empty;
    public ReleaseInfo? LatestRelease { get; init; }
    public DateTimeOffset? CheckedAt { get; init; }
    public string Message { get; init; } = string.Empty;
}
