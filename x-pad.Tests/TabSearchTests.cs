using x_pad.Services;

namespace x_pad.Tests;

public class TabSearchTests
{
    [Fact]
    public void Search_literal_match_case_insensitive_by_default()
    {
        var result = TabSearch.Search("Hello hello HELLO", "hello", new TabSearchOptions());
        Assert.Equal(3, result.Matches.Count);
        Assert.False(result.Truncated);
        Assert.Equal(1, result.Matches[0].Line);
        Assert.Equal(1, result.Matches[0].Column);
    }

    [Fact]
    public void Search_literal_match_case()
    {
        var result = TabSearch.Search("Hello hello HELLO", "hello", new TabSearchOptions { MatchCase = true });
        Assert.Single(result.Matches);
        Assert.Equal(6, result.Matches[0].Offset);
    }

    [Fact]
    public void Search_whole_word_skips_longer_identifier()
    {
        var result = TabSearch.Search(
            "alpha alphabet alpha",
            "alpha",
            new TabSearchOptions { WholeWord = true });

        Assert.Equal(2, result.Matches.Count);
        Assert.Equal(0, result.Matches[0].Offset);
        Assert.Equal(15, result.Matches[1].Offset);
    }

    [Fact]
    public void Search_regex_finds_groups()
    {
        var result = TabSearch.Search(
            "a1 b22 c3",
            @"\w+\d+",
            new TabSearchOptions { UseRegex = true });

        Assert.Equal(3, result.Matches.Count);
        Assert.Equal(0, result.Matches[0].Offset);
        Assert.Equal(2, result.Matches[0].Length);
        Assert.Equal(3, result.Matches[1].Offset);
        Assert.Equal(3, result.Matches[1].Length);
    }

    [Fact]
    public void Search_invalid_regex_returns_empty()
    {
        var result = TabSearch.Search("abc", "[", new TabSearchOptions { UseRegex = true });
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void Search_respects_max_hits_and_sets_truncated()
    {
        var result = TabSearch.Search(
            "a a a a a",
            "a",
            new TabSearchOptions { MaxHits = 3 });

        Assert.Equal(3, result.Matches.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Search_reports_line_and_column_across_newlines()
    {
        var result = TabSearch.Search("one\ntwo\nthree", "two", new TabSearchOptions());
        Assert.Single(result.Matches);
        Assert.Equal(2, result.Matches[0].Line);
        Assert.Equal(1, result.Matches[0].Column);
        Assert.Equal("two", result.Matches[0].Preview);
    }

    [Fact]
    public void ReplaceAll_applies_from_end_so_offsets_stay_valid()
    {
        var text = "foo bar foo";
        var matches = TabSearch.Search(text, "foo", new TabSearchOptions()).Matches;
        var (replaced, count) = TabSearch.ReplaceAll(text, matches, "baz", useRegex: false);
        Assert.Equal(2, count);
        Assert.Equal("baz bar baz", replaced);
    }

    [Fact]
    public void ReplaceOne_replaces_a_single_match()
    {
        var text = "alpha beta alpha";
        var match = TabSearch.Search(text, "alpha", new TabSearchOptions()).Matches[1];
        var replaced = TabSearch.ReplaceOne(text, match, "gamma", useRegex: false);
        Assert.Equal("alpha beta gamma", replaced);
    }

    [Fact]
    public void ReplaceAll_regex_supports_capture_groups()
    {
        var text = "a=1 b=2";
        var options = new TabSearchOptions { UseRegex = true };
        var matches = TabSearch.Search(text, @"(\w)=(\d)", options).Matches;
        var (replaced, count) = TabSearch.ReplaceAll(
            text,
            matches,
            "$2=$1",
            useRegex: true,
            pattern: @"(\w)=(\d)");
        Assert.Equal(2, count);
        Assert.Equal("1=a 2=b", replaced);
    }
}
