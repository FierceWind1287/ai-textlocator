using System.Windows;

namespace TextLocator
{
    public partial class ProgressWindow : Window
    {
        public ProgressWindow()
        {
            InitializeComponent();
        }

        // 可绑定的提示文字 —— 默认值可随意
        public static readonly DependencyProperty HintProperty =
            DependencyProperty.Register(nameof(Hint),
                                        typeof(string),
                                        typeof(ProgressWindow),
                                        new PropertyMetadata("Processing, please wait…"));

        public string Hint
        {
            get => (string)GetValue(HintProperty);
            set => SetValue(HintProperty, value);
        }
    }
}
