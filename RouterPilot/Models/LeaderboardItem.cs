using System.Globalization;

namespace RouterPilot.Models;

/// <summary>
/// Presentation model shared by leaderboard views.
/// Percentage is clamped to the 0-100 range for safe progress-bar binding.
/// </summary>
public sealed class LeaderboardItem
{
    private double _percentage;

    public int Rank { get; init; }

    public string Label { get; init; } = string.Empty;

    public long Value { get; init; }

    public string? FormattedValue { get; init; }

    public string? ToolTipText { get; init; }

    public double Percentage
    {
        get => _percentage;
        init => _percentage = Math.Clamp(value, 0d, 100d);
    }

    public string DisplayValue =>
        string.IsNullOrWhiteSpace(FormattedValue)
            ? Value.ToString("N0", CultureInfo.CurrentCulture)
            : FormattedValue;

    public string ToolTip =>
        string.IsNullOrWhiteSpace(ToolTipText)
            ? Label
            : ToolTipText;
}
