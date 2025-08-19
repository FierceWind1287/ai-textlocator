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
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application, ISingleInstanceApp
    {
        /* ========== P/Invoke ========== */
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr AddDllDirectory(string path);

        // Fallback for Windows 7 and earlier (AddDllDirectory is not available)
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
        private const uint LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400;
        /* ============================= */

        private static readonly ILog log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod()!.DeclaringType);

        /// <summary>
        /// Entry point
        /// </summary>
        [STAThread]
        public static void Main()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string uniqueName = string.Format(CultureInfo.InvariantCulture, "Local\\{{{0}}}{{{1}}}", assembly.GetType().GUID, assembly.GetName().Name);
            if (SingleInstance<App>.InitializeAsFirstInstance(uniqueName))
            {
                var app = new App();
                app.InitializeComponent();
                app.Run();

                SingleInstance<App>.Cleanup();
            }
        }

        /// <summary>
        /// Handle external command line arguments
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

        // Tray icon
        private static TaskbarIcon _taskbar;
        public static TaskbarIcon Taskbar { get => _taskbar; set => _taskbar = value; }

        public App()
        {
            // Initialize thread pool size
            AppCore.SetThreadPoolSize();

            // Initialize configuration
            InitAppConfig();

            // Initialize file service engine
            InitFileInfoServiceEngine();

            // Initialize window state size
            CacheUtil.Put("WindowState", WindowState.Normal);
        }

        /// <summary>
        /// Override OnStartup
        /// </summary>
        /// <param name="e"></param>
        protected override void OnStartup(StartupEventArgs e)
        {
            /* ①  Specify external DLL directory (./extern) */
            string externDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extern");
            SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LOAD_LIBRARY_SEARCH_USER_DIRS);
            if (AddDllDirectory(externDir) == IntPtr.Zero && Marshal.GetLastWin32Error() == 87)
                SetDllDirectory(externDir);               // Windows 7 fallback

            /* ②  Initialize Whisper */
            string whisperModel = @"E:\Intel\distil-whisper-large";         // ← Change to your model path
            int rc = WhisperNative.Init(whisperModel, "CPU");
            if (rc != 0) MessageBox.Show($"Whisper initialization failed: error code {rc}");


            /* ⑤  Continue the normal startup process */
            _taskbar = (TaskbarIcon)FindResource("Taskbar");
            base.OnStartup(e);
        }

        #region Initialization
        /// <summary>
        /// Initialize AppConfig
        /// </summary>
        private void InitAppConfig()
        {
            // Save cache pool capacity
            AppUtil.WriteValue("AppConfig", "CachePoolCapacity", AppConst.CACHE_POOL_CAPACITY + "");

            // Number of items displayed per page
            AppUtil.WriteValue("AppConfig", "ResultListPageSize", AppConst.MRESULT_LIST_PAGE_SIZE + "");

            // File read timeout
            AppUtil.WriteValue("AppConfig", "FileContentReadTimeout", AppConst.FILE_CONTENT_READ_TIMEOUT + "");

            // File content summary cut length
            AppUtil.WriteValue("AppConfig", "FileContentBreviaryCutLength", AppConst.FILE_CONTENT_BREVIARY_CUT_LENGTH + "");
        }
        #endregion

        #region File Service Engine Registration
        /// <summary>
        /// Initialize file info service engine
        /// </summary>
        private void InitFileInfoServiceEngine()
        {
            try
            {
                log.Debug("Initialize the file engine factory");
                // Word service
                FileInfoServiceFactory.Register(FileType.Word, new WordFileService());
                // Excel service
                FileInfoServiceFactory.Register(FileType.Excel, new ExcelFileService());
                // PowerPoint service
                FileInfoServiceFactory.Register(FileType.PowerPoint, new PowerPointFileService());
                // PDF service
                FileInfoServiceFactory.Register(FileType.PDF, new PdfFileService());
                // HTML or XML service
                FileInfoServiceFactory.Register(FileType.DOM, new DomFileService());
                // Plain text service
                FileInfoServiceFactory.Register(FileType.Text, new TxtFileService());
                // Common image service
                FileInfoServiceFactory.Register(FileType.Image, new NoTextFileService());
                // Common archive service
                FileInfoServiceFactory.Register(FileType.Archive, new ZipFileService());
                // Source code service
                FileInfoServiceFactory.Register(FileType.SourceCode, new CodeFileService());
            }
            catch (Exception ex)
            {
                log.Error("File service factory initialization error: " + ex.Message, ex);
            }
        }
        #endregion

        #region Exception Handling
        /// <summary>
        /// Handle uncaught exception in non-UI thread
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
        /// Handle uncaught exception inside Task thread
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            log.Error("Unhandled exception in Task thread: " + e.Exception.Message, e.Exception);
            e.SetObserved();
        }

        /// <summary>
        /// Handle uncaught exception in UI thread
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                log.Error("Uncaught exception on the UI thread: " + e.Exception.Message, e.Exception);
                // After handling, set Handler=true to indicate that the exception has been processed
                e.Handled = true;
            }
            catch (Exception ex)
            {
                log.Fatal("A serious error has occurred in the program: " + ex.Message, ex);
            }
        }
        #endregion
    }
}
