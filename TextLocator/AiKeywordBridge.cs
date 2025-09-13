using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace TextLocator
{
    public static class AiKeywordBridge
    {
        /// <summary>
        /// Synchronously calls the sidecar KeywordService.exe and returns
        /// deduplicated lowercase keywords.
        /// Only intended for .NET Framework 4.x synchronous scenarios
        /// (do not call directly on the UI thread; wrap with Task.Run instead).
        /// </summary>
        public static string[] GetKeywords(string userQuery,
                                           string keywordServicePath,
                                           int timeoutMs = 3000)
        {
            if (string.IsNullOrWhiteSpace(userQuery))
                return Array.Empty<string>();

            string workDir = Path.GetDirectoryName(keywordServicePath) ?? AppDomain.CurrentDomain.BaseDirectory;

            // Log directory: user-writable directory
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TextLocatorAI", "KeywordService", "logs");
            try { Directory.CreateDirectory(logDir); } catch { /* Ignore directory creation failure */ }

            var psi = new ProcessStartInfo
            {
                FileName = keywordServicePath,
                Arguments = $"\"{(userQuery ?? "").Replace("\"", "\\\"")}\"",
                WorkingDirectory = workDir,                // Important
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            // Place logs into a writable directory
            psi.EnvironmentVariables["KEYWORD_LOG_DIR"] = logDir;

            using (var p = Process.Start(psi))
            {
                if (p == null)
                    throw new InvalidOperationException("Unable to start KeywordService process.");

                // Asynchronous read (prevent blocking)
                var tOut = p.StandardOutput.ReadToEndAsync();
                var tErr = p.StandardError.ReadToEndAsync();

                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    throw new TimeoutException("KeywordService timed out.");
                }

                // Allow up to 1s extra for finishing read
                System.Threading.Tasks.Task.WaitAll(new[] { tOut, tErr }, 1000);

                string stdout = tOut.Result ?? "";
                string stderr = tErr.Result ?? "";

                if (!string.IsNullOrWhiteSpace(stderr))
                    Debug.WriteLine("[KeywordService stderr] " + stderr);

                // Sidecar stdout only outputs "final single-line CSV"
                string firstLine = stdout
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault() ?? "";

                // Split, trim, deduplicate, lowercase
                string[] arr = firstLine
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .Select(s => s.ToLowerInvariant())
                    .Distinct()
                    .ToArray();

                if (arr.Length >= 3)
                    return arr;

                // Fallback: split by spaces
                return (userQuery ?? "")
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim().ToLowerInvariant())
                    .Where(s => s.Length >= 2)
                    .Take(5)
                    .ToArray();
            }
        }

        public static string FormatKeywordsForDisplay(string[] keywords)
            => (keywords == null || keywords.Length == 0) ? string.Empty : string.Join(", ", keywords);
    }
}
