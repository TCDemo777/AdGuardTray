using System;
using System.Collections.Generic;
using System.IO;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using RouterPilot.Models;

namespace RouterPilot.Views;

public partial class BackupRestorePreviewWindow : Window
{
    public BackupRestorePreviewWindow(BackupInspection inspection)
    {
        InitializeComponent();
        ViewModel = new BackupRestorePreviewViewModel(inspection);
        DataContext = ViewModel;
    }

    public BackupRestorePreviewViewModel ViewModel { get; }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedFiles.Count == 0)
        {
            MessageBox.Show("Select at least one item to restore.", "Restore backup", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}

public sealed class BackupRestorePreviewViewModel
{
    public BackupRestorePreviewViewModel(BackupInspection inspection)
    {
        BackupVersion = inspection.Manifest.ApplicationVersion;
        CreatedDisplay = inspection.Manifest.CreatedUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm");
        BackupFileName = Path.GetFileName(inspection.ArchivePath);
        FormatVersion = inspection.Manifest.FormatVersion;
        Items = new ObservableCollection<BackupRestorePreviewItem>(inspection.AvailableFiles
            .Select(name => new BackupRestorePreviewItem(name)));
    }

    public string BackupVersion { get; }
    public string CreatedDisplay { get; }
    public string BackupFileName { get; }
    public int FormatVersion { get; }
    public ObservableCollection<BackupRestorePreviewItem> Items { get; }
    public IReadOnlyCollection<string> SelectedFiles => Items
        .Where(item => item.IsSelected)
        .Select(item => item.FileName)
        .ToArray();
}

public sealed partial class BackupRestorePreviewItem : ObservableObject
{
    public BackupRestorePreviewItem(string fileName)
    {
        FileName = fileName;
        DisplayName = fileName switch
        {
            "settings.json" => "Settings",
            "notifications.json" => "Notifications",
            "client-profiles.json" => "Client profiles",
            "adguard-service-schedules.json" => "Service schedules",
            _ => fileName
        };
    }

    public string FileName { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private bool isSelected = true;
}
