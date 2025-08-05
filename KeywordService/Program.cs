using LLama;
using LLama.Common;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KeywordAI;            // 你的 CleanRawOutput

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        // ── 0. 将后续 Console.WriteLine 全部重定向到 stderr ──────────────────
        var stdout = Console.Out;                // 先保存真正的 stdout
        Console.SetOut(Console.Error);           // 日志一律写到 stderr

        // ── 1. 读取查询 ────────────────────────────────────────────────
        if (args.Length == 0)
        {
            Console.Error.Write("请输入查询：");
            args = new[] { Console.ReadLine() ?? "" };
        }
        string userInput = string.Join(" ", args);

        // ── 2. 模型路径 (相对路径，防止部署目录不同) ─────────────────────
        string modelPath = Path.Combine(AppContext.BaseDirectory,
                                 "granite-3.3-2b-instruct-Q4_K_M.gguf");
        if (!File.Exists(modelPath))
        {
            Console.Error.WriteLine($"Model not found: {modelPath}");
            return 1;
        }

        // ── 3. Prompt ──────────────────────────────────────────────────
        string prompt = $@"You are a keyword-extraction assistant.
**Return *exactly 3-5* distinct core keywords or short phrases,
comma-separated, lowercase, no line breaks, no extra words.**

query: ""{userInput}""
keywords:";

        // ── 4. 加载模型 ────────────────────────────────────────────────
        var mp = new ModelParams(modelPath) { ContextSize = 512, GpuLayerCount = 6 };
        using var w = LLamaWeights.LoadFromFile(mp);
        using var cx = w.CreateContext(mp);
        var ex = new StatelessExecutor(w, mp);

        // ── 5. 推理（用 AntiPrompt “Query:” 终止，而不是遇到换行就停） ─────
        var ip = new InferenceParams
        {
            MaxTokens = 60,
            AntiPrompts = new[] { "Query:" }
        };

        // 6. 收集模型输出
        var sb = new StringBuilder();
        await foreach (var tk in ex.InferAsync(prompt, ip, CancellationToken.None))
            sb.Append(tk);

        string rawOutput = sb.ToString();

        // ───────★ 只加下面这一段保险丝 ★─────────
        static string StripSecondRound(string txt)
        {
            if (string.IsNullOrWhiteSpace(txt)) return "";

            // ① 统一换行 -> 空格，方便后面找
            txt = txt.Replace('\n', ' ').Replace("\r", "");

            // ② 出现下列关键词就截断
            string[] stops = { "query:", "keywords:", "keyword:" };

            int cut = txt.Length;
            foreach (var stop in stops)
            {
                int idx = txt.IndexOf(stop, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0 && idx < cut) cut = idx;
            }

            return txt.Substring(0, cut).Trim();
        }

        rawOutput = StripSecondRound(rawOutput);

        // —— 新增：如果出现下一轮 prompt，就把它切掉 —— 
        int cut = rawOutput.IndexOf("query:", StringComparison.OrdinalIgnoreCase);
        if (cut >= 0)            // 找到了 "query:"
            rawOutput = rawOutput.Substring(0, cut);

        rawOutput = rawOutput.Trim();


        // ── 6. 清洗关键词 ──────────────────────────────────────────────
        string cleaned = KeywordUtils.CleanRawOutput(rawOutput);

        // ── 7. 只把最后结果写到 **stdout** ──────────────────────────────
        Console.SetOut(stdout);                  // 切换回 stdout
        Console.WriteLine(cleaned.Trim());

        // 如果是交互模式，方便手动运行查看
        if (args.Length == 0 && !Console.IsInputRedirected)
        {
            Console.Error.WriteLine("(Press any key to exit)");
            Console.ReadKey(true);
        }
        return 0;
    }
}
