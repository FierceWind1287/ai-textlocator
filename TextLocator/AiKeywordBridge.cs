using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace TextLocator
{
    public static class AiKeywordBridge
    {
        /// <summary>
        /// Call the external KeywordService.exe and return an array of keywords (in lowercase, without duplicates).
        /// Compatible with .NET Framework 4.x: Does not rely on asynchronous APIs with CancellationToken.
        /// </summary>
        public static string[] GetKeywords(string userQuery,
                                           string keywordServicePath,
                                           int timeoutMs = 3_000)
        {
            // 1. Process startup information
            var psi = new ProcessStartInfo
            {
                FileName = keywordServicePath,
                Arguments = $"\"{userQuery.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(psi))
            {
                if (process == null)
                    throw new InvalidOperationException("Unable to start the KeywordService process.");

                // 2. Asynchronous reading of stdout / stderr
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();

                // Wait to exit (with timeout)
                bool exited = process.WaitForExit(timeoutMs);
                if (!exited)
                {
                    try { process.Kill(); } catch { /* ignore */ }
                    throw new TimeoutException("KeywordService did not return within the specified time.");
                }

                // Wait to read the task for a maximum of 1 second.
                Task.WaitAll(new[] { outputTask, errorTask }, 1_000);

                string stdOut = outputTask.Result ?? string.Empty;
                string stdErr = errorTask.Result ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(stdErr))
                    Debug.WriteLine($"KeywordService stderr: {stdErr}");

                // 3. Extract the line 'Cleaned keywords:' from stdout.
                string cleanedLine = ExtractCleanedKeywordsLine(stdOut);

                if (string.IsNullOrWhiteSpace(cleanedLine))
                    return Array.Empty<string>();

                // 4. Split, deduplicate, lowercase
                string[] keywords = cleanedLine
                    .Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(k => k.Trim().ToLowerInvariant())
                    .Distinct()
                    .ToArray();

                return keywords;
            }
        }

        /// <summary>
        /// Extract the line/part after 'Cleaned keywords:' from the entire stdout text.
        /// </summary>
        private static string ExtractCleanedKeywordsLine(string stdout)
        {
            if (string.IsNullOrWhiteSpace(stdout)) return string.Empty;

            string[] lines = stdout
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // same row model: Cleaned keywords: xxx, yyy, zzz
                const string tag = "Cleaned keywords:";
                if (line.StartsWith(tag, StringComparison.OrdinalIgnoreCase))
                {
                    string inline = line.Substring(tag.Length).Trim();
                    if (!string.IsNullOrWhiteSpace(inline))
                        return inline;

                    // Next row mode: the real keyword comes after the line break.
                    if (i + 1 < lines.Length)
                        return lines[i + 1].Trim();
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Concatenate the keyword array for UI display.
        /// </summary>
        public static string FormatKeywordsForDisplay(string[] keywords)
        {
            return (keywords == null || keywords.Length == 0)
                   ? string.Empty
                   : string.Join(", ", keywords);
        }
    }
}
