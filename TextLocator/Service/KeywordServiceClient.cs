// TextLocator/Service/KeywordServiceClient.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TextLocator.Util;

namespace TextLocator.Service
{
    public sealed class KeywordServiceClient
    {
        private readonly IProcessRunner _runner;
        private readonly string _exePath;
        private readonly string _workDir;
        private readonly int _timeoutMs;

        private static bool _cold = true; // Treat the first call as a cold start

        public KeywordServiceClient(IProcessRunner runner, string exePath, int timeoutMs = 15000)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _exePath = exePath ?? throw new ArgumentNullException(nameof(exePath));
            _workDir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            _timeoutMs = timeoutMs;
        }

        public async Task<string[]> ExtractAsync(string rawQuery, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(rawQuery)) return Array.Empty<string>();

            // Longer timeout for cold start (first model loading)
            int timeout = _cold ? Math.Max(_timeoutMs, 60000) : _timeoutMs;

            // Parameters: safely escape, wrap the whole sentence with quotes
            string args = "\"" + (rawQuery?.Replace("\"", "\\\"") ?? "") + "\"";

            // Log directory: use user-writable directory to avoid Program Files permission issues
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TextLocatorAI", "KeywordService", "logs");
            try { Directory.CreateDirectory(logDir); } catch { /* Ignore directory creation failure */ }

            var psi = new ProcessStartInfo
            {
                FileName = _exePath,
                Arguments = args,
                WorkingDirectory = _workDir,              // Important: let the sidecar resolve relative paths (e.g., Models) from its own directory
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            // Place logs into a writable directory
            psi.EnvironmentVariables["KEYWORD_LOG_DIR"] = logDir;

            var (stdout, stderr, exitCode) = await _runner.RunAsync(psi, timeout, ct);

            // After one successful run, no longer considered a cold start
            if (exitCode == 0) _cold = false;

            // Sidecar stdout only outputs "the final one-line CSV"
            string line = (stdout ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "";

            // Parse CSV
            var arr = KeywordCsvParser.ParseCsv(line)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (exitCode == 0 && arr.Length >= 3)
                return arr;

            // Fallback if failed or too few: split by space to get 3–5 words
            var fallback = (rawQuery ?? "")
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToLowerInvariant())
                .Where(s => s.Length >= 2)
                .Take(5)
                .ToArray();

            return fallback;
        }

        public static KeywordServiceClient FromAppBase(IProcessRunner runner, int timeoutMs = 15000)
        {
            // Sidecar is placed under bin\...\extern\KeywordService\KeywordService.exe
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var exe = Path.Combine(baseDir, "extern", "KeywordService", "KeywordService.exe");
            return new KeywordServiceClient(runner, exe, timeoutMs);
        }
    }
}
