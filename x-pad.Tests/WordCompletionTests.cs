using AvaloniaEdit.Document;
using x_pad.Editor;

namespace x_pad.Tests;

public class WordCompletionTests
{
    [Fact]
    public void Complete_replaces_full_identifier_prefix()
    {
        var doc = new TextDocument("al");
        var data = new WordCompletionData("alphabet");
        // Simulate a too-narrow segment (only empty at caret) which previously
        // produced "alalphabet" / "alalpha" style bugs.
        var narrow = new TextSegment { StartOffset = 2, Length = 0 };

        // Expand-left logic mirrored from WordCompletionData without TextArea.
        var end = Math.Max(narrow.EndOffset, 2);
        var start = narrow.StartOffset;
        while (start > 0)
        {
            var c = doc.GetCharAt(start - 1);
            if (!char.IsLetterOrDigit(c) && c != '_')
                break;
            start--;
        }
        doc.Replace(start, end - start, data.Text);

        Assert.Equal("alphabet", doc.Text);
    }

    [Fact]
    public void RankCandidates_filters_prefix_case_insensitively()
    {
        var words = new[] { "alpha", "alphabet", "Alpine", "beta", "alpha" };
        var ranked = WordCompletion.RankCandidates(words, "alp");
        Assert.Equal(new[] { "alpha", "Alpine", "alphabet" }, ranked);
    }

    [Fact]
    public void RankCandidates_requires_prefix_unless_forced()
    {
        var words = new[] { "one", "two" };
        Assert.Empty(WordCompletion.RankCandidates(words, ""));
        Assert.Equal(new[] { "one", "two" }, WordCompletion.RankCandidates(words, "", allowEmptyPrefix: true));
    }

    [Fact]
    public void CollectWords_extracts_identifiers()
    {
        var words = WordCompletion.CollectWords("var alpha = 1; // alphabet\nvoid Main()");
        Assert.Contains("alpha", words);
        Assert.Contains("Main", words);
        Assert.DoesNotContain("//", words);
    }
}
