using log4net;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using TextLocator.Core;
using TextLocator.Message;
using TextLocator.Util;


namespace TextLocator
{
    /// <summary>
    /// Interaction logic for HelpWindow.xaml
    /// </summary>
    public partial class HelpWindow : Window
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Singleton instance
        /// </summary>
        private static HelpWindow _instance;

        public HelpWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Create an instance of the system parameter settings window
        /// </summary>
        /// <returns></returns>
        public static HelpWindow CreateInstance()
        {
            return _instance ?? (_instance = new HelpWindow());
        }

        /// <summary>
        /// Window close event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Closed(object sender, EventArgs e)
        {
            _instance.Topmost = false;
            _instance = null;
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch
            {
                // Optional: show message when navigation fails
                // MessageBox.Show("Unable to open link: " + e.Uri.AbsoluteUri);
            }
            e.Handled = true;
        }
    }
}
