using log4net;
using System;
using System.Text.RegularExpressions;
using System.Windows;
using TextLocator.Core;
using TextLocator.Message;
using TextLocator.Util;

namespace TextLocator
{
    /// <summary>
    /// SettingWindow.xaml 的交互逻辑
    /// </summary>
    public partial class SettingWindow : Window
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        /// <summary>
        /// 单例
        /// </summary>
        private static SettingWindow _instance;

        public SettingWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 创建系统参数设置窗体实例
        /// </summary>
        /// <returns></returns>
        public static SettingWindow CreateInstance()
        {
            return _instance ?? (_instance = new SettingWindow());
        }

        /// <summary>
        /// 加载完毕
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 加载配置
            LoadConfig();
        }

        /// <summary>
        /// 加载配置信息
        /// </summary>
        private void LoadConfig()
        {
            // -------- 索引和文件
            // 启用索引更新任务
            this.EnableIndexUpdateTask.IsChecked = AppConst.ENABLE_INDEX_UPDATE_TASK;
            // 索引更新时间间隔
            this.IndexUpdateTaskInterval.Text = AppConst.INDEX_UPDATE_TASK_INTERVAL + "";

            // 文件读取超时时间
            this.FileContentReadTimeout.Text = AppConst.FILE_CONTENT_READ_TIMEOUT + "";

            // -------- 列表和缓存
            // 每页显示条数
            this.ResultListPageSize.Text = AppConst.MRESULT_LIST_PAGE_SIZE + "";

            // 缓存池容量
            this.CachePoolCapacity.Text = AppConst.CACHE_POOL_CAPACITY + "";

            // -------- 内容预览
            // 启用预览内容摘要
            this.EnablePreviewSummary.IsChecked = AppConst.ENABLE_PREVIEW_SUMMARY;
            // 文件内容摘要切割长度
            this.FileContentBreviaryCutLength.Text = AppConst.FILE_CONTENT_BREVIARY_CUT_LENGTH + "";
        }

        #region 保存并关闭
        /// <summary>
        /// 保存并关闭
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private  void SaveClose_Click(object sender, RoutedEventArgs e)
        {
            // -------- 索引和文件
            // 启用索引更新任务
            bool enableIndexUpdateTask = (bool)this.EnableIndexUpdateTask.IsChecked;
            if (enableIndexUpdateTask)
            {
                // 索引更新时间间隔
                string indexUpdateTaskIntervalText = this.IndexUpdateTaskInterval.Text;
                int indexUpdateTaskInterval = 0;
                try
                {
                    indexUpdateTaskInterval = int.Parse(indexUpdateTaskIntervalText);
                }
                catch
                {
                    MessageCore.ShowWarning("Index update task interval time is incorrect.");
                    return;
                }
                if (indexUpdateTaskInterval < 5 || indexUpdateTaskInterval > 60)
                {
                    MessageCore.ShowWarning("Index update task interval: It is recommended to set it within the range of 5 to 60 minutes.");
                    return;
                }

                AppConst.INDEX_UPDATE_TASK_INTERVAL = indexUpdateTaskInterval;
            }
            // 文件读取超时时间
            string fileContentReadTimeoutText = this.FileContentReadTimeout.Text;
            int fileContentReadTimeout = 0;
            try
            {
                fileContentReadTimeout = int.Parse(fileContentReadTimeoutText);
            }
            catch
            {
                MessageCore.ShowWarning("Invalid file read time");
                return;
            }
            if (fileContentReadTimeout < 5 || fileContentReadTimeout > 15)
            {
                MessageCore.ShowWarning("File content read timeout: It is recommended to set it within the range of 5 - 15 minutes.");
                return;
            }

            // -------- 列表和缓存
            // 每页显示条数
            string resultListPageSizeText = this.ResultListPageSize.Text;
            int resultListPageSize = 0;
            try
            {
                resultListPageSize = int.Parse(resultListPageSizeText);
            }
            catch
            {
                MessageCore.ShowWarning("Invalid value for page size.");
                return;
            }
            if (resultListPageSize < 50 || resultListPageSize > 300)
            {
                MessageCore.ShowWarning("Items per page: It is recommended to set it within the range of 50 - 300.");
                return;
            }
            // 缓存池容量
            string cachePoolCapacityText = this.CachePoolCapacity.Text;
            int cachePoolCapacity = 0;
            try
            {
                cachePoolCapacity = int.Parse(cachePoolCapacityText);
            }
            catch
            {
                MessageCore.ShowWarning("Invalid cache pool capacity.");
                return;
            }
            if (cachePoolCapacity < 50000 || cachePoolCapacity > 500000)
            {
                MessageCore.ShowWarning("Cache pool capacity: Recommended range is 50,000 - 500,000.");
                return;
            }

            // -------- 内容预览
            // 启用预览内容摘要
            bool enablePreviewSummary = (bool)this.EnablePreviewSummary.IsChecked;
            if (enablePreviewSummary)
            {
                // 文件内容摘要切割长度
                string fileContentBreviaryCutLengthText = this.FileContentBreviaryCutLength.Text;
                int fileContentBreviaryCutLength = 0;
                try
                {
                    fileContentBreviaryCutLength = int.Parse(fileContentBreviaryCutLengthText);
                }
                catch
                {
                    MessageCore.ShowWarning("Invalid content summary cut length.");
                    return;
                }
                if (fileContentBreviaryCutLength < 30 || fileContentBreviaryCutLength > 120)
                {
                    MessageCore.ShowWarning("Content summary cut length: Recommended range is 30 - 120.");
                    return;
                }

                AppConst.FILE_CONTENT_BREVIARY_CUT_LENGTH = fileContentBreviaryCutLength;
            }


            // -------- 刷新、保存
            // ---- 索引和文件
            AppConst.ENABLE_INDEX_UPDATE_TASK = enableIndexUpdateTask;
            AppUtil.WriteValue("AppConfig", "EnableIndexUpdateTask", AppConst.ENABLE_INDEX_UPDATE_TASK + "");
            log.Debug("Updated 'EnableIndexUpdateTask：" + AppConst.ENABLE_INDEX_UPDATE_TASK);
            if (AppConst.ENABLE_INDEX_UPDATE_TASK)
            {
                AppUtil.WriteValue("AppConfig", "IndexUpdateTaskInterval", AppConst.INDEX_UPDATE_TASK_INTERVAL + "");
                log.Debug("Updated index update task interval:" + AppConst.INDEX_UPDATE_TASK_INTERVAL);
            }
            AppConst.FILE_CONTENT_READ_TIMEOUT = fileContentReadTimeout;
            AppUtil.WriteValue("AppConfig", "FileContentReadTimeout", AppConst.FILE_CONTENT_READ_TIMEOUT + "");
            log.Debug("Updated file content read timeout: " + AppConst.FILE_CONTENT_READ_TIMEOUT);

            // ---- 列表和缓存
            AppConst.MRESULT_LIST_PAGE_SIZE = resultListPageSize;
            AppUtil.WriteValue("AppConfig", "ResultListPageSize", AppConst.MRESULT_LIST_PAGE_SIZE + "");
            log.Debug("Updated list page size: " + AppConst.MRESULT_LIST_PAGE_SIZE);
            AppConst.CACHE_POOL_CAPACITY = cachePoolCapacity;
            AppUtil.WriteValue("AppConfig", "CachePoolCapacity", AppConst.CACHE_POOL_CAPACITY + "");
            log.Debug("Updated cache pool capacity: " + AppConst.CACHE_POOL_CAPACITY);

            // ---- 内容预览
            AppConst.ENABLE_PREVIEW_SUMMARY = enablePreviewSummary;
            AppUtil.WriteValue("AppConfig", "EnablePreviewSummary", AppConst.ENABLE_PREVIEW_SUMMARY + "");
            log.Debug("Updated preview summary enabled:" + AppConst.ENABLE_PREVIEW_SUMMARY);
            if (AppConst.ENABLE_PREVIEW_SUMMARY)
            {
                AppUtil.WriteValue("AppConfig", "FileContentBreviaryCutLength", AppConst.FILE_CONTENT_BREVIARY_CUT_LENGTH + "");
                log.Debug("Updated content summary cut length:" + AppConst.FILE_CONTENT_BREVIARY_CUT_LENGTH);
            }

            MessageCore.ShowSuccess("Settings saved successfully. Some changes will take effect after restarting the application.");

            this.Close();
        }
        #endregion

        /// <summary>
        /// 窗体关闭
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Closed(object sender, EventArgs e)
        {
            _instance.Topmost = false;
            _instance = null;
        }

        /// <summary>
        /// 数字文本框预览输入
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Number_TextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            e.Handled = new Regex("[^0-9.-]+").IsMatch(e.Text);
        }
    }
}
