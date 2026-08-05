using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace RouterPilot.Views
{
    public partial class AnalyticsView : UserControl
    {
        private bool _initialLayoutInProgress = true;
        private bool _initialScrollApplied;

        public AnalyticsView()
        {
            InitializeComponent();
            Loaded += AnalyticsView_Loaded;
        }

        private void AnalyticsView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_initialScrollApplied)
            {
                return;
            }

            _initialScrollApplied = true;
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    AnalyticsScrollViewer.ScrollToTop();
                    _initialLayoutInProgress = false;
                }),
                DispatcherPriority.ContextIdle);
        }

        private void AnalyticsScrollViewer_RequestBringIntoView(
            object sender,
            RequestBringIntoViewEventArgs e)
        {
            // Charts and focusable descendants can request scrolling while
            // their templates are first realised. Keep the new page at its
            // documented starting position, then restore normal accessibility.
            if (_initialLayoutInProgress &&
                !ReferenceEquals(e.OriginalSource, AnalyticsScrollViewer))
            {
                e.Handled = true;
            }
        }
    }
}
