// TextLocator.UnitTests/FakeRunner.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using TextLocator.Service;

public sealed class FakeRunner : IProcessRunner
{
    public Func<string, string, (string Out, string Err, int Code)> Handler
        = (_, __) => ("alpha, beta, gamma", "", 0);

    public Task<(string stdout, string stderr, int exitCode)> RunAsync(
        string file, string args, int timeoutMs, CancellationToken ct)
        => Task.FromResult((Handler(file, args).Out, Handler(file, args).Err, Handler(file, args).Code));
}
