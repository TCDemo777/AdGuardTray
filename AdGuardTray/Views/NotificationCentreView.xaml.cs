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
}
