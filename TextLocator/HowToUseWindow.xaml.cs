using System.Windows;

namespace TextLocator
{
    public partial class HowToUseWindow : Window
    {
        public HowToUseWindow()
        {
            InitializeComponent();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
