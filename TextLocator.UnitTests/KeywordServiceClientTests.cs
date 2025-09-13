// TextLocator.UnitTests/KeywordServiceClientTests.cs
using System.Threading;
using System.Threading.Tasks;
using TextLocator.Service;
using Xunit;

public class KeywordServiceClientTests
{
    [Fact]
    public async Task NormalCsv_ReturnsArray()
    {
        var fake = new FakeRunner { Handler = (_, __) => ("revenue, quarterly, deck", "", 0) };
        var cli = new KeywordServiceClient(fake, "ignored.exe");
        var arr = await cli.ExtractAsync("any query", CancellationToken.None);

        Assert.Equal(3, arr.Length);
        Assert.Equal("revenue", arr[0]);
    }

    [Fact]
    public async Task EmptyStdout_UsesFallback()
    {
        var fake = new FakeRunner { Handler = (_, __) => ("", "err", 1) };
        var cli = new KeywordServiceClient(fake, "ignored.exe");
        var arr = await cli.ExtractAsync("hello world 2025", CancellationToken.None);

        Assert.True(arr.Length >= 2);
        Assert.Contains("hello", arr);
        Assert.Contains("world", arr);
    }
}
