using x_pad.Editor;

namespace x_pad.Tests;

public class AutoIndentTests
{
    [Fact]
    public void ComputeNextLineIndent_bumps_after_open_brace_with_spaces()
    {
        var indent = AutoIndent.ComputeNextLineIndent("    if (true) {", tabSize: 4, insertSpaces: true);
        Assert.Equal("        ", indent);
    }

    [Fact]
    public void ComputeNextLineIndent_bumps_with_tab_when_insertSpaces_false()
    {
        var indent = AutoIndent.ComputeNextLineIndent("\tif (true) {", tabSize: 4, insertSpaces: false);
        Assert.Equal("\t\t", indent);
    }

    [Fact]
    public void ComputeNextLineIndent_copies_indent_without_bump()
    {
        var indent = AutoIndent.ComputeNextLineIndent("  return 1;", tabSize: 2, insertSpaces: true);
        Assert.Equal("  ", indent);
    }
}
