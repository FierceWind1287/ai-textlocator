using Xunit;
using TextLocator.Util;

public class KeywordCsvParserTests
{
    [Theory]
    [InlineData("revenue, quarterly, deck", 3)]
    [InlineData("a,b,c,d,e,f", 5)] // More than 5 will be truncated
    [InlineData("", 0)]
    public void ParseCsv_Works(string csv, int min)
    {
        var arr = KeywordCsvParser.ParseCsv(csv);
        Assert.True(arr.Length >= min);
        Assert.All(arr, s => Assert.Equal(s, s.ToLowerInvariant()));
    }
}
