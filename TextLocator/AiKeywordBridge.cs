using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace TextLocator
{
    public static class AiKeywordBridge
    {
        /// <summary>
        /// 调用外部 KeywordService.exe 并返回关键词数组（小写、去重）。
        /// 兼容 .NET Framework 4.x：不依赖带 CancellationToken 的异步 API。
        /// </summary>
        public static string[] GetKeywords(string userQuery,
                                           string keywordServicePath,
                                           int timeoutMs = 3_000)
        {
            // 1. 进程启动信息
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
                    throw new InvalidOperationException("无法启动 KeywordService 进程。");

                // 2. 异步读取 stdout / stderr
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();

                // 等待退出（带超时）
                bool exited = process.WaitForExit(timeoutMs);
                if (!exited)
                {
                    try { process.Kill(); } catch { /* 忽略 */ }
                    throw new TimeoutException("KeywordService 未在限定时间内返回。");
                }

                // 再等读取任务最多 1 秒
                Task.WaitAll(new[] { outputTask, errorTask }, 1_000);

                string stdOut = outputTask.Result ?? string.Empty;
                string stdErr = errorTask.Result ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(stdErr))
                    Debug.WriteLine($"KeywordService stderr: {stdErr}");

                // 3. 从 stdout 中提取 “Cleaned keywords:” 行
                string cleanedLine = ExtractCleanedKeywordsLine(stdOut);

                if (string.IsNullOrWhiteSpace(cleanedLine))
                    return Array.Empty<string>();

                // 4. 拆分、去重、小写
                string[] keywords = cleanedLine
                    .Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(k => k.Trim().ToLowerInvariant())
                    .Distinct()
                    .ToArray();

                return keywords;
            }
        }

        /// <summary>
        /// 从整段 stdout 文本中提取“Cleaned keywords:”后面的那一行/那部分。
        /// </summary>
        private static string ExtractCleanedKeywordsLine(string stdout)
        {
            if (string.IsNullOrWhiteSpace(stdout)) return string.Empty;

            string[] lines = stdout
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // 同行模式：Cleaned keywords: xxx, yyy, zzz
                const string tag = "Cleaned keywords:";
                if (line.StartsWith(tag, StringComparison.OrdinalIgnoreCase))
                {
                    string inline = line.Substring(tag.Length).Trim();
                    if (!string.IsNullOrWhiteSpace(inline))
                        return inline;

                    // 下一行模式：换行后是真正关键词
                    if (i + 1 < lines.Length)
                        return lines[i + 1].Trim();
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 将关键词数组按 ", " 拼接，供 UI 显示。
        /// </summary>
        public static string FormatKeywordsForDisplay(string[] keywords)
        {
            return (keywords == null || keywords.Length == 0)
                   ? string.Empty
                   : string.Join(", ", keywords);
        }
    }
}
