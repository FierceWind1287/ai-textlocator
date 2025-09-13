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

    // ────────── Dependency preloading (PATH injection + CUDA/CRT/ggml/llama) ──────────
    static void EnsureDllSearchPath()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (!path.Split(';', StringSplitOptions.RemoveEmptyEntries)
                 .Any(p => string.Equals(p.TrimEnd('\\'), baseDir, StringComparison.OrdinalIgnoreCase)))
        {
            Environment.SetEnvironmentVariable("PATH", baseDir + ";" + path);
            Log("[env] PATH prepended: " + baseDir);
        }
    }

    static bool TryLoadDll(string pathOrName)
    {
        try
        {
            if (File.Exists(pathOrName))
            {
                NativeLibrary.Load(pathOrName);
                Log("preloaded: " + pathOrName);
                return true;
            }
            if (NativeLibrary.TryLoad(pathOrName, out var _))
            {
                Log("preloaded(by name): " + pathOrName);
                return true;
            }
        }
        catch (Exception ex)
        {
            Log("WARN preload failed: " + pathOrName + " -> " + ex.Message);
        }
        return false;
    }

    /// <summary>
    /// Preload GPU-related DLLs (including CUDA runtime).
    /// If any of the three main CUDA runtime DLLs are missing, returns false.
    /// </summary>
    static bool TryPreloadCudaAndFriends()
    {
        string baseDir = AppContext.BaseDirectory;
        string cudaDir = Path.Combine(baseDir, "runtimes", "win-x64", "native", "cuda12");

        EnsureDllSearchPath();

        // nvcuda.dll from the graphics driver (provided by driver, not Toolkit)
        bool hasDriver = NativeLibrary.TryLoad("nvcuda", out var _);
        Log("nvcuda.dll: " + (hasDriver ? "FOUND" : "NOT FOUND"));
        if (!hasDriver) return false;

        // CUDA Toolkit 12.x runtime trio (should already be copied by .csproj into publish dir)
        string[] cudaRts = { "cublas64_12.dll", "cublasLt64_12.dll", "cudart64_12.dll" };
        bool[] okRt = new bool[cudaRts.Length];

        // ggml / llama native libs
        string[] ggmls = { "ggml-cuda.dll", "ggml-base.dll", "llama.dll" };
        bool[] okGgml = new bool[ggmls.Length];

        // Try loading from publish root, cuda12 subdir, or by name
        foreach (var name in cudaRts.Concat(ggmls))
        {
            string p1 = Path.Combine(baseDir, name);
            string p2 = Path.Combine(cudaDir, name);

            bool ok = TryLoadDll(p1) || TryLoadDll(p2) || TryLoadDll(name);

            int idxRt = Array.IndexOf(cudaRts, name);
            if (idxRt >= 0) okRt[idxRt] = ok;

            int idxG = Array.IndexOf(ggmls, name);
            if (idxG >= 0) okGgml[idxG] = ok;
        }

        Log("llama.dll    : " + (GetLoadedPath("llama.dll") ?? "NOT LOADED"));
        Log("ggml-cuda.dll: " + (GetLoadedPath("ggml-cuda.dll") ?? "NOT LOADED"));

        bool cudaOk = okRt.All(x => x);
        bool ggmlOk = okGgml.All(x => x);
        if (!cudaOk) Log("[env] CUDA runtime missing: need cublas64_12.dll, cublasLt64_12.dll, cudart64_12.dll");
        if (!ggmlOk) Log("[env] ggml/llama native missing");

        return cudaOk && ggmlOk;
    }
    // ────────── End of dependency preloading ──────────

    static async Task<int> Main(string[] args)
    {
        InitFileLog();
        Environment.SetEnvironmentVariable("GGML_LOG_LEVEL", "DEBUG");
        Environment.SetEnvironmentVariable("LLAMA_LOG_LEVEL", "INFO");

        // Only final result goes to stdout
        var realStdout = Console.Out;
        Console.SetOut(Console.Error);

        try
        {
            Log("[svc] hello, I am new build");
            Log($"base dir: {AppContext.BaseDirectory}");

            if (args.Length == 0)
            {
                Console.Error.Write("Enter query: ");
                args = new[] { Console.ReadLine() ?? "" };
            }
            string userInput = string.Join(" ", args);
            Log($"query: \"{userInput}\"");

            string modelPath = Path.Combine(AppContext.BaseDirectory, "Models", "granite-3.3-2b-instruct-Q4_K_M.gguf");
            if (!File.Exists(modelPath))
            {
                var alt = Path.Combine(AppContext.BaseDirectory, "granite-3.3-2b-instruct-Q4_K_M.gguf");
                if (File.Exists(alt)) modelPath = alt;
            }
            Log($"model path: {modelPath}");
            if (!File.Exists(modelPath)) { Log("MODEL NOT FOUND"); SafeWriteFinal(realStdout, ""); return 1; }

            // Configuration (default tries GPU; for GTX 1650/4GB recommend 4–6 layers)
            int ctxInt = Math.Clamp(ParseEnvInt("KEYWORD_CTX", 256), 8, 4096);
            int wantGpuLayers = ParseEnvInt("KEYWORD_GPU_LAYERS", 6);
            int maxTok = ParseEnvInt("KEYWORD_MAXTOK", 16);

            // Early fallback: if CUDA/dependencies missing, disable GPU to avoid 0x8007007E
            bool canUseGpu = wantGpuLayers > 0 && TryPreloadCudaAndFriends();
            int gpuLayers = canUseGpu ? wantGpuLayers : 0;
            Log($"config: ctx={ctxInt}, gpu_layers(want={wantGpuLayers}) -> using {gpuLayers}, max_tokens={maxTok}");

            // Build ModelParams (with fallback)
            ModelParams mp;
            try
            {
                mp = new ModelParams(modelPath)
                {
                    ContextSize = (uint)ctxInt,
                    GpuLayerCount = gpuLayers,
                    MainGpu = 0,
                };
            }
            catch (Exception ex) when (gpuLayers > 0)
            {
                Log("GPU init failed at ModelParams, fallback CPU: " + ex.Message);
                mp = new ModelParams(modelPath)
                {
                    ContextSize = (uint)ctxInt,
                    GpuLayerCount = 0,
                    MainGpu = 0,
                };
                gpuLayers = 0;
            }

            // Load weights (with fallback)
            LLamaWeights w;
            var t0 = _sw.ElapsedMilliseconds;
            try
            {
                Log("loading weights...");
                w = LLamaWeights.LoadFromFile(mp);
            }
            catch (Exception ex) when (gpuLayers > 0)
            {
                Log("GPU init failed at LoadFromFile, retry CPU: " + ex.Message);
                mp.GpuLayerCount = 0;
                gpuLayers = 0;
                w = LLamaWeights.LoadFromFile(mp);
            }
            Log($"weights loaded (+{_sw.ElapsedMilliseconds - t0} ms), creating context...");
            var t1 = _sw.ElapsedMilliseconds;
            using var cx = w.CreateContext(mp);
            Log($"context created (+{_sw.ElapsedMilliseconds - t1} ms)");

            var exr = new StatelessExecutor(w, mp);

            var ip = new InferenceParams
            {
                MaxTokens = maxTok,
                AntiPrompts = new[] { "\n", "query:", "Query:", "keywords:", "keyword:", "answer:", "output:" }
            };

            Log("inference begin");
            var t2 = _sw.ElapsedMilliseconds;
            var sb = new StringBuilder();
            await foreach (var tk in exr.InferAsync(
                "Return a comma-separated list of 3 to 5 lowercase keywords only.\n" +
                $"Text: \"{userInput}\"\nKeywords:", ip, CancellationToken.None))
                sb.Append(tk);
            Log($"inference end (+{_sw.ElapsedMilliseconds - t2} ms)");

            string raw = StripSecondRound(sb.ToString()).Trim();
            string cleaned = KeywordUtils.CleanRawOutput(raw).Replace('_', ' ');
            var bad = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "query","keyword","keywords","answer","output","input","example" };

            var items = cleaned
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().TrimEnd(':'))
                .Where(s => s.Length > 1 && !bad.Contains(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

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

            SafeWriteFinal(realStdout, cleaned.Trim());
            Log($"result written to stdout; exit ok (total {_sw.ElapsedMilliseconds} ms)");
            return 0;
        }
        catch (Exception ex)
        {
            Log("FATAL: " + ex);
            SafeWriteFinal(realStdout, "");
            return 2;
        }
        finally { CloseFileLog(); }
    }

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

    static void SafeWriteFinal(TextWriter realStdout, string line)
    {
        try { Console.SetOut(realStdout); Console.WriteLine(line ?? ""); Console.Out.Flush(); }
        catch { }
        finally { Console.SetOut(Console.Error); }
    }

    static int ParseEnvInt(string key, int def)
        => int.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : def;
}
