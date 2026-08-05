using System.Windows.Controls;
using RouterPilot.ViewModels;

namespace RouterPilot.Views;

public partial class NotificationCentreView : UserControl
{
    public NotificationCentreView(NotificationCentreViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
