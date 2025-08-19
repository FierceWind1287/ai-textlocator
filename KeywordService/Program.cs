using LLama;
using LLama.Common;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using KeywordAI;

internal class Program
{
    // ─────────────── Global stopwatch & file logging ───────────────
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

    // —— Extract 3–5 content words from user text if the model fails —— 
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

    // —— Get the actual path of a loaded DLL (e.g., llama.dll) —— 
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

    // —— Force preloading of CUDA native libraries —— 
    static void PreloadCudaNativeLibs()
    {
        string cudaDir = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "cuda12");
        string[] candidates =
        {
            Path.Combine(cudaDir, "ggml-cuda.dll"),
            Path.Combine(cudaDir, "ggml-base.dll"),
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

        // Print the actual path of llama.dll
        var loaded = GetLoadedPath("llama.dll");
        if (loaded != null) Log("llama.dll loaded from: " + loaded);
        else Log("llama.dll not loaded yet");
    }

    static async Task<int> Main(string[] args)
    {
        InitFileLog();

        // Enable lower-level logs (e.g., to see CUDA init)
        Environment.SetEnvironmentVariable("GGML_LOG_LEVEL", "DEBUG");
        Environment.SetEnvironmentVariable("LLAMA_LOG_LEVEL", "INFO");

        // Keep only the final results in stdout
        var realStdout = Console.Out;
        Console.SetOut(Console.Error);

        try
        {
            Log("[svc] hello, I am new build");
            Log($"base dir: {AppContext.BaseDirectory}");

            // Read query
            if (args.Length == 0)
            {
                Console.Error.Write("Enter query: ");
                args = new[] { Console.ReadLine() ?? "" };
            }
            string userInput = string.Join(" ", args);
            Log($"query: \"{userInput}\"");

            // Model path
            string modelPath = Path.Combine(AppContext.BaseDirectory, "granite-3.3-2b-instruct-Q4_K_M.gguf");
            Log($"model path: {modelPath}");
            if (!File.Exists(modelPath)) { Log("MODEL NOT FOUND"); return 1; }

            // Prompt
            string prompt =
              "Return a comma-separated list of 3 to 5 lowercase keywords only.\n" +
              $"Text: \"{userInput}\"\n" +
              "Keywords:";

            Log("prompt built");

            // Config
            int ctxInt = Math.Clamp(ParseEnvInt("KEYWORD_CTX", 256), 8, 4096);
            int gpuLayer = ParseEnvInt("KEYWORD_GPU_LAYERS", 40);
            int maxTok = ParseEnvInt("KEYWORD_MAXTOK", 16);
            Log($"config: ctx={ctxInt}, gpu_layers={gpuLayer}, max_tokens={maxTok}");

            // Preload CUDA libraries
            PreloadCudaNativeLibs();

            // Load weights and create context
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

            // Inference params
            var ip = new InferenceParams
            {
                MaxTokens = maxTok,
                AntiPrompts = new[] { "\n", "query:", "Query:", "keywords:", "keyword:", "answer:", "output:" }
            };

            // Run inference
            Log("inference begin");
            var t2 = _sw.ElapsedMilliseconds;
            var sb = new StringBuilder();
            await foreach (var tk in ex.InferAsync(prompt, ip, CancellationToken.None))
                sb.Append(tk);
            Log($"inference end (+{_sw.ElapsedMilliseconds - t2} ms)");

            string raw = sb.ToString();
            Log($"raw length = {raw.Length}");

            // Strip “second round” generations (hallucinated repeats)
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

            // Clean & filter blacklist
            string cleaned = KeywordUtils.CleanRawOutput(raw).Replace('_', ' ');
            var bad = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "query","keyword","keywords","answer","output","input","example" };

            var items = cleaned
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().TrimEnd(':'))
                .Where(s => s.Length > 1 && !bad.Contains(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Fallback if too few items
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

            // Output final result to stdout
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
            Console.WriteLine(""); // Prevent deadlocks on caller side
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
