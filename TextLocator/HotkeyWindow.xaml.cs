using log4net;
using Newtonsoft.Json;
using Rubyer;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TextLocator.HotKey;
using TextLocator.Util;

namespace TextLocator
{
    /// <summary>
    /// Interaction logic for HotkeyWindow.xaml
    /// </summary>
    public partial class HotkeyWindow : Window
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Singleton instance
        /// </summary>
        private static HotkeyWindow _instance;

        // Collection
        private ObservableCollection<HotKeyModel> _hotKeyList = new ObservableCollection<HotKeyModel>();
        public ObservableCollection<HotKeyModel> HotKeyList { get => _hotKeyList; set => _hotKeyList = value; }

        public HotkeyWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Create an instance of the system parameter settings window
        /// </summary>
        /// <returns></returns>
        public static HotkeyWindow CreateInstance()
        {
            return _instance ?? (_instance = new HotkeyWindow());
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize hotkeys
            InitHotKey();
        }

        /// <summary>
        /// Initialize hotkeys
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        private void InitHotKey()
        {
            var list = HotKeySettingManager.Instance.LoadDefaultHotKey();
            list.ToList().ForEach(x => HotKeyList.Add(x));
        }

        #region Save and Close
        /// <summary>
        /// Save and close
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SaveClose_Click(object sender, RoutedEventArgs e)
        {
            if (!HotKeySettingManager.Instance.RegisterGlobalHotKey(HotKeyList))
            {
                return;
            }
            foreach (HotKeyModel hotKey in HotKeyList)
            {
                log.Debug(Newtonsoft.Json.JsonConvert.SerializeObject(hotKey));
                AppUtil.WriteValue("HotKey", hotKey.Name, String.Format("{0}_{1}_{2}_{3}_{4}", hotKey.IsUsable, hotKey.IsSelectCtrl, hotKey.IsSelectAlt, hotKey.IsSelectShift, hotKey.SelectKey));
            }
            this.Close();
        }
        #endregion

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
    }
}
