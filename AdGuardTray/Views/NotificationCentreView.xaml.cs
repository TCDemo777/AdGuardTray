using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using AdGuardTray.ViewModels;

namespace AdGuardTray.Views;

public partial class NotificationCentreView : UserControl
{
    public NotificationCentreView(NotificationCentreViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OpenNotificationAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target } ||
            !Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("https" or "http"))
            return;

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
        {
            UseShellExecute = true
        });
    }
}
