using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace AdGuardTray.Views
{
    public partial class AboutView : UserControl
    {
        public AboutView()
        {
            InitializeComponent();
        }

        private void GitHubLink_RequestNavigate(
            object sender,
            RequestNavigateEventArgs e)
        {
            Process.Start(
                new ProcessStartInfo(e.Uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });

            e.Handled = true;
        }
    }
}