using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

public class SidecarSmokeTests
{
    private static string FindExe()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent!)
        {
            var debug = Path.Combine(dir.FullName, "KeywordService", "bin", "Debug", "net8.0", "KeywordService.exe");
            var release = Path.Combine(dir.FullName, "KeywordService", "bin", "Release", "net8.0", "KeywordService.exe");
            if (File.Exists(debug)) return debug;
            if (File.Exists(release)) return release;
        }
        throw new FileNotFoundException("KeywordService.exe not found. Please build KeywordService first or modify FindExe() path.");
    }

    [Fact(Timeout = 60000)] // 60s hard timeout to prevent hang
    public async Task Sidecar_Returns_OneLine_3to5_Csv()
    {
        string exe = FindExe();

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = "\"find quarterly revenue deck for 2023\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Force CPU to avoid CUDA dependency causing slow initialization or failure
        psi.Environment["KEYWORD_GPU_LAYERS"] = "0";
        psi.Environment["KEYWORD_CTX"] = "512";
        psi.Environment["KEYWORD_MAXTOK"] = "24";
        // Optional: write logs to file for easier debugging
        // psi.Environment["KEYWORD_LOG_DIR"] = Path.GetTempPath();

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stderr = new List<string>();
        p.ErrorDataReceived += (_, e) => { if (e?.Data != null) lock (stderr) stderr.Add(e.Data); };

        if (!p.Start()) throw new Exception("Process failed to start");
        p.BeginErrorReadLine();

        // Concurrently read the "one line" from stdout
        var stdoutLineTask = p.StandardOutput.ReadLineAsync();

        // Set a 40s soft timeout (model load + inference should usually be a few seconds)
        using var cts = new CancellationTokenSource(40000);

        string? line;
        try
        {
            line = await stdoutLineTask.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(p);
            var err = string.Join(Environment.NewLine, stderr);
            throw new TimeoutException("Timeout waiting for one line from stdout. stderr:\n" + err);
        }

        // Wait for process to exit (allow another 15s)
        using var cts2 = new CancellationTokenSource(15000);
        try { await p.WaitForExitAsync(cts2.Token); }
        catch { TryKill(p); }

        var errAll = string.Join(Environment.NewLine, stderr);

        Assert.Equal(0, p.ExitCode); // Must exit successfully
        Assert.False(string.IsNullOrWhiteSpace(line), $"stdout is empty. stderr:\n{errAll}");

        // Assert: 3~5 segments, comma-separated (loose format: letters/numbers/spaces/hyphen)
        var ok = Regex.IsMatch(line!.Trim(),
            @"^[a-z0-9][a-z0-9 \-]*?(,\s*[a-z0-9][a-z0-9 \-]*){2,4}$");
        Assert.True(ok, $"Format invalid: {line}\nstderr:\n{errAll}");
    }

    private static void TryKill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
    }
}
