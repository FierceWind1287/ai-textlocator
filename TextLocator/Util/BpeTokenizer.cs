using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;


namespace TextLocator.Util
{
    public class BpeTokenizer
    {
        // 词 -> id
        private readonly Dictionary<string, int> _vocab;
        // merge 顺序列表
        private readonly Dictionary<(string, string), int> _bpeRanks;

        public BpeTokenizer(string vocabJsonPath, string mergesTxtPath)
        {
            // 1) load vocab.json
            _vocab = JsonConvert
                .DeserializeObject<Dictionary<string, int>>(File.ReadAllText(vocabJsonPath))
                ?? throw new Exception("vocab.json 解析失败");

            // 2) load merges.txt
            _bpeRanks = new Dictionary<(string, string), int>();
            var lines = File.ReadAllLines(mergesTxtPath);
            for (int i = 1; i < lines.Length; i++) // 跳过第一行 header
            {
                var parts = lines[i].Split(' ');
                if (parts.Length == 2)
                    _bpeRanks[(parts[0], parts[1])] = i;
            }
        }

        /// <summary>
        /// 把一句普通英文文本，返回对应的 token id 列表
        /// </summary>
        public List<int> Encode(string text)
        {
            var ids = new List<int>();
            // Roberta/SentencePiece 约定：空格前缀用 'Ġ'
            text = text.Replace("\r\n", "\n");
            foreach (var word in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // 在词前加 'Ġ' 标记空格
                var token = "Ġ" + word.ToLowerInvariant();
                ids.AddRange(Bpe(token));
            }
            return ids;
        }

        private IEnumerable<int> Bpe(string token)
        {
            // 初始拆成字符
            var chars = token.Select(c => c.ToString()).ToList();

            // 合并直到没有可合并对
            while (true)
            {
                // 找到当前所有相邻对，选取 ranks 最小的那个
                (int rank, int idx) best = (int.MaxValue, -1);
                for (int i = 0; i < chars.Count - 1; i++)
                {
                    var pair = (chars[i], chars[i + 1]);
                    if (_bpeRanks.TryGetValue(pair, out var r) && r < best.rank)
                        best = (r, i);
                }

                if (best.idx < 0) break; // 没有可合并

                // 执行一次 merge
                int i0 = best.idx;
                chars[i0] = chars[i0] + chars[i0 + 1];
                chars.RemoveAt(i0 + 1);
            }

            // 最终每个子词查表
            foreach (var piece in chars)
            {
                if (_vocab.TryGetValue(piece, out var id))
                    yield return id;
                else
                    yield return _vocab["<unk>"]; // 不在表里就当 <unk>
            }
        }
    }
}
