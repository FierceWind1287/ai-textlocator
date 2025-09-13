using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace ASR.IntegrationTests
{
    [Collection("ASR-collection")]
    public class E2E_Tests : IClassFixture<AsrFixture>
    {
        private readonly AsrFixture _fx;
        public E2E_Tests(AsrFixture fx) { _fx = fx; }

        /// <summary>
        /// End-to-end: wav -> ASR -> text -> KeywordService.exe -> keywords (3~5)
        /// </summary>
        [Fact(Timeout = 180_000)]                    // Allow enough time (including model loading)
        public async Task Wav_To_Keywords_EndToEnd() // Use async to avoid blocking the pipeline
        {
            // 1) Prepare audio (make sure it is 16kHz/Mono/16-bit PCM)
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string wav = Path.Combine(baseDir, "testdata", "hello16k.wav");   // you can also use how_are_you_doing_today.wav
            Assert.True(File.Exists(wav), "Missing testdata/hello16k.wav (16kHz/mono/16-bit PCM)");

            // 2) ASR: wav -> float[] -> text
            float[] pcm = Wav16kMonoToFloat(wav);
            string text = WhisperNativeForTests.DoTranscribe(pcm);
            Assert.False(string.IsNullOrWhiteSpace(text), "ASR returned empty text, please check extern/ model integrity.");
            Debug.WriteLine("[ASR] " + text);

            // 3) Call KeywordService.exe (.NET 8 sidecar)
            string exe = FindKeywordServiceExe();
            Assert.True(File.Exists(exe), "Cannot find KeywordService.exe: " + exe);

            string escaped = text.Replace("\"", "\\\"");
            string workDir = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory;

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "\"" + escaped + "\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workDir,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // Performance/Stability environment variables (should match the main program)
            SetOrAdd(psi, "KEYWORD_CTX", "512");
            SetOrAdd(psi, "KEYWORD_GPU_LAYERS", "6");
            SetOrAdd(psi, "KEYWORD_MAXTOK", "24");

            string stdout, stderr;
            var errBuf = new StringBuilder();

            using (var p = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                p.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        errBuf.AppendLine(e.Data);
                };

                Assert.True(p.Start(), "Unable to start KeywordService.exe");
                p.BeginErrorReadLine();                          // async read stderr
                var outTask = p.StandardOutput.ReadToEndAsync(); // async read stdout

                // Wait at most 60s (first-time model loading may be slow)
                var finished = await Task.WhenAny(outTask, Task.Delay(60_000));
                if (finished != outTask)
                {
                    try { if (!p.HasExited) p.Kill(); } catch { }
                    throw new TimeoutException("KeywordService timeout (>60s). stderr:\n" + errBuf);
                }

                stdout = (await outTask).Trim();
                p.WaitForExit();
                stderr = errBuf.ToString().Trim();
            }

            Debug.WriteLine("[KS][stdout] " + stdout);
            if (!string.IsNullOrEmpty(stderr))
                Debug.WriteLine("[KS][stderr] " + stderr);

            // 4) Parse keywords and assert 3~5 items
            var keywords = stdout
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(s => s.Replace('_', ' '))                       // Clean up same as main program
                .Select(s => Regex.Replace(s, @"[^a-z0-9 \-_]", ""))    // Keep only safe characters
                .Where(s => s.Length > 0)
                .ToArray();

            Assert.InRange(keywords.Length, 3, 5);

            var re = new Regex("^[a-z0-9 _-]+$");
            foreach (var k in keywords)
                Assert.True(re.IsMatch(k), "Invalid keyword: " + k);
        }

        private static void SetOrAdd(ProcessStartInfo psi, string key, string val)
        {
            if (psi.Environment.ContainsKey(key)) psi.Environment[key] = val;
            else psi.Environment.Add(key, val);
        }

        // —— WAV reader (validate 16k/Mono/16-bit PCM) ——
        private static float[] Wav16kMonoToFloat(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs))
            {
                string riff = new string(br.ReadChars(4)); if (riff != "RIFF") throw new InvalidDataException();
                br.ReadInt32();
                string wave = new string(br.ReadChars(4)); if (wave != "WAVE") throw new InvalidDataException();

                string id = new string(br.ReadChars(4));
                while (id != "fmt ")
                {
                    int skip = br.ReadInt32(); fs.Seek(skip, SeekOrigin.Current);
                    id = new string(br.ReadChars(4));
                }
                int fmtLen = br.ReadInt32();
                short fmt = br.ReadInt16(); short ch = br.ReadInt16(); int sr = br.ReadInt32();
                br.ReadInt32(); br.ReadInt16(); short bps = br.ReadInt16();
                if (fmtLen > 16) fs.Seek(fmtLen - 16, SeekOrigin.Current);
                if (fmt != 1 || ch != 1 || sr != 16000 || bps != 16)
                    throw new InvalidDataException("Requires 16kHz/Mono/16-bit PCM");

                id = new string(br.ReadChars(4));
                while (id != "data")
                {
                    int skip = br.ReadInt32(); fs.Seek(skip, SeekOrigin.Current);
                    id = new string(br.ReadChars(4));
                }
                int dataLen = br.ReadInt32();
                int n = dataLen / 2;
                var arr = new float[n];
                for (int i = 0; i < n; i++) arr[i] = br.ReadInt16() / 32768f;
                return arr;
            }
        }

        // —— Find KeywordService.exe (prefer Release, fallback Debug) ——
        private static string FindKeywordServiceExe()
        {
            // From test output directory back to solution root: …\bin\x64\Release\net462\ → root
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string root = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));

            string rel = Path.Combine(root, "KeywordService", "bin", "Release", "net8.0", "KeywordService.exe");
            if (File.Exists(rel)) return rel;

            string dbg = Path.Combine(root, "KeywordService", "bin", "Debug", "net8.0", "KeywordService.exe");
            if (File.Exists(dbg)) return dbg;

            return rel; // Return expected Release path for error reporting
        }
    }
}
