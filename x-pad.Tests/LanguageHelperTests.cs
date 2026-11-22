using x_pad.Services;

namespace x_pad.Tests;

public class LanguageHelperTests
{
    [Fact]
    public void GetProbePath_UsesOverrideExtension()
    {
        var probe = LanguageHelper.GetProbePath(@"C:\notes\readme", ".cs");
        Assert.Equal("probe.cs", probe);
    }

    [Fact]
    public void GetProbePath_NormalizesExtensionWithoutDot()
    {
        var probe = LanguageHelper.GetProbePath(null, "py");
        Assert.Equal("probe.py", probe);
    }

    [Fact]
    public void GetProbePath_FallsBackToFilePathWhenNoOverride()
    {
        var path = @"D:\src\app.ts";
        Assert.Equal(path, LanguageHelper.GetProbePath(path, null));
        Assert.Equal(path, LanguageHelper.GetProbePath(path, "   "));
    }

    [Fact]
    public void CommonLanguageChoices_IncludesAutoAndCsharp()
    {
        Assert.Contains(LanguageHelper.CommonLanguageChoices, c => c.Display.StartsWith("Auto") && c.Extension == "");
        Assert.Contains(LanguageHelper.CommonLanguageChoices, c => c.Display == "C#" && c.Extension == ".cs");
    }

    [Fact]
    public void GetLineCommentPrefix_MatchesCommonLanguages()
    {
        Assert.Equal("//", LanguageHelper.GetLineCommentPrefix("a.cs"));
        Assert.Equal("#", LanguageHelper.GetLineCommentPrefix("a.py"));
        Assert.Equal("--", LanguageHelper.GetLineCommentPrefix("a.sql"));
    }

    [Fact]
    public void GetLineCommentPrefix_UsesLanguageOverride()
    {
        Assert.Equal("#", LanguageHelper.GetLineCommentPrefix("a.cs", ".py"));
        Assert.Null(LanguageHelper.GetLineCommentPrefix("a.cs", ".html"));
    }

    [Fact]
    public void GetBlockCommentDelimiters_ForHtmlAndXml()
    {
        var html = LanguageHelper.GetBlockCommentDelimiters("a.html");
        Assert.NotNull(html);
        Assert.Equal(("<!--", "-->"), html);

        var fromOverride = LanguageHelper.GetBlockCommentDelimiters("a.cs", ".xml");
        Assert.Equal(("<!--", "-->"), fromOverride);

        Assert.Null(LanguageHelper.GetBlockCommentDelimiters("a.cs"));
    }
}
