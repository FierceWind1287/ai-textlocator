using log4net;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TextLocator.Core;
using TextLocator.Enums;
using TextLocator.HotKey;
using TextLocator.Index;
using TextLocator.Message;
using TextLocator.Util;
using TextLocator.ViewModel.Main;
using Rubyer;

namespace TextLocator
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// 全部
        /// </summary>
        private RadioButton _radioButtonAll;
        /// <summary>
        /// 时间戳
        /// </summary>
        private long _timestamp;
        /// <summary>
        /// 搜索参数
        /// </summary>
        private Entity.SearchParam _searchParam;
        /// <summary>
        /// 索引构建中
        /// </summary>
        private static volatile bool build = false;

        /// <summary>
        /// 数据模型
        /// </summary>
        private MainViewModel _viewModel = new MainViewModel();

        #region 热键
        /// <summary>
        /// 当前窗口句柄
        /// </summary>
        private IntPtr _hwnd = new IntPtr();
        /// <summary>
        /// 记录快捷键注册项的唯一标识符
        /// </summary>
        private Dictionary<HotKeySetting, int> _hotKeySettings = new Dictionary<HotKeySetting, int>();
        #endregion


        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = _viewModel;

        }

        #region 窗口初始化
        /// <summary>
        /// WPF窗体的资源初始化完成，并且可以通过WindowInteropHelper获得该窗体的句柄用来与Win32交互后调用
        /// </summary>
        /// <param name="e"></param>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // 获取窗体句柄
            _hwnd = new WindowInteropHelper(this).Handle;
            HwndSource hWndSource = HwndSource.FromHwnd(_hwnd);
            // 添加处理程序
            if (hWndSource != null) hWndSource.AddHook(WndProc);
        }

        /// <summary>
        /// 所有控件初始化完成后调用
        /// </summary>
        /// <param name="e"></param>
        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            // 注册热键
            _ = InitHotKey();
        }

        /// <summary>
        /// 加载完毕
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 初始化应用信息
            InitializeAppInfo();

            // 初始化配置文件信息
            InitializeAppConfig();

            // 初始化文件类型列表
            InitializeSearchFileType();

            // 初始化排序类型列表
            InitializeSortType();

            // 初始化搜索域列表
            InitializeSearchRegion();

            // 清理事件（必须放在初始化之后，否则类型筛选的选中Reset可能存在错误）
            ResetSearchResult();

            // 检查索引是否存在：如果存在才执行更新检查，不存在的跳过更新检查。
            if (CheckIndexExist(false))
            {
                // 软件每次启动时执行索引更新逻辑？
                IndexUpdateTask();
            }

            // 注册全局热键时间
            HotKeySettingManager.Instance.RegisterGlobalHotKeyEvent += Instance_RegisterGlobalHotKeyEvent;
        }

        /// <summary>
        /// 窗口激活
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Activated(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = CacheUtil.Get<WindowState>("WindowState");
        }

        /// <summary>
        /// 窗口关闭中，改为隐藏
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            this.Hide();
            e.Cancel = true;
            CacheUtil.Put("WindowState", this.WindowState);
        }

        /// <summary>
        /// 尺寸变化
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            CacheUtil.Put("WindowWidth", this.Width);
            CacheUtil.Put("WindowHeight", this.Height);
        }

        /// <summary>
        /// 状态变化
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_StateChanged(object sender, EventArgs e)
        {
            CacheUtil.Put("WindowState", this.WindowState);
        }
        #endregion

        #region 程序初始化
        /// <summary>
        /// 初始化应用信息
        /// </summary>
        private void InitializeAppInfo()
        {
            // 获取程序版本
            Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
			
			// 设置标题
            this.Title = string.Format("{0} v{1} (Released)", this.Title, version);
        }

        /// <summary>
        /// 初始化排序类型列表
        /// </summary>
        //private void InitializeSortType()
        //{
        //    TaskTime taskTime = TaskTime.StartNew();
        //    SortOptions.Items.Clear();

        //    // 🆕 原创：用包装类替代直接添加 enum
        //    SortOptions.Items.Add(new SortOptionItem { DisplayName = "Default Order", Value = SortType.Default });
        //    SortOptions.Items.Add(new SortOptionItem { DisplayName = "By Date (ASC)", Value = SortType.Date_ASC });
        //    SortOptions.Items.Add(new SortOptionItem { DisplayName = "By Date (DESC)", Value = SortType.Date_DESC });
        //    SortOptions.Items.Add(new SortOptionItem { DisplayName = "By Size (ASC)", Value = SortType.Size_ASC });
        //    SortOptions.Items.Add(new SortOptionItem { DisplayName = "By Size (DESC)", Value = SortType.Size_DESC });

        //    log.Debug("InitializeSortType Duration：" + taskTime.ConsumeTime + ".");
        //}

        private void InitializeSortType()
        {
            TaskTime taskTime = TaskTime.StartNew();

            var sortOptions = new List<SortOptionItem>
    {
        new SortOptionItem { DisplayName = "Default Order", Value = SortType.Default },
        new SortOptionItem { DisplayName = "By Date (ASC)", Value = SortType.Date_ASC },
        new SortOptionItem { DisplayName = "By Date (DESC)", Value = SortType.Date_DESC },
        new SortOptionItem { DisplayName = "By Size (ASC)", Value = SortType.Size_ASC },
        new SortOptionItem { DisplayName = "By Size (DESC)", Value = SortType.Size_DESC }
    };

            SortOptions.ItemsSource = sortOptions;
            SortOptions.SelectedIndex = 0;  // 默认选第一个

            log.Debug("InitializeSortType Duration：" + taskTime.ConsumeTime + ".");
        }


        /// <summary>
        /// 初始化搜索域
        /// </summary>
        private void InitializeSearchRegion()
        {
            TaskTime taskTime = TaskTime.StartNew();
            SearchScope.Items.Clear();

            // 显示用包装类替代 enum 直接显示
            SearchScope.Items.Add(new SearchRegionItem { DisplayName = "File Name and Content", Value = SearchRegion.FileNameAndContent });
            SearchScope.Items.Add(new SearchRegionItem { DisplayName = "File Name Only", Value = SearchRegion.FileNameOnly });
            SearchScope.Items.Add(new SearchRegionItem { DisplayName = "Content Only", Value = SearchRegion.ContentOnly });

            SearchScope.SelectedIndex = 0;

            log.Debug("InitializeSearchRegion Duration：" + taskTime.ConsumeTime + ".");
        }
        private void SearchScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SearchScope.SelectedItem is SearchRegionItem selectedRegion)
            {
                SearchRegion selectedValue = selectedRegion.Value;

                // ✅ 你可以在这里根据 selectedValue 做一些处理
                log.Debug("Selected search region: " + selectedValue.ToString());

                // 如果你需要在别的地方用这个值，也可以存成字段：
                // this.CurrentSearchRegion = selectedValue;
            }

            // 原来的逻辑
            BeforeSearch();
        }




        /// <summary>
        /// 初始化文件类型过滤器列表
        /// </summary>
        private void InitializeSearchFileType()
        {
            TaskTime taskTime = TaskTime.StartNew();
            // 文件类型筛选下拉框数据初始化
            SearchFileType.Children.Clear();
            // 遍历文件类型枚举
            foreach (FileType fileType in Enum.GetValues(typeof(FileType)))
            {
                // 构造UI元素
                RadioButton radioButton = new RadioButton()
                {
                    GroupName = "SearchFileType",
                    Name = "FileType" + fileType.ToString(),
                    Width = 80,
                    Margin = new Thickness(1),
                    Tag = fileType,
                    Content = fileType.ToString(),                    
                    IsChecked = fileType == FileType.All
                };
                if (fileType != FileType.All)
                {
                    radioButton.ToolTip = fileType.GetDescription();
                }
                radioButton.Checked += FileType_Checked;
                SearchFileType.Children.Add(radioButton);

                // 缓存全部，用于还原到默认值（因为默认选中全部）
                if (fileType == FileType.All)
                {
                    _radioButtonAll = radioButton;
                }
            }
            // 搜索筛选条件直接读取的当前值，初始化时默认赋值全部。其他选项修改时会更改此值
            SearchFileType.Tag = FileType.All;
            log.Debug("InitializeSearchFileTypes Duration：" + taskTime.ConsumeTime + "。");
        }

        /// <summary>
        /// 初始化配置文件信息
        /// </summary>
        public void InitializeAppConfig()
        {
            TaskTime taskTime = TaskTime.StartNew();

            // 启用的搜索区域信息显示
            List<Entity.AreaInfo> enableAreaInfos = AreaUtil.GetEnableAreaInfoList();
            string enableAreaNames = "";
            string enableAreaNameDescs = "";
            foreach (Entity.AreaInfo areaInfo in enableAreaInfos)
            {
                enableAreaNames += areaInfo.AreaName + "，";
                enableAreaNameDescs += areaInfo.AreaName + "：" + string.Join(",", areaInfo.AreaFolders.ToArray()) + "\r\n";
            }
            this.EnableAreaInfos.Text = enableAreaNames.Substring(0, enableAreaNames.Length - 1);
            this.EnableAreaInfos.ToolTip = enableAreaNameDescs.Substring(0, enableAreaNameDescs.Length - 2);

            // 未启用的搜索区域信息显示
            List<Entity.AreaInfo> disableAreaInfos = AreaUtil.GetDisableAreaInfoList();
            string disableAreaNames = "";
            string disableAreaNameDescs = "";
            foreach (Entity.AreaInfo areaInfo in disableAreaInfos)
            {
                disableAreaNames += areaInfo.AreaName + "，";
                disableAreaNameDescs += areaInfo.AreaName + "：" + string.Join(",", areaInfo.AreaFolders.ToArray()) + "\r\n";
            }
            this.DisableAreaInfos.Text = string.IsNullOrEmpty(disableAreaNames) ? disableAreaNames : disableAreaNames.Substring(0, disableAreaNames.Length - 1);
            if (!string.IsNullOrEmpty(disableAreaNameDescs))
            {
                this.DisableAreaInfos.ToolTip = disableAreaNameDescs.Substring(0, disableAreaNameDescs.Length - 2);
            }            

            // 读取分页每页显示条数
            if (string.IsNullOrEmpty(AppUtil.ReadValue("AppConfig", "ResultListPageSize", "")))
            {
                AppUtil.WriteValue("AppConfig", "ResultListPageSize", AppConst.MRESULT_LIST_PAGE_SIZE + "");
            }

            log.Debug("InitializeAppConfig Duration：" + taskTime.ConsumeTime + "。");
        }

        #endregion

        #region 热键注册
        /// <summary>
        /// 通知注册系统快捷键事件处理函数
        /// </summary>
        /// <param name="hotKeyModelList"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        private bool Instance_RegisterGlobalHotKeyEvent(System.Collections.ObjectModel.ObservableCollection<HotKeyModel> hotKeyModelList)
        {
            _ = InitHotKey(hotKeyModelList);
            return true;
        }

        /// <summary>
        /// 初始化注册快捷键
        /// </summary>
        /// <param name="hotKeyModelList">待注册热键的项</param>
        /// <returns>true:保存快捷键的值；false:弹出设置窗体</returns>
        private async Task<bool> InitHotKey(ObservableCollection<HotKeyModel> hotKeyModelList = null)
        {
            var list = hotKeyModelList ?? HotKeySettingManager.Instance.LoadDefaultHotKey();
            // 注册全局快捷键
            string failList = HotKeyHelper.RegisterGlobalHotKey(list, _hwnd, out _hotKeySettings);
            if (string.IsNullOrEmpty(failList))
                return true;

            var result = await MessageCore.ShowMessageBox(string.Format("The following hotkeys could not be registered: \r\n\r\n{0}Would you like to change these hotkeys?", failList), "Confirmation", MessageBoxButton.YesNo);
            // 弹出热键设置窗体
            var win = HotkeyWindow.CreateInstance();
            if (result == MessageBoxResult.Yes)
            {
                if (!win.IsVisible)
                {
                    win.Topmost = true;
                    win.ShowDialog();
                }
                else
                {
                    win.Activate();
                }
                return false;
            }
            return true;
        }

        /// <summary>
        /// 窗体回调函数，接收所有窗体消息的事件处理函数
        /// </summary>
        /// <param name="hWnd">窗口句柄</param>
        /// <param name="msg">消息</param>
        /// <param name="wideParam">附加参数1</param>
        /// <param name="longParam">附加参数2</param>
        /// <param name="handled">是否处理</param>
        /// <returns>返回句柄</returns>
        private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wideParam, IntPtr longParam, ref bool handled)
        {
            var hotKeySetting = new HotKeySetting();
            switch (msg)
            {
                case HotKeyManager.WM_HOTKEY:
                    int sid = wideParam.ToInt32();
                    // 显示
                    if (sid == _hotKeySettings[HotKeySetting.Show])
                    {
                        hotKeySetting = HotKeySetting.Show;

                        this.Show();
                        this.WindowState = WindowState.Normal;
                    }
                    // 隐藏
                    else if (sid == _hotKeySettings[HotKeySetting.Hide])
                    {
                        hotKeySetting = HotKeySetting.Hide;
                        this.Hide();
                    }
                    // 清空
                    else if (sid == _hotKeySettings[HotKeySetting.Clear])
                    {
                        hotKeySetting = HotKeySetting.Clear;
                        ResetSearchResult();
                    }
                    // 退出
                    else if (sid == _hotKeySettings[HotKeySetting.Exit])
                    {
                        hotKeySetting = HotKeySetting.Exit;
                        AppCore.Shutdown();
                    }
                    // 上一项
                    else if (sid == _hotKeySettings[HotKeySetting.Previous])
                    {
                        hotKeySetting = HotKeySetting.Previous;
                        Switch2Preview(HotKeySetting.Previous);
                    }
                    // 下一项
                    else if (sid == _hotKeySettings[HotKeySetting.Next])
                    {
                        hotKeySetting = HotKeySetting.Next;
                        Switch2Preview(HotKeySetting.Next);
                    }
                    log.Debug(string.Format("Hotkey【{0}】triggered", hotKeySetting));
                    handled = true;
                    break;
            }
            return IntPtr.Zero;
        }
        #endregion

        #region 关键词搜索
        /// <summary>
        /// 搜索
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            // 获取搜索关键词列表
            List<string> keywords = GetSearchTextKeywords();
            if (keywords.Count <= 0)
            {
                MessageCore.ShowWarning("Please enter the search keywords");
                return;
            }

            // ---- 搜索按钮时，下拉框和其他筛选条件全部恢复默认值
            // 取消精确检索
            PreciseRetrieval.IsChecked = false;
            // 取消匹配全词
            MatchWords.IsChecked = false;

            // 全部文件类型
            ToggleButtonAutomationPeer toggleButtonAutomationPeer = new ToggleButtonAutomationPeer(_radioButtonAll);
            IToggleProvider toggleProvider = toggleButtonAutomationPeer.GetPattern(PatternInterface.Toggle) as IToggleProvider;
            toggleProvider.Toggle();

            // 默认排序
            SortOptions.SelectedIndex = 0;
            // 文件名和内容
            // SearchScope.SelectedIndex = 0;

            BeforeSearch();
        }

        /// <summary>
        /// 关键词文本框回车搜索
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchText_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // ---- 光标移除文本框
                SearchText.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));

                // ---- 搜索按钮时，下拉框和其他筛选条件全部恢复默认值
                // 取消精确检索
                PreciseRetrieval.IsChecked = false;
                // 取消匹配全词
                MatchWords.IsChecked = false;

                // 全部文件类型
                ToggleButtonAutomationPeer toggleButtonAutomationPeer = new ToggleButtonAutomationPeer(_radioButtonAll);
                IToggleProvider toggleProvider = toggleButtonAutomationPeer.GetPattern(PatternInterface.Toggle) as IToggleProvider;
                toggleProvider.Toggle();

                // 默认排序
                SortOptions.SelectedIndex = 0;
                // 文件名和内容
                // SearchScope.SelectedIndex = 0;

                BeforeSearch();

                // 光标聚焦
                SearchText.Focus();
            }
        }

        /// <summary>
        /// 文本内容变化时
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchText_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 如果文本为空则隐藏清空按钮，如果不为空则显示清空按钮
            this.CleanButton.Visibility = this.SearchText.Text.Length > 0 ? Visibility.Visible : Visibility.Hidden;
            // 文本框颜色
            SearchTextBorder.BorderBrush = new SolidColorBrush(this.SearchText.Text.Length > 0 ? Colors.Green : (Color)ColorConverter.ConvertFromString("#2196f3"));
        }

        /// <summary>
        /// 搜索前
        /// </summary>
        /// <param name="page">指定页</param>
        private void BeforeSearch(int page = 1)
        {
            // 1、---- 搜索信息预处理
            // 还原分页count
            if (page != _viewModel.PageIndex)
            {
                _viewModel.PageIndex = page;
                // 设置分页标签总条数
                _viewModel.TotalCount = 0;
            }

            // 获取搜索关键词列表
            List<string> keywords = GetSearchTextKeywords();
            if (keywords.Count <= 0)
            {
                return;
            }


            // 2、---- 预览信息还原
            // 预览区打开文件和文件夹标记清空
            OpenFile.Tag = null;
            OpenFolder.Tag = null;

            // 预览文件名清空
            PreviewFileName.Text = "";

            // 预览文件内容清空
            PreviewFileContent.Document = null;

            // 预览图标清空
            PreviewImage.Source = null;

            // 预览文件类型图标清空
            PreviewFileTypeIcon.Source = null;

            // 预览切换标记清空
            SwitchPreview.Tag = null;


            // 3、---- 生成本次搜索时间戳
            _timestamp = Convert.ToInt64((DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalMilliseconds);

            SortOptionItem selectedSortItem = (SortOptionItem)SortOptions.SelectedItem;
            SearchRegionItem selectedSearchRegion = (SearchRegionItem)SearchScope.SelectedItem;

            _searchParam = new Entity.SearchParam()
            {
                Keywords = keywords,
                FileType = (FileType)SearchFileType.Tag,
                SortType = selectedSortItem.Value,                   // ✅ 用包装类提取 SortType 枚举值
                IsPreciseRetrieval = (bool)PreciseRetrieval.IsChecked,
                IsMatchWords = (bool)MatchWords.IsChecked,
                SearchRegion = selectedSearchRegion.Value,           // ✅ 用包装类提取 SearchRegion 枚举值
                PageSize = _viewModel.PageSize,
                PageIndex = _viewModel.PageIndex
            };


            // 5、---- 搜索
            Search(
                _timestamp,
                _searchParam
            );
        }

        /// <summary>
        /// 搜索
        /// </summary>
        /// <param name="timestamp">时间戳，用于校验为同一子任务；时间戳不相同表名父任务结束，子任务跳过执行</param>
        /// <param name="searchParam">搜索条件</param>
        private void Search(long timestamp, Entity.SearchParam searchParam)
        {
            if (!CheckIndexExist())
            {
                return;
            }

            ShowStatus("Searching in progress...");
            ShowSearchLoading();

            Thread t = new Thread(() =>
            {
                try
                {
                    // 1、---- 清空搜索结果列表
                    Dispatcher.Invoke(() =>
                    {
                        this.SearchResultList.Items.Clear();
                    });

                    // 2、---- 查询列表（参数，消息回调）
                    Entity.SearchResult searchResult = IndexCore.Search(searchParam, ShowStatus);

                    // 验证列表数据
                    if (null == searchResult || searchResult.Results.Count <= 0)
                    {
                        MessageCore.ShowWarning("No results found. Please adjust your search criteria.");
                        HideSearchLoading();
                        return;
                    }

                    // 3、---- 遍历结果
                    int index = 1;
                    foreach (Entity.FileInfo fileInfo in searchResult.Results)
                    {
                        if (_timestamp != timestamp)
                        {
                            return;
                        }
                        fileInfo.Index = index++;
                        Dispatcher.Invoke(() =>
                        {
                            this.SearchResultList.Items.Add(new FileInfoItem(fileInfo));
                        });
                    }

                    // 4、---- 分页总数、显示预览列表分页信息
                    _viewModel.TotalCount = searchResult.Total;
                    _viewModel.PreviewPage = string.Format("0/{0}", searchResult.Results.Count);
                    _viewModel.PreviewSwitchVisibility = searchResult.Total > 0 ? Visibility.Visible : Visibility.Hidden;
                }
                catch (Exception ex)
                {
                    log.Error("Search error：" + ex.Message, ex);
                }
                finally
                {
                    HideSearchLoading();
                }
            });
            t.Priority = ThreadPriority.Highest;
            t.Start();
        }
        #endregion

        #region 数据分页
        // 切换页码
        private void PageBar_PageIndexChanged(object sender, RoutedPropertyChangedEventArgs<int> e)
        {
            log.Debug($"pageIndex : {e.OldValue} => {e.NewValue}");

            BeforeSearch(e.NewValue);
        }

        private void PageBar_PageSizeChanged(object sender, RoutedPropertyChangedEventArgs<int> e)
        {
            log.Debug($"pageSize : {e.OldValue} => {e.NewValue}");

            _viewModel.PageSize = e.NewValue;
        }
        #endregion

        #region 列表排序
        /// <summary>
        /// 排序选中
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SortOptions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BeforeSearch(_viewModel.PageIndex);
        }
        #endregion

        #region 结果清空
        /// <summary>
        /// 清空按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CleanButton_Click(object sender, RoutedEventArgs e)
        {
            ResetSearchResult();
        }

        /// <summary>
        /// 清理查询结果
        /// </summary>
        private void ResetSearchResult()
        {
            // -------- 搜索框
            // 先清空搜索框
            SearchText.Text = "";
            // 光标移除文本框
            SearchText.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            // 光标聚焦
            SearchText.Focus();

            // -------- 筛选条件
            // 文件类型筛选取消选中
            ToggleButtonAutomationPeer toggleButtonAutomationPeer = new ToggleButtonAutomationPeer(_radioButtonAll);
            IToggleProvider toggleProvider = toggleButtonAutomationPeer.GetPattern(PatternInterface.Toggle) as IToggleProvider;
            toggleProvider.Toggle();

            // 取消精确检索
            PreciseRetrieval.IsChecked = false;
            // 取消匹配全词
            MatchWords.IsChecked = false;

            // 排序类型切换为默认
            SortOptions.SelectedIndex = 0;
            // 文件名和内容
            SearchScope.SelectedIndex = 0;

            // -------- 搜索结果列表
            // 搜索结果列表清空
            SearchResultList.Items.Clear();

            // -------- 右侧预览区
            // 右侧预览区，打开文件和文件夹标记清空
            OpenFile.Tag = null;
            OpenFolder.Tag = null;

            // 预览文件名清空
            PreviewFileName.Text = "";

            // 预览文件内容清空
            PreviewFileContent.Document = null;

            // 预览图片清空
            PreviewImage.Source = null;

            // 预览文件类型图标清空
            PreviewFileTypeIcon.Source = null;

            // -------- 分页标签
            // 还原为第一页
            _viewModel.PageIndex = 1;
            // 设置分页标签总条数
            _viewModel.TotalCount = 0;

            // -------- 快捷标签
            // 隐藏上一个和下一个切换面板
            this.SwitchPreview.Visibility = Visibility.Collapsed;

            // -------- 搜索参数
            _searchParam = null;

            // -------- 状态栏
            // 工作状态更新为就绪
            ShowStatus("Ready");
        }
        #endregion

        #region 数据列表
        /// <summary>
        /// 列表项被选中事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SearchResultList.SelectedIndex == -1)
            {
                return;
            }

            // 预览切换索引标记
            this.SwitchPreview.Tag = SearchResultList.SelectedIndex;
            // 显示预览分页信息
            _viewModel.PreviewPage = String.Format("{0}/{1}", this.SearchResultList.SelectedIndex + 1, SearchResultList.Items.Count);

            // 手动GC
            GC.Collect();
            GC.WaitForPendingFinalizers();

            FileInfoItem infoItem = SearchResultList.SelectedItem as FileInfoItem;
            Entity.FileInfo fileInfo = infoItem.Tag as Entity.FileInfo;

            // 根据文件类型显示图标
            PreviewFileTypeIcon.Source = FileUtil.GetFileIcon(fileInfo.FileType);
            PreviewFileName.Text = fileInfo.FileName;
            PreviewFileContent.Document = null;

            // 绑定打开文件和打开路径的Tag
            OpenFile.Tag = fileInfo.FilePath;
            OpenFolder.Tag = fileInfo.FilePath.Substring(0, fileInfo.FilePath.LastIndexOf("\\"));

            // 图片文件
            if (FileType.Image == FileTypeUtil.GetFileType(fileInfo.FilePath))
            {
                PreviewFileContent.Visibility = Visibility.Hidden;
                PreviewImage.Visibility = Visibility.Visible;
                Task.Factory.StartNew(() =>
                {
                    try
                    {
                        BitmapImage bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.StreamSource = new MemoryStream(File.ReadAllBytes(fileInfo.FilePath));
                        bi.EndInit();
                        bi.Freeze();

                        Dispatcher.InvokeAsync(() =>
                        {
                            PreviewImage.Source = bi;
                        });
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex.Message, ex);
                        try
                        {
                            Dispatcher.InvokeAsync(() =>
                            {
                                PreviewImage.Source = null;
                            });
                        }
                        catch { }
                    }
                });
            }
            else
            {
                PreviewImage.Visibility = Visibility.Hidden;
                PreviewFileContent.Visibility = Visibility.Visible;
                // 文件内容预览
                Task.Factory.StartNew(() =>
                {
                    try
                    {
                        // 方案一：通过工厂接口读取文档内容（为提高预览速度，现已放弃） -> FileInfoServiceFactory.GetFileContent(fileInfo.FilePath, true);
                        // 方案二：创建索引时写入内容到索引，预览时直接读取使用。
                        string content = fileInfo.Preview;

                        Dispatcher.InvokeAsync(() =>
                        {
                            // 预览摘要启用
                            if (AppConst.ENABLE_PREVIEW_SUMMARY)
                            {
                                FlowDocument document = FileContentUtil.GetHitBreviaryFlowDocument(content, fileInfo.Keywords, Colors.Red);
                                PreviewFileContent.Document = document;
                                PreviewFileContent.CanGoToPage(1);
                            }
                            else
                            {
                                // 填充数据
                                FileContentUtil.FillFlowDocument(PreviewFileContent, content, new SolidColorBrush(Colors.Black));
                                // 默认滚动到第一页
                                PreviewFileContent.CanGoToPage(1);
                                ScrollViewer sourceScrollViewer = PreviewFileContent.Template.FindName("PART_ContentHost", PreviewFileContent) as ScrollViewer;
                                if (sourceScrollViewer != null)
                                {
                                    sourceScrollViewer.ScrollToTop();
                                }
                                // 关键词高亮
                                FileContentUtil.FlowDocumentHighlight(
                                    PreviewFileContent,
                                    Colors.Red,
                                    fileInfo.Keywords
                                );
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex.Message, ex);
                    }
                });
            }
        }
        #endregion

        #region 功能事件

        /// <summary>
        /// 搜索域切换事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        //private void SearchScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
        //{
        //    BeforeSearch();
        //}
        /// <summary>
        /// 文件类型过滤器选中事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileType_Checked(object sender, RoutedEventArgs e)
        {
            if (!"All".Equals((sender as RadioButton).Content) && GetSearchTextKeywords().Count <= 0)
            {
                ResetSearchResult();
                return;
            }

            SearchFileType.Tag = (sender as RadioButton).Tag;

            BeforeSearch();
        }

        /// <summary>
        /// 匹配全词
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CheckChange(object sender, RoutedEventArgs e)
        {
            BeforeSearch();
        }

        /// <summary>
        /// 参数设置
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SettingButton_Click(object sender, RoutedEventArgs e)
        {
            var win = SettingWindow.CreateInstance();
            if (!win.IsVisible)
            {
                win.Topmost = true;
                win.Owner = this;
                win.ShowDialog();
            }
            else
            {
                win.Activate();
            }
        }

        /// <summary>
        /// 正则工具
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RegularToolButton_Click(object sender, RoutedEventArgs e)
        {
            MessageCore.ShowInfo("Function not yet available");
            /*var win = new RegularTool.MainWindow();
            if (!win.IsVisible)
            {
                win.Topmost = true;
                win.Owner = this;
                win.Width = this.Width;
                win.Height = this.Height;
                win.ShowDialog();
            }
            else
            {
                win.Activate();
            }*/
        }

        /// <summary>
        /// 优化按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void IndexUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (build)
            {
                MessageCore.ShowWarning("Index building is in progress, cannot execute repeatedly!");
                return;
            }
            build = true;

            ShowStatus("Starting to update the index, please wait...");

            _ = Task.Factory.StartNew(() =>
            {
                BuildIndex(false, false);
            });
        }

        /// <summary>
        /// 重建按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void IndexRebuildButton_Click(object sender, RoutedEventArgs e)
        {
            if (build)
            {
                MessageCore.ShowWarning("Index building is in progress, cannot execute repeatedly!");
                return;
            }
            if (CheckIndexExist(false))
            {
                var result = await MessageCore.ShowMessageBox("Are you sure you want to rebuild the index? It might take a while.！", "Confirmation");
                if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }

            if (build)
            {
                MessageCore.ShowWarning("Index building in progress, please wait.");
                return;
            }
            build = true;

            ShowStatus("Starting to rebuild the index, please wait...");

            _ = Task.Factory.StartNew(() =>
            {
                BuildIndex(true, false);
            });
        }

        /// <summary>
        /// 搜索区双击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AreaInfos_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            AreaWindow areaDialog = new AreaWindow();
            areaDialog.Owner = this;
            areaDialog.Topmost = true;
            areaDialog.ShowDialog();
            
			// 不管是否修改都刷新
            InitializeAppConfig();
        }

        /// <summary>
        /// 上一个
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnLast_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Switch2Preview(HotKeySetting.Previous);
        }

        /// <summary>
        /// 下一个
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnNext_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Switch2Preview(HotKeySetting.Next);
        }

        /// <summary>
        /// 切换预览，next为true，下一个；next为false，上一个
        /// </summary>
        /// <param name="next"></param>
        private void Switch2Preview(HotKeySetting setting)
        {
            // 当前索引 = 预览标记不为空 ? 使用标记 ： 默认值0
            int index = this.SwitchPreview.Tag != null ? int.Parse(this.SwitchPreview.Tag + "") : -1;

            // 搜索结果列表为空时，不能执行切换
            if (this.SearchResultList.Items.Count <= 0)
            {
                return;
            }

            // 下一个
            if (setting == HotKeySetting.Next && index < this.SearchResultList.Items.Count)
            {
                this.SearchResultList.SelectedIndex = index + 1;
            }
            // 上一个
            else if (setting == HotKeySetting.Previous && index > 0)
            {
                this.SearchResultList.SelectedIndex = index - 1;
            }

            // 显示分页信息
            _viewModel.PreviewPage = String.Format("{0}/{1}", this.SearchResultList.SelectedIndex + 1, SearchResultList.Items.Count);
        }
        #endregion

        #region 辅助方法
        /// <summary>
        /// 检查索引是否需要更新
        /// </summary>
        private void IndexUpdateTask()
        {
            // 方案一：定时器
            /*if (AppConst.INDEX_UPDATE_TASK_INTERVAL <= 5)
                AppConst.INDEX_UPDATE_TASK_INTERVAL = 5;

            System.Timers.Timer timer = new System.Timers.Timer();
            timer.Interval = AppConst.INDEX_UPDATE_TASK_INTERVAL * 60 * 1000;
            timer.Elapsed += Timer_Elapsed;
            timer.AutoReset = true;
            timer.Enabled = true;
            timer.Start();*/

            // 方案二：线程
            Task.Factory.StartNew(() =>
            {
                try
                {
                    while (AppConst.ENABLE_INDEX_UPDATE_TASK)
                    {
                        if (build)
                        {
                            log.Info("The last task has not been completed, skipping this task.");
                            return;
                        }
                        else
                        {
                            log.Info("Start executing index update check.");

                            build = true;

                            BuildIndex(false, true);
                        }

                        // 修复bug容错处理
                        if (AppConst.INDEX_UPDATE_TASK_INTERVAL <= 5)
                            AppConst.INDEX_UPDATE_TASK_INTERVAL = 5;

                        Thread.Sleep(TimeSpan.FromMinutes(AppConst.INDEX_UPDATE_TASK_INTERVAL));
                    }
                }
                catch (Exception ex)
                {
                    log.Error("Index update task execution error：" + ex.Message, ex);
                }
            });
        }

        /// <summary>
        /// 定时器执行逻辑
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (build)
            {
                log.Info("The last task has not been completed, skipping this task.");
            }
            else
            {
                log.Info("Start executing index update check.");

                build = true;

                BuildIndex(false, true);
            }
        }

        /// <summary>
        /// 检查索引是否存在
        /// </summary>
        /// <returns></returns>
        private bool CheckIndexExist(bool showWarning = true)
        {
            bool exists = Directory.Exists(AppConst.APP_INDEX_DIR);
            if (!exists)
            {
                if (showWarning)
                {
                    MessageCore.ShowWarning("First time use requires setting up the search area and rebuilding the index.");
                }
            }
            return exists;
        }

        /// <summary>
        /// 构建索引
        /// </summary>
        /// <param name="isRebuild">是否重建</param>
        /// <param name="isBackground">是否后台执行，默认前台执行</param>
        private void BuildIndex(bool isRebuild, bool isBackground = false)
        {
            try
            {
                // 提示语
                string tag = isRebuild ? "Rebuild" : "Update";

                // 1、-------- 定义总数
                // 文件总数
                int fileTotalCount = 0;
                // 更新总数
                int updateTotalCount = 0;
                // 删除总数
                int deleteTotalCount = 0;
                // 错误总数
                int errorTotalCount = 0;

                // 总任务消耗时间
                var totalTaskMark = TaskTime.StartNew();

                // 2、-------- 遍历搜索区
                List<Entity.AreaInfo> areaInfos = AreaUtil.GetEnableAreaInfoList();
                int areaInfosCount = areaInfos.Count;
                for (int i = 0; i < areaInfosCount; i++)
                {
                    Entity.AreaInfo areaInfo = areaInfos[i];

                    var singleTaskMark = TaskTime.StartNew();

                    // 不同区域，索引分开记录
                    string areaIdIndex = areaInfo.AreaId + "Index";

                    // 重建则删除全部标记
                    if (isRebuild)
                    {
                        // 重建时，删除全部标记
                        AppUtil.DeleteSection(areaIdIndex);
                    }

                    // 2.1、-------- 开始获取文件列表
                    string msg = string.Format("Search area【{0}】，starting to scan files...", areaInfo.AreaName);
                    log.Info(msg);
                    ShowStatus(msg);

                    // 定义全部文件列表
                    List<string> allFilePaths = new List<string>();
                    // 定义更新文件列表
                    List<string> updateFilePaths = new List<string>();
                    // 定义删除文件列表
                    List<string> deleteFilePaths = new List<string>();

                    // 2.2、-------- 获取支持的文件类型后缀（根据不同区域配置的支持文件类型查找对应的文件列表）
                    Regex fileExtRegex = RegexUtil.BuildRegex(@"^.+\.(" + FileTypeUtil.ConvertToFileTypeExts(areaInfo.AreaFileTypes, "|") + ")$"); //new Regex(@"^.+\.(" + FileTypeUtil.ConvertToFileTypeExts(areaInfo.AreaFileTypes, "|") + ")$");

                    var scanTaskMark = TaskTime.StartNew();
                    // 扫描需要建立索引的文件列表
                    foreach (string s in areaInfo.AreaFolders)
                    {
                        log.Info("Catalog：" + s);
                        // 获取文件信息列表
                        FileUtil.GetAllFiles(allFilePaths, s, fileExtRegex);
                    }

                    msg = string.Format("search area【{0}】，file scanning completed；file num：{1}，duration：{2}；Starting to analyze the list of files that need to be updated...", areaInfo.AreaName, allFilePaths.Count, scanTaskMark.ConsumeTime);
                    log.Info(msg);
                    ShowStatus(msg);

                    var analysisTaskMark = TaskTime.StartNew();
                    // 2.3、-------- 获取需要删除的文件列表
                    if (AppUtil.ReadSectionList(areaIdIndex) != null)
                    {
                        foreach (string filePath in AppUtil.ReadSectionList(areaIdIndex))
                        {
                            // 不存在，则表示文件已删除
                            if (!allFilePaths.Contains(filePath))
                            {
                                deleteFilePaths.Add(filePath);
                                AppUtil.WriteValue(areaIdIndex, filePath, null);
                            }
                        }
                    }

                    // 2.4、-------- 如果是更新操作，判断文件格式是否变化 -> 判断文件更新时间变化找到最终需要更新的文件列表
                    // 更新是才需要校验，重建是直接跳过
                    if (!isRebuild)
                    {
                        // 更新：需要更新的文件列表
                        foreach (string filePath in allFilePaths)
                        {
                            try
                            {
                                FileInfo fileInfo = new FileInfo(filePath);
                                // 当前文件修改时间
                                string lastWriteTime = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss.ffff");
                                // 上次索引时文件修改时间标记
                                string lastWriteTimeTag = AppUtil.ReadValue(areaIdIndex, filePath);

                                // 文件修改时间不一致，说明文件已修改
                                if (!lastWriteTime.Equals(lastWriteTimeTag))
                                {
                                    updateFilePaths.Add(filePath);
                                }
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        // 重建：全部文件列表
                        updateFilePaths.AddRange(allFilePaths);
                    }

                    msg = string.Format("search area 【{0}】，file analysis completed；{1} num：{2}，deleted：{3}，duration：{4}；starting to {5} index...", areaInfo.AreaName, tag, updateFilePaths.Count, deleteFilePaths.Count, analysisTaskMark.ConsumeTime, tag);
                    log.Info(msg);
                    ShowStatus(msg);

                    // 2.5、-------- 验证扫描文件列表是否为空（如果是更新操作，判断文件格式是否变化 -> 判断文件更新时间变化找到最终需要更新的文件列表）
                    if (updateFilePaths.Count <= 0 && deleteFilePaths.Count <= 0)
                    {
                        build = false;
                        msg = string.Format("search area【{0}】， no updated files and deleted files，do not {1} index...", areaInfo.AreaName, tag);
                        log.Info(msg);
                        ShowStatus(msg);
                        continue;
                    }

                    // 后台执行时修改为最小线程单位，反之恢复为系统配置线程数
                    AppCore.SetThreadPoolSize(!isBackground);

                    // 2.6、-------- 创建索引方法
                    Entity.CreareIndexParam creareParam = new Entity.CreareIndexParam()
                    {
                        AreaId = areaInfo.AreaId,
                        AreaIndex = i,
                        AreasCount = areaInfosCount,
                        UpdateFilePaths = updateFilePaths,
                        DeleteFilePaths = deleteFilePaths,
                        IsRebuild = isRebuild,
                        Callback = ShowStatus
                    };
                    int errorCount = IndexCore.CreateIndex(creareParam);

                    // 2.7、-------- 当前区域完成日志
                    msg = string.Format("search area【{0}】，index {1} completed；{2} num：{3}，deleted：{4}，error：{5}，duration：{6}.", areaInfo.AreaName, tag, tag, updateFilePaths.Count, deleteFilePaths.Count, errorCount, singleTaskMark.ConsumeTime);
                    log.Info(msg);
                    ShowStatus(msg);

                    MessageCore.ShowSuccess(msg);

                    // 2.8、-------- 记录文件总数、更新总数、删除总数、错误总数
                    fileTotalCount = fileTotalCount + allFilePaths.Count;
                    updateTotalCount = updateTotalCount + updateFilePaths.Count;
                    deleteTotalCount = deleteTotalCount + deleteFilePaths.Count;
                    errorTotalCount = errorTotalCount + errorCount;
                }

                // 3、-------- 完成日志
                string message = string.Format("index {0} completed. regions：{1}，{2} num：{3}，deleted：{4}，error：{5}，duration：{6}.", tag, areaInfos.Count, tag, updateTotalCount, deleteTotalCount, errorTotalCount, totalTaskMark.ConsumeTime);
                log.Info(message);
                ShowStatus(message);

                // 4、-------- 标记索引文件数量 和 最后更新时间
                AppUtil.WriteValue("AppConfig", "FileTotalCount", fileTotalCount + "");
                AppUtil.WriteValue("AppConfig", "LastIndexTime", DateTime.Now.ToString());

                // 5、-------- 构建结束
                build = false;
            }
            catch (Exception ex)
            {
                log.Error("Index construction error：" + ex.Message, ex);

                build = false;
            }
        }

        /// <summary>
        /// 显示状态
        /// </summary>
        /// <param name="text">消息</param>
        /// <param name="percent">进度，0-100</param>
        private void ShowStatus(string text, double percent = AppConst.MAX_PERCENT)
        {
            void Refresh()
            {
                WorkStatus.Text = text;
                TaskbarInfo.ProgressState = percent < AppConst.MAX_PERCENT ? System.Windows.Shell.TaskbarItemProgressState.Normal : System.Windows.Shell.TaskbarItemProgressState.None;
                if (percent > AppConst.MIN_PERCENT)
                {
                    WorkProgress.Value = percent;
                    TaskbarInfo.ProgressValue = percent / 100;
                }
            }
            try
            {
                Refresh();
            }
            catch
            {
                Dispatcher.InvokeAsync(() =>
                {
                    Refresh();
                });
            }
        }
        #endregion

        #region 右侧预览区域
        /// <summary>
        /// 打开文件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenFile_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (OpenFile.Tag != null)
            {
                string filePath = OpenFile.Tag + "";
                try
                {
                    System.Diagnostics.Process.Start(filePath);
                }
                catch (Exception ex)
                {
                    log.Error("Failed to open the file：" + ex.Message, ex);
                }
            }
        }

        /// <summary>
        /// 打开文件夹
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenFolder_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (OpenFolder.Tag != null)
            {
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", @"/select," + OpenFile.Tag);
                }
                catch (Exception ex)
                {
                    log.Error(ex.Message, ex);
                    try
                    {
                        System.Diagnostics.Process.Start("explorer.exe", @"" + OpenFolder.Tag);
                    }
                    catch (Exception ex1)
                    {
                        log.Error(ex1.Message, ex1);
                    }
                }
            }
        }
        #endregion

        #region 其他私有封装
        /// <summary>
        /// 获取文本关键词
        /// </summary>
        /// <returns></returns>
        private List<string> GetSearchTextKeywords()
        {
            string searchText = SearchText.Text.Trim();
            // 申明关键词列表
            List<string> keywords = new List<string>();
            // 为空直接返回null
            if (string.IsNullOrEmpty(searchText)) return keywords;

            // 精确检索未选中 || 非正则表达式
            if (PreciseRetrieval.IsChecked == false || !searchText.StartsWith(AppConst.REGEX_SEARCH_PREFIX))
            {
                // 替换内置（AND|OR|NOT|\\&\\&|\\|\\||\"|\\~|\\:）特殊字符
                searchText = AppConst.REGEX_BUILT_IN_SYMBOL.Replace(searchText, " ");
            }

            // 精确检索 || 正则表达式
            if (PreciseRetrieval.IsChecked == true || searchText.StartsWith(AppConst.REGEX_SEARCH_PREFIX))
            {
                keywords.Add(searchText);
            }
            // 空格分词
            else if (searchText.IndexOf(" ") != -1)
            {
                string[] texts = searchText.Split(' ');
                foreach (string keyword in texts)
                {
                    if (string.IsNullOrEmpty(keyword))
                    {
                        continue;
                    }
                    keywords.Add(keyword);
                }
            }
            // 分词器自动分词
            else
            {
                // 分词列表
                List<string> segmentList = IndexCore.GetKeywords(searchText);//AppConst.INDEX_SEGMENTER.CutForSearch(searchText).ToList();
                // 合并关键列表
                keywords = keywords.Union(segmentList).ToList();
            }
            return keywords;
        }
        #endregion

        #region Loading

        /// <summary>
        /// 显示搜索Loading
        /// </summary>
        private void ShowSearchLoading()
        {
            Dispatcher.Invoke(new Action(() =>
            {
                this._searchLoading.Visibility = Visibility.Visible;
            }));
        }
        /// <summary>
        /// 隐藏搜索Loading
        /// </summary>
        private void HideSearchLoading()
        {
            Dispatcher.Invoke(new Action(() =>
            {
                this._searchLoading.Visibility = Visibility.Collapsed;
            }));
        }
        #endregion

        private void BackToAISearch_Click(object sender, RoutedEventArgs e)
        {
            // ① 隐藏自己，别 Close（Close 会触发 HotKey 注销等一堆逻辑）
    this.Hide();

    // ② 若 AIPage 已经开过就激活；否则新建
    if (Application.Current.Windows
                       .OfType<AIPage>()
                       .FirstOrDefault() is AIPage aiWin)
    {
        aiWin.Show();
        aiWin.Activate();
    }
    else
    {
                AIPage AIPage = new AIPage();
                AIPage.Show();
    }
        }

        public void PerformSearchWithKeywords(string[] keywords)
        {
            if (keywords == null || keywords.Length == 0)
                return;

            // 1. 组合成查询
            string combinedQuery = string.Join(" ", keywords);

            // 2. 设到 UI 上
            SearchText.Text = combinedQuery;

            // 3. 恢复和点击搜索时一样的默认状态（和 SearchButton_Click 里前半部分一致）
            PreciseRetrieval.IsChecked = false;
            MatchWords.IsChecked = false;

            // 全部文件类型
            ToggleButtonAutomationPeer toggleButtonAutomationPeer = new ToggleButtonAutomationPeer(_radioButtonAll);
            IToggleProvider toggleProvider = toggleButtonAutomationPeer.GetPattern(PatternInterface.Toggle) as IToggleProvider;
            toggleProvider.Toggle();

            // 默认排序
            SortOptions.SelectedIndex = 0;

            // 4. 真正执行搜索（默认第一页）
            BeforeSearch();
        }

    }
}
