using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;              // ← Added
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using TextLocator.Service;          // ← Added: our wrapped sidecar client
using TextLocator.Util;             // already exists (CSV parsing, etc.)

namespace TextLocator
{
    public partial class AIPage : Window
    {
        // ────────────── Added: wrapped keyword client ──────────────
        private readonly KeywordServiceClient _kwClient;

        // ────────────── Constructor ──────────────
        public AIPage()
        {
            InitializeComponent();
            LoadAreaInfo();                       // Fill in the 'Search Area' information.

            // Use our wrapped client (locating KeywordService.exe from executable directory)
            _kwClient = KeywordServiceClient.FromAppBase(new ProcessRunner(), timeoutMs: 60000);

            // (Optional) Force CPU first, reduce environment dependencies; later you can change layers back to 4–6 for GPU
            Environment.SetEnvironmentVariable("KEYWORD_GPU_LAYERS", "0");
            Environment.SetEnvironmentVariable("KEYWORD_CTX", "512");
            Environment.SetEnvironmentVariable("KEYWORD_MAXTOK", "24");

            // ★ Optional: background warm-up to reduce first response latency
            _ = Task.Run(() => WarmupKeywordService());
        }

        // ────────────── MainWindow Switch ──────────────
        private void EnterFileSearch_Click(object sender, RoutedEventArgs e)
        {
            var main = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            (main ?? new MainWindow()).Show();     // If it doesn't exist, create a new one.
            main?.Activate();
            Close();
        }

        // ────────────── Search Area Settings Dialog ──────────────
        private void AreaInfos_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var dlg = new AreaWindow { Owner = this, Topmost = true };
            dlg.ShowDialog();

            Application.Current.Windows.OfType<MainWindow>()
                .FirstOrDefault()?.InitializeAppConfig();

            LoadAreaInfo();
        }

        private void LoadAreaInfo()
        {
            var list = AreaUtil.GetEnableAreaInfoList();
            if (list.Count == 0)
            {
                EnableAreaInfos.Text = "Search area not set";
                EnableAreaInfos.ToolTip = "Double-click to set search area.";
                return;
            }

            EnableAreaInfos.Text = string.Join(", ", list.Select(a => a.AreaName));
            EnableAreaInfos.ToolTip = string.Join(Environment.NewLine,
                list.Select(a => $"{a.AreaName}: {string.Join(", ", a.AreaFolders)}"));
        }

        // ────────────── Input box & Clear button ──────────────
        private void CommandInput_TextChanged(object s, TextChangedEventArgs e) =>
            CleanButton.Visibility = string.IsNullOrWhiteSpace(CommandInput.Text)
                                     ? Visibility.Hidden : Visibility.Visible;

        private void CleanButton_Click(object s, RoutedEventArgs e)
        {
            CommandInput.Clear();
            CommandInput.Focus();
        }

        private void CommandInput_PreviewKeyUp(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) SearchButton_Click(this, new RoutedEventArgs());
        }

        // ────────────── Audio related ──────────────
        private WaveInEvent _mic;
        private readonly List<float> _audioBuffer = new();
        private bool _isRecording;

        private void MicButton_Click(object s, RoutedEventArgs e)
        {
            if (!_isRecording)
            {
                _isRecording = true;
                MicButton.ToolTip = "Stop";
                _audioBuffer.Clear();

                _mic = new WaveInEvent { WaveFormat = new WaveFormat(16000, 1) };
                _mic.DataAvailable += Mic_DataAvailable;
                _mic.RecordingStopped += Mic_RecordingStopped;
                _mic.StartRecording();
            }
            else
            {
                _isRecording = false;
                MicButton.IsEnabled = false;
                _mic.StopRecording();
            }
        }
        private void Mic_DataAvailable(object s, WaveInEventArgs e)
        {
            for (int i = 0; i < e.BytesRecorded / 2; i++)
                _audioBuffer.Add(BitConverter.ToInt16(e.Buffer, i * 2) / 32768f);
        }
        private async void Mic_RecordingStopped(object s, StoppedEventArgs e)
        {
            _mic.Dispose(); _mic = null;
            string text = await Task.Run(() => WhisperNative.Transcribe(_audioBuffer.ToArray()));
            CommandInput.Text = text;
            MicButton.ToolTip = "Voice Input";
            MicButton.IsEnabled = true;
        }

        private void AddonLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private async void BtnMic_Click(object s, RoutedEventArgs e)
        {
            var dlg = new RecordWindow { Owner = this };
            if (dlg.ShowDialog() != true || dlg.RecordedPcm == null) return;

            var prog = new ProgressWindow { Owner = this, Hint = "Transcribing audio, please wait…" }; prog.Show();
            try
            {
                int n = dlg.RecordedPcm.Length / 2;
                var pcm = new float[n];
                for (int i = 0; i < n; i++)
                    pcm[i] = BitConverter.ToInt16(dlg.RecordedPcm, i * 2) / 32768f;

                CommandInput.Text = await Task.Run(() => WhisperNative.Transcribe(pcm));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Transcribe error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { prog.Close(); }
        }

        // ────────────── Search button ──────────────
        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            string query = CommandInput.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(query))
            {
                MessageBox.Show("Please input something before searching");
                return;
            }

            SearchButton.IsEnabled = CleanButton.IsEnabled = false;

            // Open the progress popup
            var prog = new ProgressWindow
            {
                Owner = this,
                Hint = "Extracting keywords, please wait..."
            };
            prog.Show();

            try
            {
                // Use the wrapped client (stdout one-line only + parse + fallback)
                var keywords = await _kwClient.ExtractAsync(query, CancellationToken.None);

                if (keywords.Length == 0)
                {
                    MessageBox.Show("No keywords extracted.");
                    return;
                }

                // Find / Create MainWindow
                MainWindow main = Application.Current.Windows
                                              .OfType<MainWindow>()
                                              .FirstOrDefault();
                if (main == null)
                {
                    main = new MainWindow();
                    main.Show();               // Show first, then call the search.
                }
                else
                {
                    main.Show();               // Maybe it was hidden before.
                    main.Activate();
                }

                // Hand the keywords over to MainWindow.
                main.PerformSearchWithKeywords(keywords);

                // hide the AIPage
                this.Hide();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Keyword extraction failed:\n" + ex.Message);
            }
            finally
            {
                prog.Close();                                  // close the progress popup
                SearchButton.IsEnabled = CleanButton.IsEnabled = true;
            }
        }

        // ────────────── (Replaced) call sidecar: delegate to client ──────────────
        private Task<string[]> ExtractKeywordsAsync(string userInput)
        {
            // For compatibility with your old calls, just delegate to _kwClient
            return _kwClient.ExtractAsync(userInput, CancellationToken.None);
        }

        private void HowToUseLink_Click(object sender, RoutedEventArgs e)
        {
            new HowToUseWindow { Owner = this }.ShowDialog();
        }

        // ────────────── Warm-up ──────────────
        private async Task WarmupKeywordService()
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var _ = await _kwClient.ExtractAsync("warm-up", CancellationToken.None);
                Debug.WriteLine($"[Warmup] finished in {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Warm-up failed: " + ex.Message);
            }
        }
    }
}
