using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace TextLocator.Service
{
    public interface IProcessRunner
    {
        Task<(string stdout, string stderr, int exitCode)> RunAsync(
            string file, string args, int timeoutMs, CancellationToken ct);

        Task<(string stdout, string stderr, int exitCode)> RunAsync(
            ProcessStartInfo psi, int timeoutMs, CancellationToken ct);
    }
}
