using LLama;
using LLama.Common;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;   // ★ 用于 NativeLibrary 和 Win32 API
using KeywordAI;

internal class Program
{
    // ─────────────── 全局计时 & 文件日志 ───────────────
    static readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
    static StreamWriter? _fileLog;

    static void InitFileLog()
    {
        string? dir = Environment.GetEnvironmentVariable("KEYWORD_LOG_DIR");
        if (string.IsNullOrWhiteSpace(dir)) dir = AppContext.BaseDirectory;
        Directory.CreateDirectory(dir!);
        string path = Path.Combine(dir!, "ks.log");

        _fileLog = new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
            new UTF8Encoding(false))
        { AutoFlush = true };

        Console.Error.WriteLine($"[logger] file log at: {path}");
        _fileLog!.WriteLine();
        _fileLog!.WriteLine($"==== KeywordService start {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} pid={Environment.ProcessId} ====");
    }

    static void CloseFileLog()
    {
        try { _fileLog?.Flush(); _fileLog?.Dispose(); } catch { }
        _fileLog = null;
    }

    static void Log(string msg)
    {
        string line = $"[{_sw.Elapsed.TotalSeconds,7:0.000}s][pid {Environment.ProcessId}] {msg}";
        Console.Error.WriteLine(line);
        try { _fileLog?.WriteLine(line); } catch { }
    }

    // —— 兜底：从用户原文里抠 3–5 个内容词，避免空结果 —— 
    static string FallbackFromUserText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var stop = new HashSet<string>(new[] {
            "the","a","an","and","or","of","to","for","in","on","at","by","with","as","from","about",
            "is","are","was","were","be","been","being","this","that","these","those",
            "it","its","into","than","then","over","under","between","among","within","without",
            "how","what","why","when","where","who","whom","which","please"
        }, StringComparer.OrdinalIgnoreCase);

        var tokens = new string(text.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == '_').ToArray())
                        .Replace('_', ' ')
                        .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim().ToLowerInvariant())
                        .Where(s => s.Length >= 3 && !stop.Contains(s))
                        .Distinct()
                        .Take(5);

        return string.Join(", ", tokens);
    }

    // —— 打印实际被加载的 llama.dll 路径 —— 
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetModuleFileName(IntPtr hModule, StringBuilder lpFilename, int nSize);

    static string? GetLoadedPath(string module)
    {
        var h = GetModuleHandle(module);
        if (h == IntPtr.Zero) return null;
        var sb = new StringBuilder(1024);
        var n = GetModuleFileName(h, sb, sb.Capacity);
        return n > 0 ? sb.ToString() : null;
    }

    // —— 强制预加载 CUDA 版原生库（在 LoadFromFile 之前调用）——
    static void PreloadCudaNativeLibs()
    {
        string cudaDir = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "cuda12");
        string[] candidates =
        {
            Path.Combine(cudaDir, "ggml-cuda.dll"),  // 有的包名就是这个
            Path.Combine(cudaDir, "ggml-base.dll"),  // 有的包还会带这个
            Path.Combine(cudaDir, "llama.dll"),

        };

        foreach (var p in candidates)
        {
            if (!File.Exists(p)) continue;
            try
            {
                NativeLibrary.Load(p);
                Log("preloaded: " + p);
            }
            catch (Exception ex)
            {
                Log("WARN preload failed: " + p + " -> " + ex.Message);
            }
        }

        // 打印实际命中的 llama.dll
        var loaded = GetLoadedPath("llama.dll");
        if (loaded != null) Log("llama.dll loaded from: " + loaded);
        else Log("llama.dll not loaded yet");
    }

    static async Task<int> Main(string[] args)
    {
        InitFileLog();

        // 打开底层日志（能看到是否初始化 CUDA）
        Environment.SetEnvironmentVariable("GGML_LOG_LEVEL", "DEBUG"); // 或 TRACE
        Environment.SetEnvironmentVariable("LLAMA_LOG_LEVEL", "INFO");

        // 只把最终结果保留到 stdout，其他都走 stderr/file
        var realStdout = Console.Out;
        Console.SetOut(Console.Error);

        try
        {
            Log("[svc] hello, I am new build");
            Log($"base dir: {AppContext.BaseDirectory}");

            // 读取查询
            if (args.Length == 0)
            {
                Console.Error.Write("请输入查询：");
                args = new[] { Console.ReadLine() ?? "" };
            }
            string userInput = string.Join(" ", args);
            Log($"query: \"{userInput}\"");

            // 模型路径
            string modelPath = Path.Combine(AppContext.BaseDirectory, "granite-3.3-2b-instruct-Q4_K_M.gguf");
            Log($"model path: {modelPath}");
            if (!File.Exists(modelPath)) { Log("MODEL NOT FOUND"); return 1; }

            // Prompt（尽量短 & 不含“query:/keywords:”标签）
            string prompt =
              "Return a comma-separated list of 3 to 5 lowercase keywords only.\n" +
              $"Text: \"{userInput}\"\n" +
              "Keywords:";

            Log("prompt built");

            // 配置（可用环境变量覆盖）
            int ctxInt = Math.Clamp(ParseEnvInt("KEYWORD_CTX", 256), 8, 4096);
            int gpuLayer = ParseEnvInt("KEYWORD_GPU_LAYERS", 40); // -1: 尽量多层上 GPU；0: 全 CPU
            int maxTok = ParseEnvInt("KEYWORD_MAXTOK", 16);
            Log($"config: ctx={ctxInt}, gpu_layers={gpuLayer}, max_tokens={maxTok}");

            // ★★★ 关键：强制预加载 CUDA 版原生库
            PreloadCudaNativeLibs();

            // 加载 & 创建上下文（分段计时）
            var t0 = _sw.ElapsedMilliseconds;
            Log("loading weights...");
            var mp = new ModelParams(modelPath)
            {
                ContextSize = (uint)ctxInt,
                GpuLayerCount = gpuLayer,
                MainGpu = 0,
            };
            using var w = LLamaWeights.LoadFromFile(mp);
            Log($"weights loaded (+{_sw.ElapsedMilliseconds - t0} ms), creating context...");
            var t1 = _sw.ElapsedMilliseconds;
            using var cx = w.CreateContext(mp);
            Log($"context created (+{_sw.ElapsedMilliseconds - t1} ms)");
            var ex = new StatelessExecutor(w, mp);

            // 推理参数
            var ip = new InferenceParams
            {
                MaxTokens = maxTok,
                AntiPrompts = new[] { "\n", "query:", "Query:", "keywords:", "keyword:", "answer:", "output:" }
            };

            // 推理
            Log("inference begin");
            var t2 = _sw.ElapsedMilliseconds;
            var sb = new StringBuilder();
            await foreach (var tk in ex.InferAsync(prompt, ip, CancellationToken.None))
                sb.Append(tk);
            Log($"inference end (+{_sw.ElapsedMilliseconds - t2} ms)");

            string raw = sb.ToString();
            Log($"raw length = {raw.Length}");

            // 保险丝：截掉“第二轮”
            static string StripSecondRound(string txt)
            {
                if (string.IsNullOrWhiteSpace(txt)) return "";
                txt = txt.Replace('\n', ' ').Replace("\r", "");
                string[] stops = { "query:", "keywords:", "keyword:", "answer:", "output:" };
                int cut = txt.Length;
                foreach (var stop in stops)
                {
                    int idx = txt.IndexOf(stop, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0 && idx < cut) cut = idx;
                }
                return txt.Substring(0, cut).Trim();
            }
            raw = StripSecondRound(raw).Trim();

            // 清洗 + 黑名单
            string cleaned = KeywordUtils.CleanRawOutput(raw).Replace('_', ' ');
            var bad = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "query","keyword","keywords","answer","output","input","example" };

            var items = cleaned
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().TrimEnd(':'))
                .Where(s => s.Length > 1 && !bad.Contains(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 兜底
            if (items.Count < 3)
            {
                var fb = FallbackFromUserText(userInput);
                if (!string.IsNullOrWhiteSpace(fb))
                {
                    items = items.Concat(
                                fb.Split(',').Select(x => x.Trim())
                                  .Where(x => x.Length > 1 && !bad.Contains(x)))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .Take(5)
                             .ToList();
                }
            }

            cleaned = string.Join(", ", items);
            Log($"cleaned: \"{cleaned}\"");

            // 输出最终结果到 stdout（宿主读这个）
            Console.SetOut(realStdout);
            Console.WriteLine(cleaned.Trim());
            Console.SetOut(Console.Error);

            Log($"result written to stdout; exit ok (total {_sw.ElapsedMilliseconds} ms)");
            return 0;
        }
        catch (Exception ex)
        {
            Log("FATAL: " + ex);
            Console.SetOut(realStdout);
            Console.WriteLine(""); // 防调用端阻塞
            return 2;
        }
        finally
        {
            CloseFileLog();
        }
    }

    static int ParseEnvInt(string key, int def)
        => int.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : def;
}
