using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TextLocator.Service
{
    public sealed class ProcessRunner : IProcessRunner
    {
        public async Task<(string stdout, string stderr, int exitCode)> RunAsync(
            string file, string args, int timeoutMs, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args ?? "",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            return await RunAsync(psi, timeoutMs, ct).ConfigureAwait(false);
        }

        public async Task<(string stdout, string stderr, int exitCode)> RunAsync(
            ProcessStartInfo psi, int timeoutMs, CancellationToken ct)
        {
            if (psi == null) throw new ArgumentNullException(nameof(psi));

            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding ??= Encoding.UTF8;
            psi.StandardErrorEncoding ??= Encoding.UTF8;
            psi.CreateNoWindow = true;

            using (var p = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var tcsOut = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var tcsErr = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var sbOut = new StringBuilder();
                var sbErr = new StringBuilder();

                p.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) tcsOut.TrySetResult(sbOut.ToString());
                    else sbOut.AppendLine(e.Data);
                };
                p.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) tcsErr.TrySetResult(sbErr.ToString());
                    else sbErr.AppendLine(e.Data);
                };

                if (!p.Start())
                    throw new InvalidOperationException("Failed to start process.");

                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                using (var timeoutCts = new CancellationTokenSource(timeoutMs))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token))
                {
                    var waitExit = Task.Run(() => p.WaitForExit(), CancellationToken.None);
                    try
                    {
                        await Task.WhenAny(waitExit, Task.Delay(Timeout.Infinite, linked.Token))
                                  .ConfigureAwait(false);
                    }
                    catch (TaskCanceledException) { }

                    if (!p.HasExited)
                    {
                        try { p.Kill(); } catch { }
                    }
                }

                var readOut = tcsOut.Task;
                var readErr = tcsErr.Task;
                await Task.WhenAny(Task.WhenAll(readOut, readErr), Task.Delay(1000)).ConfigureAwait(false);

                var stdout = readOut.IsCompleted ? readOut.Result : sbOut.ToString();
                var stderr = readErr.IsCompleted ? readErr.Result : sbErr.ToString();
                int code = p.HasExited ? p.ExitCode : -1;

                return (stdout, stderr, code);
            }
        }
    }
}
