using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KeywordAI
{
    public static class KeywordUtils
    {
        /// <summary>
        /// Simple cleaning: keep a-z, 0-9, and spaces; remove duplicates
        /// </summary>
        public static string CleanRawOutput(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            // ① Convert everything to lowercase, remove line breaks and quotes
            string normalized = raw.ToLowerInvariant()
                                    .Replace("\r", " ")
                                    .Replace("\n", " ")
                                    .Replace("\"", "");

            // ② Split phrases by commas → Trim → remove duplicates → take first 5
            var phrases = normalized.Split(',')
                                    .Select(p => p.Trim())
                                    .Where(p => p.Length > 0)
                                    .Distinct()
                                    .Take(5);

            // ③ Join phrases back together with ", "
            return string.Join(", ", phrases);
        }
    }
}
