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
        /* ===== P/Invoke for native DLL search paths ===== */
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr AddDllDirectory(string path);

        // Fallback for Windows 7 and earlier
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
        private const uint LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400;
        /* ================================================= */

        private static readonly ILog log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod()!.DeclaringType);

        // Fixed unique application ID for single instance control
        private const string AppId = "b9f6b1c9-1a0e-4b2a-8a9d-9b8f4c1e7f1a";

        // Tray icon
        private static TaskbarIcon _taskbar;
        public static TaskbarIcon Taskbar { get => _taskbar; set => _taskbar = value; }

        /// <summary>
        /// Application entry point
        /// </summary>
        [STAThread]
        public static void Main()
        {
            string uniqueName = $@"Local\{{{AppId}}}{{{Assembly.GetExecutingAssembly().GetName().Name}}}";
            if (SingleInstance<App>.InitializeAsFirstInstance(uniqueName))
            {
                var app = new App();
                app.InitializeComponent();
                app.Run();
                SingleInstance<App>.Cleanup();
            }
        }

        public App()
        {
            // Initialize thread pool size
            AppCore.SetThreadPoolSize();

            // Initialize configuration values
            InitAppConfig();

            // Initialize file service engine
            InitFileInfoServiceEngine();

            // Initialize window state cache
            CacheUtil.Put("WindowState", WindowState.Normal);
        }

        /// <summary>
        /// Handles command line arguments from subsequent instances
        /// </summary>
        public bool SignalExternalCommandLineArgs(IList<string> args)
        {
            if (this.MainWindow != null)
            {
                if (this.MainWindow.WindowState == WindowState.Minimized)
                    this.MainWindow.WindowState = WindowState.Normal;
                this.MainWindow.Activate();
            }
            return true;
        }

        /// <summary>
        /// Application startup logic
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string externDir = Path.Combine(baseDir, "extern");

            // 1) Set native DLL search directory
            try
            {
                try
                {
                    SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LOAD_LIBRARY_SEARCH_USER_DIRS);
                    if (AddDllDirectory(externDir) == IntPtr.Zero && Marshal.GetLastWin32Error() == 87)
                        SetDllDirectory(externDir); // ERROR_INVALID_PARAMETER → fallback to Win7 method
                }
                catch (EntryPointNotFoundException)
                {
                    SetDllDirectory(externDir); // API missing on legacy systems
                }
            }
            catch (Exception dllPathEx)
            {
                log.Warn("Failed to set native DLL search path: " + dllPathEx.Message, dllPathEx);
            }

            // 2) Resolve Whisper model directory
            // Priority: Environment variable → Config → Relative path ./extern/distil-whisper-large
            string fromEnv = Environment.GetEnvironmentVariable("WHISPER_MODEL_DIR");
            string fromConfig = AppUtil.ReadValue("Whisper", "ModelDir", "");
            if (!string.IsNullOrWhiteSpace(fromConfig) && !Path.IsPathRooted(fromConfig))
                fromConfig = Path.GetFullPath(Path.Combine(baseDir, fromConfig));

            string defaultInExtern = Path.Combine(externDir, "distil-whisper-large");

            string whisperModel = new[] { fromEnv, fromConfig, defaultInExtern }
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p));

            if (string.IsNullOrEmpty(whisperModel))
            {
                MessageBox.Show("Whisper model directory not found.\n" +
                                "Please set WHISPER_MODEL_DIR or place the model under .\\extern\\distil-whisper-large");
            }
            else
            {
                string device = AppUtil.ReadValue("Whisper", "Device", "CPU"); // CPU/GPU/NPU/AUTO
                int rc = WhisperNative.Init(whisperModel, device);
                if (rc != 0)
                {
                    MessageBox.Show($"Whisper initialization failed (code {rc}).");
                    log.Error($"Whisper init failed. dir={whisperModel}, device={device}, rc={rc}");
                }
                else
                {
                    log.Info($"Whisper initialized. dir={whisperModel}");
                }
            }

            // 3) Register global exception handlers
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            // 4) Initialize tray icon
            _taskbar = (TaskbarIcon)FindResource("Taskbar");

            base.OnStartup(e);
        }

        #region Initialization
        /// <summary>
        /// Initialize application configuration values
        /// </summary>
        private void InitAppConfig()
        {
            AppUtil.WriteValue("AppConfig", "CachePoolCapacity", AppConst.CACHE_POOL_CAPACITY + "");
            AppUtil.WriteValue("AppConfig", "ResultListPageSize", AppConst.MRESULT_LIST_PAGE_SIZE + "");
            AppUtil.WriteValue("AppConfig", "FileContentReadTimeout", AppConst.FILE_CONTENT_READ_TIMEOUT + "");
            AppUtil.WriteValue("AppConfig", "FileContentBreviaryCutLength", AppConst.FILE_CONTENT_BREVIARY_CUT_LENGTH + "");
        }

        /// <summary>
        /// Initialize file info service engine
        /// </summary>
        private void InitFileInfoServiceEngine()
        {
            try
            {
                log.Debug("Initialize the file engine factory");
                FileInfoServiceFactory.Register(FileType.Word, new WordFileService());
                FileInfoServiceFactory.Register(FileType.Excel, new ExcelFileService());
                FileInfoServiceFactory.Register(FileType.PowerPoint, new PowerPointFileService());
                FileInfoServiceFactory.Register(FileType.PDF, new PdfFileService());
                FileInfoServiceFactory.Register(FileType.DOM, new DomFileService());
                FileInfoServiceFactory.Register(FileType.Text, new TxtFileService());
                FileInfoServiceFactory.Register(FileType.Image, new NoTextFileService());
                FileInfoServiceFactory.Register(FileType.Archive, new ZipFileService());
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
        /// Handles unhandled exceptions in non-UI threads
        /// </summary>
        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var builder = new StringBuilder();
            if (e.IsTerminating) builder.Append("A fatal error occurred in a non-UI thread. ");
            builder.Append("Non-UI thread exception: ");
            if (e.ExceptionObject is Exception ex) builder.Append(ex.Message);
            else builder.Append(e.ExceptionObject);
            log.Error(builder.ToString());
        }

        /// <summary>
        /// Handles unobserved task exceptions
        /// </summary>
        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            log.Error("Unhandled exception in Task thread: " + e.Exception.Message, e.Exception);
            e.SetObserved();
        }

        /// <summary>
        /// Handles unhandled exceptions in the UI thread
        /// </summary>
        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                log.Error("Uncaught exception on the UI thread: " + e.Exception.Message, e.Exception);
                e.Handled = true; // prevent crash after handling
            }
            catch (Exception ex)
            {
                log.Fatal("A serious error has occurred in the program: " + ex.Message, ex);
            }
        }
        #endregion
    }
}
