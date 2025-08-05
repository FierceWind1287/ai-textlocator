using NAudio.Wave;
using System;
using System.IO;
using System.Windows;

namespace TextLocator   // ← 与项目命名空间保持一致
{
    public partial class RecordWindow : Window
    {
        private WaveInEvent? _waveIn;
        private MemoryStream? _buf;

        public byte[]? RecordedPcm { get; private set; }   // 16‑bit PCM

        public RecordWindow() => InitializeComponent();

        /* ------------ 事件 ------------ */

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16_000, 16, 1)
            };
            _buf = new MemoryStream();

            _waveIn.DataAvailable += (s, a) =>
            {
                _buf!.Write(a.Buffer, 0, a.BytesRecorded);
                // 粗略峰值映射到 0‑100
                short peak = 0;
                for (int i = 0; i < a.BytesRecorded; i += 2)
                {
                    short sample = BitConverter.ToInt16(a.Buffer, i);
                    if (sample < 0) sample = (short)-sample;
                    if (sample > peak) peak = sample;
                }
                Dispatcher.Invoke(() => pbLevel.Value = peak / 327.68);
            };

            _waveIn.StartRecording();

            txtStatus.Text = "Recording...";
            btnStart.IsEnabled = false;
            btnStop.IsEnabled = true;
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _waveIn?.StopRecording();
            _waveIn?.Dispose();
            _waveIn = null;

            RecordedPcm = _buf?.ToArray();
            _buf?.Dispose();
            _buf = null;

            double sec = (RecordedPcm?.Length ?? 0) / 32000.0;  // 16 k 16‑bit mono
            txtStatus.Text = $"Recorded {sec:F1} seconds";
            btnStop.IsEnabled = false;
            btnRetry.IsEnabled = btnOK.IsEnabled = true;
        }

        private void BtnRetry_Click(object sender, RoutedEventArgs e)
        {
            RecordedPcm = null;
            pbLevel.Value = 0;
            txtStatus.Text = "Waiting to start...";
            btnStart.IsEnabled = true;
            btnRetry.IsEnabled = btnOK.IsEnabled = false;
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            if (RecordedPcm == null)
            {
                MessageBox.Show("No recording yet"); return;
            }
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            RecordedPcm = null;
            DialogResult = false;
        }
    }
}
