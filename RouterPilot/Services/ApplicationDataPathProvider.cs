using System.Diagnostics;
using System.IO;

namespace RouterPilot.Services;

/// <summary>
/// Owns RouterPilot's application-data locations and performs the one-way,
/// non-destructive migration from the legacy application folder.
/// </summary>
public sealed class ApplicationDataPathProvider
{
    private static readonly string[] MigratedFiles =
    [
        "settings.json",
        "notifications.json",
        "client-profiles.json",
        "adguard-service-schedules.json"
    ];

    public ApplicationDataPathProvider()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        CurrentPath = Path.Combine(localApplicationData, "RouterPilot");
        LegacyPath = Path.Combine(localApplicationData, "AdGuardTray");
    }

    public string CurrentPath { get; }

    public string LegacyPath { get; }

    /// <summary>
    /// Copies only missing supported files. Legacy files remain untouched so a
    /// previous release can continue to use them after a rollback.
    /// </summary>
    public void MigrateLegacyData()
    {
        try
        {
            Directory.CreateDirectory(CurrentPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Application-data migration: destination folder unavailable ({ex.GetType().Name}).");
            return;
        }

        foreach (string fileName in MigratedFiles)
        {
            string source = Path.Combine(LegacyPath, fileName);
            string destination = Path.Combine(CurrentPath, fileName);

            if (File.Exists(destination))
            {
                Debug.WriteLine($"Application-data migration: {fileName} destination already exists.");
                continue;
            }

            if (!File.Exists(source))
            {
                Debug.WriteLine($"Application-data migration: {fileName} source missing.");
                continue;
            }

            try
            {
                File.Copy(source, destination, overwrite: false);
                Debug.WriteLine($"Application-data migration: {fileName} copied.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Application-data migration: {fileName} copy failed ({ex.GetType().Name}).");
            }
        }
    }
}
