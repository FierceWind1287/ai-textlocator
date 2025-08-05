using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KeywordAI
{
    public static class KeywordUtils
    {
        /// <summary>最简单清洗：保留 a-z、0-9、空格，去重</summary>
        public static string CleanRawOutput(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            // ① 全部转小写，去掉换行和引号
            string normalized = raw.ToLowerInvariant()
                                    .Replace("\r", " ")
                                    .Replace("\n", " ")
                                    .Replace("\"", "");

            // ② 按逗号拆短语，Trim→去重→取前 5
            var phrases = normalized.Split(',')
                                    .Select(p => p.Trim())
                                    .Where(p => p.Length > 0)
                                    .Distinct()
                                    .Take(5);

            // ③ 用 “, ” 再拼回去
            return string.Join(", ", phrases);
        }

    }
}
