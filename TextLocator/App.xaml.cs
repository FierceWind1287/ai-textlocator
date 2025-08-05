using DocumentFormat.OpenXml.Wordprocessing;
using Hardcodet.Wpf.TaskbarNotification;
using log4net;
using Lucene.Net.Analysis;
using Rubyer;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TextLocator.Core;
using TextLocator.Enums;
using TextLocator.Factory;
using TextLocator.Service;
using TextLocator.SingleInstance;
using TextLocator.Util;


namespace TextLocator
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application, ISingleInstanceApp
    {
        /* ========== P/Invoke ========== */
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr AddDllDirectory(string path);

        // 兼容 Windows 8 以前（AddDllDirectory 不可用）时的 fallback
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
        private const uint LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400;
        /* ============================= */

        private static readonly ILog log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod()!.DeclaringType);

        /// <summary>
        /// 入口函数
        /// </summary>
        [STAThread]
        public static void Main()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string uniqueName = string.Format(CultureInfo.InvariantCulture, "Local\\{{{0}}}{{{1}}}", assembly.GetType().GUID, assembly.GetName().Name);
            if (SingleInstance<App>.InitializeAsFirstInstance(uniqueName)) {
                var app = new App();
                app.InitializeComponent();
                app.Run();

                SingleInstance<App>.Cleanup();
            }
        }

        /// <summary>
        /// 信号外部命令行参数
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public bool SignalExternalCommandLineArgs(IList<string> args)
        {
            if (this.MainWindow.WindowState == WindowState.Minimized)
            {
                this.MainWindow.WindowState = WindowState.Normal;
            }

            this.MainWindow.Activate();

            return true;
        }

        // 托盘图标
        private static TaskbarIcon _taskbar;
        public static TaskbarIcon Taskbar { get => _taskbar; set => _taskbar = value; }

        public App()
        {
            // 初始化线程池大小
            AppCore.SetThreadPoolSize();

            // 初始化配置
            InitAppConfig();

            // 初始化文件服务引擎
            InitFileInfoServiceEngine();

            // 初始化窗口状态尺寸
            CacheUtil.Put("WindowState", WindowState.Normal);
        }

        /// <summary>
        /// 重写OnStartup
        /// </summary>
        /// <param name="e"></param>
        protected override void OnStartup(StartupEventArgs e)
        {
            /* ①  指定外部 DLL 目录（./extern） */
            string externDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extern");
            SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LOAD_LIBRARY_SEARCH_USER_DIRS);
            if (AddDllDirectory(externDir) == IntPtr.Zero && Marshal.GetLastWin32Error() == 87)
                SetDllDirectory(externDir);               // Win7 fallback

            /* ②  初始化 Whisper */
            string whisperModelDir = Path.Combine(externDir, "distil-whisper-large"); // ← 你的模型文件夹名
            if (!Directory.Exists(whisperModelDir))
            {
                MessageBox.Show($"模型目录不存在：{whisperModelDir}");
                Shutdown();
                return;
            }

            int rc = WhisperNative.Init(whisperModelDir, "AUTO");   // 用 AUTO 让 OpenVINO 自动挑设备
            if (rc != 0)
            {
                MessageBox.Show($"Whisper 初始化失败：错误码 {rc}");
                Shutdown();
                return;
            }


            /* ⑤  继续原来启动流程 */
            _taskbar = (TaskbarIcon)FindResource("Taskbar");
            base.OnStartup(e);
        }

        #region 初始化
        /// <summary>
        /// 初始化AppConfig
        /// </summary>
        private void InitAppConfig()
        {
            // 保存缓存池容量
            AppUtil.WriteValue("AppConfig", "CachePoolCapacity", AppConst.CACHE_POOL_CAPACITY + "");

            // 每页显示条数
            AppUtil.WriteValue("AppConfig", "ResultListPageSize", AppConst.MRESULT_LIST_PAGE_SIZE + "");

            // 文件读取超时时间
            AppUtil.WriteValue("AppConfig", "FileContentReadTimeout", AppConst.FILE_CONTENT_READ_TIMEOUT + "");

            // 文件内容摘要切割长度
            AppUtil.WriteValue("AppConfig", "FileContentBreviaryCutLength", AppConst.FILE_CONTENT_BREVIARY_CUT_LENGTH + "");
        }
        #endregion

        #region 文件服务引擎注册
        /// <summary>
        /// 初始化文件信息服务引擎
        /// </summary>
        private void InitFileInfoServiceEngine()
        {
            try
            {
                log.Debug("Initialize the file engine factory");
                // Word服务
                FileInfoServiceFactory.Register(FileType.Word, new WordFileService());
                // Excel服务
                FileInfoServiceFactory.Register(FileType.Excel, new ExcelFileService());
                // PowerPoint服务
                FileInfoServiceFactory.Register(FileType.PowerPoint, new PowerPointFileService());
                // PDF服务
                FileInfoServiceFactory.Register(FileType.PDF, new PdfFileService());
                // HTML或XML服务
                FileInfoServiceFactory.Register(FileType.DOM, new DomFileService());
                // 纯文本服务
                FileInfoServiceFactory.Register(FileType.Text, new TxtFileService());
				// 常用图片服务
                FileInfoServiceFactory.Register(FileType.Image, new NoTextFileService());
                // 常用压缩包
                FileInfoServiceFactory.Register(FileType.Archive, new ZipFileService());
				// 程序员服务
                FileInfoServiceFactory.Register(FileType.SourceCode, new CodeFileService());
            }
            catch (Exception ex)
            {
                log.Error("File service factory initialization error：" + ex.Message, ex);
            }
        }
        #endregion

        #region 异常处理
        /// <summary>
        /// 非UI线程未捕获异常处理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            StringBuilder builder = new StringBuilder();
            if (e.IsTerminating)
            {
                builder.Append("A fatal error occurred in a non-UI thread.");
            }
            builder.Append("Non-UI thread exception:");
            if (e.ExceptionObject is Exception)
            {
                builder.Append((e.ExceptionObject as Exception).Message);
            }
            else
            {
                builder.Append(e.ExceptionObject);
            }
            log.Error(builder.ToString());
        }

        /// <summary>
        /// Task线程内未捕获异常处理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            log.Error("Unhandled exception in Task thread: " + e.Exception.Message, e.Exception);
            e.SetObserved();
        }

        /// <summary>
        /// UI线程未捕获异常处理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                log.Error("Uncaught exception on the UI thread：" + e.Exception.Message, e.Exception);
                // 处理完后，我们需要将Handler=true表示已此异常已处理过
                e.Handled = true;
            }
            catch (Exception ex)
            {
                log.Fatal("A serious error has occurred in the program.：" + ex.Message, ex);
            }
        }
        #endregion
    }
}
