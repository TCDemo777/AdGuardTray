using System;
using System.Diagnostics;
using System.Windows;

namespace AdGuardTray
{
    public partial class MainWindow : Window
    {

        public MainWindow()
        {
            InitializeComponent();
        }


        public void OpenAdGuard(string address)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = address,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "AdGuard Tray",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

    }
}