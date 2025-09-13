using Xunit;
using KeywordAI;  // Namespace from your code

public class CleanRawOutputTests
{
    [Fact]
    public void Lowercase_Dedup_Trim_Join()
    {
        // Mixed casing + duplicates + leading/trailing spaces
        var input = "Apple, banana  ,  Apple ,  kiwi";
        var outCsv = KeywordUtils.CleanRawOutput(input);
        Assert.Equal("apple, banana, kiwi", outCsv);
    }

    [Fact]
    public void Removes_Quotes_And_Newlines()
    {
        // Remove quotes and newlines, split by comma, deduplicate
        var input = "\"New\nYork\", data";
        var outCsv = KeywordUtils.CleanRawOutput(input);
        Assert.Equal("new york, data", outCsv);
    }

    [Fact]
    public void Take_First_Five()
    {
        var input = "a, b, c, d, e, f, g";
        var outCsv = KeywordUtils.CleanRawOutput(input);
        Assert.Equal("a, b, c, d, e", outCsv);
    }

    [Fact]
    public void Empty_When_Null_Or_Whitespace()
    {
        Assert.Equal("", KeywordUtils.CleanRawOutput(null));
        Assert.Equal("", KeywordUtils.CleanRawOutput("   "));
    }
}
