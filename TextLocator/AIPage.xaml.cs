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
                // 建议：这里其实不用 Task.Run，因为 ExtractKeywordsAsync 本身就是异步的
                string[] keywords = await ExtractKeywordsAsync(query);

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

        // ────────────────── 调用 KeywordService.exe（实时 stderr 日志 + 超时） ──────────────────
        private async Task<string[]> ExtractKeywordsAsync(string userInput)
        {
            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KeywordService.exe");
            if (!File.Exists(exe))
                throw new FileNotFoundException("Keyword service does not exist", exe);

            string escaped = userInput.Replace("\"", "\\\"");
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "\"" + escaped + "\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!    // ★ 固定工作目录，避免相对路径问题
            };

            using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

            // 收集 stderr，便于超时时抛出
            var errBuf = new System.Text.StringBuilder();
            p.ErrorDataReceived += (s, ev) =>
            {
                if (!string.IsNullOrEmpty(ev.Data))
                {
                    errBuf.AppendLine(ev.Data);
                    Debug.WriteLine("[KeywordService][stderr] " + ev.Data);   // VS Output 实时看到
                }
            };

            // 如果你也想实时看 stdout，可取消注释；否则保留一次性读取便于解析
            // p.OutputDataReceived += (s, ev) =>
            // {
            //     if (!string.IsNullOrEmpty(ev.Data))
            //         Debug.WriteLine("[KeywordService][stdout] " + ev.Data);
            // };

            var sw = Stopwatch.StartNew();
            p.Start();
            p.BeginErrorReadLine();
            // p.BeginOutputReadLine(); // 我们还是一次性读 stdout

            // 一次性读取 stdout（关键词结果）
            var outTask = p.StandardOutput.ReadToEndAsync();

            // 等待完成或超时
            var finished = await Task.WhenAny(outTask, Task.Delay(60_000));
            if (finished != outTask)
            {
                try { if (!p.HasExited) p.Kill(); } catch { /* ignore */ }
                Debug.WriteLine($"[KeywordService][timeout] after {sw.ElapsedMilliseconds} ms");
                throw new TimeoutException("KeywordService initial load timeout (>60s).\n" + errBuf.ToString());
            }

            // 确保子进程退出，收尾 stderr
            p.WaitForExit();
            Debug.WriteLine($"[KeywordService] done in {sw.ElapsedMilliseconds} ms");

            string raw = (await outTask).Trim();
            Debug.WriteLine("[KeywordService][stdout] " + raw);

            return string.IsNullOrWhiteSpace(raw)
                   ? Array.Empty<string>()
                   : raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(k => k.Trim())
                        .Where(k => k.Length > 0)
                        .Distinct()
                        .ToArray();
        }

        // ────────────────── 预热 ──────────────────
        private async Task WarmupKeywordService()
        {
            try
            {
                var sw = Stopwatch.StartNew();
                _ = await ExtractKeywordsAsync("warm-up"); // 日志会在 ExtractKeywordsAsync 里输出
                Debug.WriteLine($"[Warmup] finished in {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Warm-up failed: " + ex.Message);
            }
        }
    }
}
