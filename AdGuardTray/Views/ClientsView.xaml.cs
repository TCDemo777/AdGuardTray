using System.Windows.Controls;
using AdGuardTray.ViewModels;

namespace AdGuardTray.Views
{
    public partial class ClientsView : UserControl
    {
        public ClientsView()
        {
            InitializeComponent();

            DataContext =
                new ClientsViewModel();
        }
    }
}