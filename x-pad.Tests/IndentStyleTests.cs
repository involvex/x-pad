using x_pad.Editor;

namespace x_pad.Tests;

public class IndentStyleTests
{
    [Fact]
    public void ResolveInsertSpaces_prefers_tabs_for_tab_indented_function()
    {
        var text =
            """
            function setjava17 {
            	$env:Path="C:\Users\lukas\.jdks\jbr-21.0.8\bin;" + $env:Path
            	$env:JAVA_HOME="C:\Users\lukas\.jdks\jbr-21.0.8"
            }
            """;

        Assert.False(IndentStyle.ResolveInsertSpaces(text, preferInsertSpaces: true));
    }

    [Fact]
    public void ResolveInsertSpaces_prefers_spaces_for_space_indented_file()
    {
        var text =
            """
            function demo {
                return 1;
            }
            """;

        Assert.True(IndentStyle.ResolveInsertSpaces(text, preferInsertSpaces: false));
    }

    [Fact]
    public void ResolveInsertSpaces_blank_file_keeps_preference()
    {
        Assert.True(IndentStyle.ResolveInsertSpaces("", preferInsertSpaces: true));
        Assert.False(IndentStyle.ResolveInsertSpaces("plain\nlines\n", preferInsertSpaces: false));
    }

    [Fact]
    public void ResolveInsertSpaces_tie_keeps_preference()
    {
        var text = "\tfoo\n    bar\n";
        Assert.True(IndentStyle.ResolveInsertSpaces(text, preferInsertSpaces: true));
        Assert.False(IndentStyle.ResolveInsertSpaces(text, preferInsertSpaces: false));
    }

    [Fact]
    public void ToDisplay_labels()
    {
        Assert.Equal("Spaces", IndentStyle.ToDisplay(true));
        Assert.Equal("Tabs", IndentStyle.ToDisplay(false));
    }
}
