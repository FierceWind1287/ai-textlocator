using System;
using System.Linq;

namespace TextLocator.Util
{
    public static class KeywordCsvParser
    {
        public static string[] ParseCsv(string rawCsv)
        {
            if (string.IsNullOrWhiteSpace(rawCsv)) return Array.Empty<string>();
            return rawCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(k => k.Trim().ToLowerInvariant())
                         .Where(k => k.Length > 0)
                         .Take(5) // 最多取5个
                         .ToArray();
        }
    }
}
