using x_pad.Services.Ai;

namespace x_pad.Tests;

public class InlineCompletionPromptTests
{
    [Fact]
    public void ExtractContext_KeepsCursorBetweenPrefixAndSuffix()
    {
        // indices: 0..3 aaaa, 4..9 BBBBBB, 10..13 cccc — caret before index 6
        var text = "aaaaBBBBBBcccc";
        var (prefix, suffix) = InlineCompletionPrompt.ExtractContext(text, caretOffset: 6, prefixChars: 4, suffixChars: 4);
        Assert.Equal("aaBB", prefix);
        Assert.Equal("BBBB", suffix);
        Assert.Equal(text[2..10], prefix + suffix);
    }

    [Fact]
    public void ExtractContext_TruncatesLongBuffers()
    {
        var prefixPart = new string('a', 5000);
        var suffixPart = new string('z', 2000);
        var text = prefixPart + suffixPart;
        var caret = prefixPart.Length;
        var (prefix, suffix) = InlineCompletionPrompt.ExtractContext(text, caret);
        Assert.Equal(InlineCompletionPrompt.DefaultPrefixChars, prefix.Length);
        Assert.Equal(InlineCompletionPrompt.DefaultSuffixChars, suffix.Length);
        Assert.EndsWith("a", prefix);
        Assert.StartsWith("z", suffix);
        Assert.Equal(prefix + suffix, text.Substring(caret - prefix.Length, prefix.Length + suffix.Length));
    }

    [Fact]
    public void BuildUserMessage_IncludesCursorMarker()
    {
        var msg = InlineCompletionPrompt.BuildUserMessage("before", "after", "csharp");
        Assert.Contains("Language: csharp", msg);
        Assert.Contains("<CURSOR>", msg);
        Assert.Contains("before", msg);
        Assert.Contains("after", msg);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("hello();", "hello();")]
    [InlineData("```csharp\nfoo();\n```", "foo();")]
    [InlineData("```\nbar\n```", "bar")]
    public void CleanResponse_StripsFencesAndBlank(string? input, string expected)
    {
        Assert.Equal(expected, InlineCompletionPrompt.CleanResponse(input));
    }
}
