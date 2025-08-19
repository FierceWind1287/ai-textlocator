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
    /// The interaction logic of MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// all
        /// </summary>
        private RadioButton _radioButtonAll;
        /// <summary>
        /// timestamp
        /// </summary>
        private long _timestamp;
        /// <summary>
        /// searchParam
        /// </summary>
        private Entity.SearchParam _searchParam;
        /// <summary>
        /// index building flag
        /// </summary>
        private static volatile bool build = false;

        /// <summary>
        /// viewModel
        /// </summary>
        private MainViewModel _viewModel = new MainViewModel();

        #region hotkey
        /// <summary>
        /// current window handle
        /// </summary>
        private IntPtr _hwnd = new IntPtr();
        /// <summary>
        /// registered hotkey settings
        /// </summary>
        private Dictionary<HotKeySetting, int> _hotKeySettings = new Dictionary<HotKeySetting, int>();
        #endregion


        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = _viewModel;

        }

        #region window initialization
        /// <summary>
        /// The resource initialization of the WPF window is complete, and the handle of the window can be obtained through WindowInteropHelper for Win32 interaction.
        /// </summary>
        /// <param name="e"></param>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // get the window handle
            _hwnd = new WindowInteropHelper(this).Handle;
            HwndSource hWndSource = HwndSource.FromHwnd(_hwnd);
            // add a hook to the window message processing function
            if (hWndSource != null) hWndSource.AddHook(WndProc);
        }

        /// <summary>
        /// Call after all controls are initialized.
        /// </summary>
        /// <param name="e"></param>
        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            // register hotkey
            _ = InitHotKey();
        }

        /// <summary>
        /// Loading completed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize application information
            InitializeAppInfo();

            // Initialize configuration file information
            InitializeAppConfig();

            // Initialize file type list
            InitializeSearchFileType();

            // Initialize the sorting type list
            InitializeSortType();

            // Initialize the search domain list
            InitializeSearchRegion();

            // Cleanup Event
            ResetSearchResult();

            // Check if the index exists: if it exists, perform the update check; if it does not exist, skip the update check.
            if (CheckIndexExist(false))
            {
                // The software executes the index update logic each time it starts.
                IndexUpdateTask();
            }

            // Register global hotkey time
            HotKeySettingManager.Instance.RegisterGlobalHotKeyEvent += Instance_RegisterGlobalHotKeyEvent;
        }

        /// <summary>
        /// Window Activation
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Activated(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = CacheUtil.Get<WindowState>("WindowState");
        }

        /// <summary>
        /// The window is closing, changing to hidden.
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
        /// Size change
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            CacheUtil.Put("WindowWidth", this.Width);
            CacheUtil.Put("WindowHeight", this.Height);
        }

        /// <summary>
        /// State change
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_StateChanged(object sender, EventArgs e)
        {
            CacheUtil.Put("WindowState", this.WindowState);
        }
        #endregion

        #region Initialize application 
        /// <summary>
        /// Initialize application information
        /// </summary>
        private void InitializeAppInfo()
        {
            // Get program version
            Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

            // Set title
            this.Title = string.Format("{0} v{1} (Released)", this.Title, version);
        }

        /// <summary>
        /// Initialize the sorting type list
        /// </summary>


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
            SortOptions.SelectedIndex = 0;  // choose the first item by default

            log.Debug("InitializeSortType Duration：" + taskTime.ConsumeTime + ".");
        }


        /// <summary>
        /// Initialize search domain
        /// </summary>
        private void InitializeSearchRegion()
        {
            TaskTime taskTime = TaskTime.StartNew();
            SearchScope.Items.Clear();

            // Use packaging classes to replace enum
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

                
                log.Debug("Selected search region: " + selectedValue.ToString());

            }

            BeforeSearch();
        }




        /// <summary>
        /// Initialize the file type filter list
        /// </summary>
        private void InitializeSearchFileType()
        {
            TaskTime taskTime = TaskTime.StartNew();
            // File type filter dropdown data initialization
            SearchFileType.Children.Clear();
            // Traverse file type enumeration
            foreach (FileType fileType in Enum.GetValues(typeof(FileType)))
            {
                // Construct UI elements
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

                // Cache all, used to restore to default values
                if (fileType == FileType.All)
                {
                    _radioButtonAll = radioButton;
                }
            }
            // The current value directly read from the search filter conditions is initialized with the default value of all. 
            SearchFileType.Tag = FileType.All;
            log.Debug("InitializeSearchFileTypes Duration：" + taskTime.ConsumeTime + "。");
        }

        /// <summary>
        /// Initialize configuration file information
        /// </summary>
        public void InitializeAppConfig()
        {
            TaskTime taskTime = TaskTime.StartNew();

            // Show enabled search area information
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

            // Show disabled search area information
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

            // Read the number of items displayed per page in pagination.
            if (string.IsNullOrEmpty(AppUtil.ReadValue("AppConfig", "ResultListPageSize", "")))
            {
                AppUtil.WriteValue("AppConfig", "ResultListPageSize", AppConst.MRESULT_LIST_PAGE_SIZE + "");
            }

            log.Debug("InitializeAppConfig Duration：" + taskTime.ConsumeTime + "。");
        }

        #endregion

        #region hotkey registration
        /// <summary>
        /// Notify the registration system shortcut key event handling function
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
        /// Initialize registration shortcut key
        /// </summary>
        /// <param name="hotKeyModelList">Item to register hotkeys</param>
        /// <returns>true: Save the value of the shortcut key; false: Pop up the settings window.</returns>
        private async Task<bool> InitHotKey(ObservableCollection<HotKeyModel> hotKeyModelList = null)
        {
            var list = hotKeyModelList ?? HotKeySettingManager.Instance.LoadDefaultHotKey();
            // Register global hotkeys
            string failList = HotKeyHelper.RegisterGlobalHotKey(list, _hwnd, out _hotKeySettings);
            if (string.IsNullOrEmpty(failList))
                return true;

            var result = await MessageCore.ShowMessageBox(string.Format("The following hotkeys could not be registered: \r\n\r\n{0}Would you like to change these hotkeys?", failList), "Confirmation", MessageBoxButton.YesNo);
            // Pop up the settings window
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
        /// Window callback function, an event handler that receives all window messages.
        /// </summary>
        /// <param name="hWnd">Window handle</param>
        /// <param name="msg">information</param>
        /// <param name="wideParam">Additional parameter 1</param>
        /// <param name="longParam">Additional parameter 2</param>
        /// <param name="handled">processed or not</param>
        /// <returns>return handle</returns>
        private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wideParam, IntPtr longParam, ref bool handled)
        {
            var hotKeySetting = new HotKeySetting();
            switch (msg)
            {
                case HotKeyManager.WM_HOTKEY:
                    int sid = wideParam.ToInt32();
                    // show
                    if (sid == _hotKeySettings[HotKeySetting.Show])
                    {
                        hotKeySetting = HotKeySetting.Show;

                        this.Show();
                        this.WindowState = WindowState.Normal;
                    }
                    // Hide
                    else if (sid == _hotKeySettings[HotKeySetting.Hide])
                    {
                        hotKeySetting = HotKeySetting.Hide;
                        this.Hide();
                    }
                    // clean
                    else if (sid == _hotKeySettings[HotKeySetting.Clear])
                    {
                        hotKeySetting = HotKeySetting.Clear;
                        ResetSearchResult();
                    }
                    // exit
                    else if (sid == _hotKeySettings[HotKeySetting.Exit])
                    {
                        hotKeySetting = HotKeySetting.Exit;
                        AppCore.Shutdown();
                    }
                    // previous
                    else if (sid == _hotKeySettings[HotKeySetting.Previous])
                    {
                        hotKeySetting = HotKeySetting.Previous;
                        Switch2Preview(HotKeySetting.Previous);
                    }
                    // next
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

        #region Keyword search
        /// <summary>
        /// search
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            // Get the list of search keywords
            List<string> keywords = GetSearchTextKeywords();
            if (keywords.Count <= 0)
            {
                MessageCore.ShowWarning("Please enter the search keywords");
                return;
            }

            //---- When the search button is clicked, the dropdown box and all other filter conditions revert to their default values.
            // Cancel precise search
            PreciseRetrieval.IsChecked = false;
            // Cancel matching the whole word
            MatchWords.IsChecked = false;

            // All file types
            ToggleButtonAutomationPeer toggleButtonAutomationPeer = new ToggleButtonAutomationPeer(_radioButtonAll);
            IToggleProvider toggleProvider = toggleButtonAutomationPeer.GetPattern(PatternInterface.Toggle) as IToggleProvider;
            toggleProvider.Toggle();

            // Default sorting
            SortOptions.SelectedIndex = 0;
            // File name and content
            // SearchScope.SelectedIndex = 0;

            BeforeSearch();
        }

        /// <summary>
        /// Press Enter in the keyword text box to search
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchText_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // ---- mouse move focus out of the text box
                SearchText.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));

                // ---- When the search button is clicked, the dropdown box and all other filter conditions revert to their default values.
                // Cancel precise search
                PreciseRetrieval.IsChecked = false;
                // Cancel matching the whole word
                MatchWords.IsChecked = false;

                // All file types
                ToggleButtonAutomationPeer toggleButtonAutomationPeer = new ToggleButtonAutomationPeer(_radioButtonAll);
                IToggleProvider toggleProvider = toggleButtonAutomationPeer.GetPattern(PatternInterface.Toggle) as IToggleProvider;
                toggleProvider.Toggle();

                // Default sorting
                SortOptions.SelectedIndex = 0;
                // File name and content
                // SearchScope.SelectedIndex = 0;

                BeforeSearch();

                // mouse focus
                SearchText.Focus();
            }
        }

        /// <summary>
        /// when text content changes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchText_TextChanged(object sender, TextChangedEventArgs e)
        {
            // If the textblock is empty, hide the clear button; if not, show the clear button.
            this.CleanButton.Visibility = this.SearchText.Text.Length > 0 ? Visibility.Visible : Visibility.Hidden;
            // Textbox color
            SearchTextBorder.BorderBrush = new SolidColorBrush(this.SearchText.Text.Length > 0 ? Colors.Green : (Color)ColorConverter.ConvertFromString("#2196f3"));
        }

        /// <summary>
        /// before search
        /// </summary>
        /// <param name="page">Designated page</param>
        private void BeforeSearch(int page = 1)
        {
            // 1、---- Search information preprocessing
            // Restore pagination count
            if (page != _viewModel.PageIndex)
            {
                _viewModel.PageIndex = page;
                // Set the total number of pagination tags
                _viewModel.TotalCount = 0;
            }

            // Get the list of search keywords
            List<string> keywords = GetSearchTextKeywords();
            if (keywords.Count <= 0)
            {
                return;
            }


            // 2、---- Preview information restoration
            // clean tag
            OpenFile.Tag = null;
            OpenFolder.Tag = null;

            // clean preview file name
            PreviewFileName.Text = "";

            // clean preview file content
            PreviewFileContent.Document = null;

            // clean preview icon
            PreviewImage.Source = null;

            // clean preview file type icon
            PreviewFileTypeIcon.Source = null;

            //  clean switch preview tag
            SwitchPreview.Tag = null;


            // 3、---- Generate the timestamp for this search.
            _timestamp = Convert.ToInt64((DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalMilliseconds);

            SortOptionItem selectedSortItem = (SortOptionItem)SortOptions.SelectedItem;
            SearchRegionItem selectedSearchRegion = (SearchRegionItem)SearchScope.SelectedItem;

            _searchParam = new Entity.SearchParam()
            {
                Keywords = keywords,
                FileType = (FileType)SearchFileType.Tag,
                SortType = selectedSortItem.Value,                   // Extract SortType enumeration value
                IsPreciseRetrieval = (bool)PreciseRetrieval.IsChecked,
                IsMatchWords = (bool)MatchWords.IsChecked,
                SearchRegion = selectedSearchRegion.Value,           // Extract SearchRegion enumeration value
                PageSize = _viewModel.PageSize,
                PageIndex = _viewModel.PageIndex
            };


            // 5、---- search
            Search(
                _timestamp,
                _searchParam
            );
        }

        /// <summary>
        /// search
        /// </summary>
        /// <param name="timestamp">Timestamp used to verify the same subtask; if the timestamps are different, it indicates that the parent task has ended and the subtask is skipped.</param>
        /// <param name="searchParam">search condition</param>
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
                    // 1、---- Clear the search results list
                    Dispatcher.Invoke(() =>
                    {
                        this.SearchResultList.Items.Clear();
                    });

                    // 2、---- Query List (Parameters, Message Callback)
                    Entity.SearchResult searchResult = IndexCore.Search(searchParam, ShowStatus);

                    // Verify list data
                    if (null == searchResult || searchResult.Results.Count <= 0)
                    {
                        MessageCore.ShowWarning("No results found. Please adjust your search criteria.");
                        HideSearchLoading();
                        return;
                    }

                    // 3、---- Traversal result
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

                    // 4、---- Total number of pages, preview list paging information
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
        // Switch page number
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

        #region List sorting
        /// <summary>
        /// Sort selected
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SortOptions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BeforeSearch(_viewModel.PageIndex);
        }
        #endregion

        #region Clear results
        /// <summary>
        /// clear btn
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CleanButton_Click(object sender, RoutedEventArgs e)
        {
            ResetSearchResult();
        }

        /// <summary>
        /// Clear query results
        /// </summary>
        private void ResetSearchResult()
        {
            // -------- search box
            // clear the search box.
            SearchText.Text = "";
            // mouse move out of the text box
            SearchText.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            // mouse focus
            SearchText.Focus();

            // -------- Filter criteria
            // Cancel the selection of file type filter
            ToggleButtonAutomationPeer toggleButtonAutomationPeer = new ToggleButtonAutomationPeer(_radioButtonAll);
            IToggleProvider toggleProvider = toggleButtonAutomationPeer.GetPattern(PatternInterface.Toggle) as IToggleProvider;
            toggleProvider.Toggle();

            // Cancel precise search
            PreciseRetrieval.IsChecked = false;
            // Cancel matching the whole word
            MatchWords.IsChecked = false;

            // Switch the sorting type to default order
            SortOptions.SelectedIndex = 0;
            // filename and content
            SearchScope.SelectedIndex = 0;

            // -------- Search Results List
            // Clear the search results list
            SearchResultList.Items.Clear();

            // -------- Right preview area
            // clear tag
            OpenFile.Tag = null;
            OpenFolder.Tag = null;

            //clear all
            PreviewFileName.Text = "";

            
            PreviewFileContent.Document = null;

            
            PreviewImage.Source = null;

            
            PreviewFileTypeIcon.Source = null;

            // -------- Pagination label
            // Restore to the first page
            _viewModel.PageIndex = 1;
            // Set the total number of entries for pagination
            _viewModel.TotalCount = 0;

            // -------- Quick Labels
            // Hide the previous and next panels
            this.SwitchPreview.Visibility = Visibility.Collapsed;

            // -------- searchparam
            _searchParam = null;

            // -------- statud
            // update status to ready
            ShowStatus("Ready");
        }
        #endregion

        #region Data list
        /// <summary>
        /// List item selection event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SearchResultList.SelectedIndex == -1)
            {
                return;
            }

            // Preview switch index tag
            this.SwitchPreview.Tag = SearchResultList.SelectedIndex;
            // Display preview pagination information
            _viewModel.PreviewPage = String.Format("{0}/{1}", this.SearchResultList.SelectedIndex + 1, SearchResultList.Items.Count);

            // GC manually
            GC.Collect();
            GC.WaitForPendingFinalizers();

            FileInfoItem infoItem = SearchResultList.SelectedItem as FileInfoItem;
            Entity.FileInfo fileInfo = infoItem.Tag as Entity.FileInfo;

            // Display icons according to file type
            PreviewFileTypeIcon.Source = FileUtil.GetFileIcon(fileInfo.FileType);
            PreviewFileName.Text = fileInfo.FileName;
            PreviewFileContent.Document = null;

            // Bind the tag for open file and open path
            OpenFile.Tag = fileInfo.FilePath;
            OpenFolder.Tag = fileInfo.FilePath.Substring(0, fileInfo.FilePath.LastIndexOf("\\"));

            // image file 
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
                // file content preview
                Task.Factory.StartNew(() =>
                {
                    try
                    {
                        
                        string content = fileInfo.Preview;

                        Dispatcher.InvokeAsync(() =>
                        {
                            // Enable preview summary
                            if (AppConst.ENABLE_PREVIEW_SUMMARY)
                            {
                                FlowDocument document = FileContentUtil.GetHitBreviaryFlowDocument(content, fileInfo.Keywords, Colors.Red);
                                PreviewFileContent.Document = document;
                                PreviewFileContent.CanGoToPage(1);
                            }
                            else
                            {
                                // fill flow document
                                FileContentUtil.FillFlowDocument(PreviewFileContent, content, new SolidColorBrush(Colors.Black));
                                // go to the first page by default
                                PreviewFileContent.CanGoToPage(1);
                                ScrollViewer sourceScrollViewer = PreviewFileContent.Template.FindName("PART_ContentHost", PreviewFileContent) as ScrollViewer;
                                if (sourceScrollViewer != null)
                                {
                                    sourceScrollViewer.ScrollToTop();
                                }
                                // highlight keywords
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

        #region functions event

        /// <summary>
        /// switch search scope
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        //private void SearchScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
        //{
        //    BeforeSearch();
        //}
        /// <summary>
        /// file type filter 
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
        /// match whole words
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CheckChange(object sender, RoutedEventArgs e)
        {
            BeforeSearch();
        }

        /// <summary>
        /// Parameter settings
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
        /// Regex tool 
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
        /// Optimize button
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
        /// Rebuilt btn
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
        /// double click search area
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AreaInfos_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            AreaWindow areaDialog = new AreaWindow();
            areaDialog.Owner = this;
            areaDialog.Topmost = true;
            areaDialog.ShowDialog();

            // Refresh whether modified or not.
            InitializeAppConfig();
        }

        /// <summary>
        /// previous
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnLast_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Switch2Preview(HotKeySetting.Previous);
        }

        /// <summary>
        /// next
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnNext_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Switch2Preview(HotKeySetting.Next);
        }

        /// <summary>
        /// Switch preview, if next is true, go to the next one; if next is false, go to the previous one.
        /// </summary>
        /// <param name="next"></param>
        private void Switch2Preview(HotKeySetting setting)
        {
            // Current index = preview marker is not empty ? use tag : default value 0
            int index = this.SwitchPreview.Tag != null ? int.Parse(this.SwitchPreview.Tag + "") : -1;

            // Cannot switch when the search result list is empty.
            if (this.SearchResultList.Items.Count <= 0)
            {
                return;
            }

            // next
            if (setting == HotKeySetting.Next && index < this.SearchResultList.Items.Count)
            {
                this.SearchResultList.SelectedIndex = index + 1;
            }
            // previous
            else if (setting == HotKeySetting.Previous && index > 0)
            {
                this.SearchResultList.SelectedIndex = index - 1;
            }

            // Display pagination information
            _viewModel.PreviewPage = String.Format("{0}/{1}", this.SearchResultList.SelectedIndex + 1, SearchResultList.Items.Count);
        }
        #endregion

        #region Auxiliary method
        /// <summary>
        /// Check if the index needs to be updated
        /// </summary>
        private void IndexUpdateTask()
        {
          
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
                        //fault tolerance
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
        /// Timer execution logic
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
        /// Check if the index exists
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
        /// construct index
        /// </summary>
        /// <param name="isRebuild">rebuilt or not</param>
        /// <param name="isBackground">Whether to execute in the background, default is foreground execution.</param>
        private void BuildIndex(bool isRebuild, bool isBackground = false)
        {
            try
            {
                // prompt
                string tag = isRebuild ? "Rebuild" : "Update";

                // 1、-------- define the total num
                // Total number of documents
                int fileTotalCount = 0;
                // Total number of updated documents
                int updateTotalCount = 0;
                // Total number of deleted documents
                int deleteTotalCount = 0;
                // Total number of error documents
                int errorTotalCount = 0;

                // Total task consumption time
                var totalTaskMark = TaskTime.StartNew();

                // 2、--------traversal Search area 
                List<Entity.AreaInfo> areaInfos = AreaUtil.GetEnableAreaInfoList();
                int areaInfosCount = areaInfos.Count;
                for (int i = 0; i < areaInfosCount; i++)
                {
                    Entity.AreaInfo areaInfo = areaInfos[i];

                    var singleTaskMark = TaskTime.StartNew();

                    // Different areas have separate records of indexes.
                    string areaIdIndex = areaInfo.AreaId + "Index";

                    // Rebuilding will remove all markers.
                    if (isRebuild)
                    {
                        
                        AppUtil.DeleteSection(areaIdIndex);
                    }

                    // 2.1、-------- Start obtaining the file list
                    string msg = string.Format("Search area【{0}】，starting to scan files...", areaInfo.AreaName);
                    log.Info(msg);
                    ShowStatus(msg);

                    // Define all files list
                    List<string> allFilePaths = new List<string>();
                    // Define updated files list
                    List<string> updateFilePaths = new List<string>();
                    // Define deleted files list
                    List<string> deleteFilePaths = new List<string>();

                    // 2.2、-------- Obtain the supported file type extensions
                    // (find the corresponding file list based on the supported file types configured for different regions)
                    Regex fileExtRegex = RegexUtil.BuildRegex(@"^.+\.(" + FileTypeUtil.ConvertToFileTypeExts(areaInfo.AreaFileTypes, "|") + ")$"); //new Regex(@"^.+\.(" + FileTypeUtil.ConvertToFileTypeExts(areaInfo.AreaFileTypes, "|") + ")$");

                    var scanTaskMark = TaskTime.StartNew();
                    // Scan the list of files that need to be indexed.
                    foreach (string s in areaInfo.AreaFolders)
                    {
                        log.Info("Catalog：" + s);
                        // Get file information list
                        FileUtil.GetAllFiles(allFilePaths, s, fileExtRegex);
                    }

                    msg = string.Format("search area【{0}】，file scanning completed；file num：{1}，duration：{2}；Starting to analyze the list of files that need to be updated...", areaInfo.AreaName, allFilePaths.Count, scanTaskMark.ConsumeTime);
                    log.Info(msg);
                    ShowStatus(msg);

                    var analysisTaskMark = TaskTime.StartNew();
                    // 2.3、-------- Obtain the list of files to be deleted
                    if (AppUtil.ReadSectionList(areaIdIndex) != null)
                    {
                        foreach (string filePath in AppUtil.ReadSectionList(areaIdIndex))
                        {
                            // If it does not exist, it means the file has been deleted.
                            if (!allFilePaths.Contains(filePath))
                            {
                                deleteFilePaths.Add(filePath);
                                AppUtil.WriteValue(areaIdIndex, filePath, null);
                            }
                        }
                    }

                    // 2.4、-------- If it is an update operation, determine whether the file format has changed ->
                    // Check for changes in file update time to find the final list of files that need to be updated.
                    // Update requires verification, while reconstruction skips directly.
                    if (!isRebuild)
                    {
                        // Update: List of files that need to be updated
                        foreach (string filePath in allFilePaths)
                        {
                            try
                            {
                                FileInfo fileInfo = new FileInfo(filePath);
                                // Current file modification time
                                string lastWriteTime = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss.ffff");
                                // Last modified time tag of the file during the indexation.
                                string lastWriteTimeTag = AppUtil.ReadValue(areaIdIndex, filePath);

                                // The file modification time is different, indicating that the file has been modified.
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
                        // Rebuilt: all file list
                        updateFilePaths.AddRange(allFilePaths);
                    }

                    msg = string.Format("search area 【{0}】，file analysis completed；{1} num：{2}，deleted：{3}，duration：{4}；starting to {5} index...", areaInfo.AreaName, tag, updateFilePaths.Count, deleteFilePaths.Count, analysisTaskMark.ConsumeTime, tag);
                    log.Info(msg);
                    ShowStatus(msg);

                    // 2.5、-------- Verify whether the scanned file list is empty
                    if (updateFilePaths.Count <= 0 && deleteFilePaths.Count <= 0)
                    {
                        build = false;
                        msg = string.Format("search area【{0}】， no updated files and deleted files，do not {1} index...", areaInfo.AreaName, tag);
                        log.Info(msg);
                        ShowStatus(msg);
                        continue;
                    }

                    // When executed in the background, change to the minimum thread unit, and restore to the system configured number of threads otherwise.
                    AppCore.SetThreadPoolSize(!isBackground);

                    // 2.6、-------- Create index method
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

                    // 2.7、-------- Current area completion log
                    msg = string.Format("search area【{0}】，index {1} completed；{2} num：{3}，deleted：{4}，error：{5}，duration：{6}.", areaInfo.AreaName, tag, tag, updateFilePaths.Count, deleteFilePaths.Count, errorCount, singleTaskMark.ConsumeTime);
                    log.Info(msg);
                    ShowStatus(msg);

                    MessageCore.ShowSuccess(msg);

                    // 2.8、-------- Total number of records, total number of updates, total number of deletions, total number of errors
                    fileTotalCount = fileTotalCount + allFilePaths.Count;
                    updateTotalCount = updateTotalCount + updateFilePaths.Count;
                    deleteTotalCount = deleteTotalCount + deleteFilePaths.Count;
                    errorTotalCount = errorTotalCount + errorCount;
                }

                // 3、-------- complete log
                string message = string.Format("index {0} completed. regions：{1}，{2} num：{3}，deleted：{4}，error：{5}，duration：{6}.", tag, areaInfos.Count, tag, updateTotalCount, deleteTotalCount, errorTotalCount, totalTaskMark.ConsumeTime);
                log.Info(message);
                ShowStatus(message);

                // 4、-------- Number of indexed files and last update time
                AppUtil.WriteValue("AppConfig", "FileTotalCount", fileTotalCount + "");
                AppUtil.WriteValue("AppConfig", "LastIndexTime", DateTime.Now.ToString());

                // 5、-------- finish construction
                build = false;
            }
            catch (Exception ex)
            {
                log.Error("Index construction error：" + ex.Message, ex);

                build = false;
            }
        }

        /// <summary>
        /// show status
        /// </summary>
        /// <param name="text">message</param>
        /// <param name="percent">progress，0-100</param>
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

        #region right preview area
        /// <summary>
        /// open file
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
        /// open folder
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

        #region other private encapsulation
        /// <summary>
        /// get search keywords
        /// </summary>
        /// <returns></returns>
        private List<string> GetSearchTextKeywords()
        {
            string searchText = SearchText.Text.Trim();
            // Declaration Keyword List
            List<string> keywords = new List<string>();
            // Return null if empty
            if (string.IsNullOrEmpty(searchText)) return keywords;

            // Exact search not selected || Non-regular expression
            if (PreciseRetrieval.IsChecked == false || !searchText.StartsWith(AppConst.REGEX_SEARCH_PREFIX))
            {
                //Replace built-in special characters (AND|OR|NOT|&&|||"|~|:)
                searchText = AppConst.REGEX_BUILT_IN_SYMBOL.Replace(searchText, " ");
            }

            // Precise Search || Regular Expression
            if (PreciseRetrieval.IsChecked == true || searchText.StartsWith(AppConst.REGEX_SEARCH_PREFIX))
            {
                keywords.Add(searchText);
            }
            // Space segmentation
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
            // Automatic tokenization by the tokenizer
            else
            {
                // segmentList
                List<string> segmentList = IndexCore.GetKeywords(searchText);//AppConst.INDEX_SEGMENTER.CutForSearch(searchText).ToList();
                // combine keywords
                keywords = keywords.Union(segmentList).ToList();
            }
            return keywords;
        }
        #endregion

        #region Loading

        /// <summary>
        /// Show search Loading
        /// </summary>
        private void ShowSearchLoading()
        {
            Dispatcher.Invoke(new Action(() =>
            {
                this._searchLoading.Visibility = Visibility.Visible;
            }));
        }
        /// <summary>
        /// Hide search Loading
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
            
    this.Hide();

            // If AIPage has already been opened, activate it; otherwise, create a new one.
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

            // 1. Combine into a query
            string combinedQuery = string.Join(" ", keywords);

            // 2. Set to the UI
            SearchText.Text = combinedQuery;

            // 3. Restore to the same default state as when clicking search.
            PreciseRetrieval.IsChecked = false;
            MatchWords.IsChecked = false;

            // all file types
            ToggleButtonAutomationPeer toggleButtonAutomationPeer = new ToggleButtonAutomationPeer(_radioButtonAll);
            IToggleProvider toggleProvider = toggleButtonAutomationPeer.GetPattern(PatternInterface.Toggle) as IToggleProvider;
            toggleProvider.Toggle();

            // default sort
            SortOptions.SelectedIndex = 0;

            // 4. execute search
            BeforeSearch();
        }

    }
}
