using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TextLocator.Util;

namespace TextLocator
{
    public partial class AIPage : Window
    {
        // ────────────── 构造 ──────────────
        public AIPage()
        {
            InitializeComponent();
            LoadAreaInfo();                       // 填充“Search Area”信息

            // ★ 可选：后台预热一次 KeywordService，首帧加载更快
            _ = Task.Run(() => WarmupKeywordService());
        }

        // ────────────── MainWindow 切换 ──────────────
        private void EnterFileSearch_Click(object sender, RoutedEventArgs e)
        {
            var main = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            (main ?? new MainWindow()).Show();     // 没开就新建
            main?.Activate();
            Close();
        }

        // ────────────── 区域设置对话框 ──────────────
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
                list.Select(a => $"{a.AreaName}: {string.Join("，", a.AreaFolders)}"));
        }

        // ────────────── 输入框 & 清空键 ──────────────
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

        // ────────────── 录音相关（与你原来保持一致）──────────────
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

        private async void BtnMic_Click(object s, RoutedEventArgs e)
        {
            var dlg = new RecordWindow { Owner = this };
            if (dlg.ShowDialog() != true || dlg.RecordedPcm == null) return;

            var prog = new ProgressWindow { Owner = this,Hint= "Transcribing audio, please wait…" }; prog.Show();
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

        // ────────────────── 搜索按钮 ──────────────────
        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            string query = CommandInput.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(query))
            {
                MessageBox.Show("Please input something before searching");
                return;
            }

            SearchButton.IsEnabled = CleanButton.IsEnabled = false;

            // ① 打开进度弹窗
            var prog = new ProgressWindow
            {
                Owner = this,
                Hint = "Extracting keywords, please wait..."
            };
            prog.Show();

            try
            {
                // ② 在后台线程执行关键词提取（不会阻塞 UI）
                string[] keywords = await Task.Run(() => ExtractKeywordsAsync(query));

                if (keywords.Length == 0)
                {
                    MessageBox.Show("No keywords extracted.");
                    return;
                }

                // ③ 找到 / 创建 MainWindow
                MainWindow main = Application.Current.Windows
                                              .OfType<MainWindow>()
                                              .FirstOrDefault();
                if (main == null)
                {
                    main = new MainWindow();
                    main.Show();               // 先 Show 再调用搜索
                }
                else
                {
                    main.Show();               // 可能之前被 Hide
                    main.Activate();
                }

                // 把关键词交给 MainWindow
                main.PerformSearchWithKeywords(keywords);

                // ④ （可选）隐藏 AIPage 本身
                this.Hide();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Keyword extraction failed:\n" + ex.Message);
            }
            finally
            {
                prog.Close();                                  // 关闭进度条
                SearchButton.IsEnabled = CleanButton.IsEnabled = true;
            }
        }


        // ────────────────── 调用 KeywordService.exe ──────────────────
        private async Task<string[]> ExtractKeywordsAsync(string userInput)
        {
            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KeywordService.exe");
            if (!File.Exists(exe))
                throw new FileNotFoundException("Keyword service does not exist", exe);

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "\"" + userInput.Replace("\"", "\\\"") + "\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi)!;
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            var readAllTask = Task.WhenAll(outTask, errTask);     // ★ 只建一次

            // 40s → 60s
            var finished = await Task.WhenAny(readAllTask, Task.Delay(60_000));
            if (finished != readAllTask)
            {
                try { p.Kill(); } catch { }
                string err = await errTask;
                throw new TimeoutException("KeywordService initial load timeout (>60s).\n" +
                                           "Please retry or check the CPU usage / model path.\n" + err);
            }


            string raw = (await outTask).Trim();
            Debug.WriteLine("KeywordService >>> " + raw);

            return string.IsNullOrWhiteSpace(raw)
                   ? Array.Empty<string>()
                   : raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)  // ← 只看逗号
                        .Select(k => k.Trim())                                        // 去首尾空格
                        .Where(k => k.Length > 0)
                        .Distinct()
                        .ToArray();
        }

        // ────────────────── 预热 ──────────────────
        private async Task WarmupKeywordService()
        {
            try { await ExtractKeywordsAsync("warm-up"); }
            catch (Exception ex) { Debug.WriteLine("Warm-up failed: " + ex.Message); }
        }
    }
}
