using System.Text;
using x_pad.Models;
using x_pad.ViewModels;

namespace x_pad.Tests;

public class ClosedTabSnapshotTests
{
    [Fact]
    public void RestoreClosedBuffer_marks_dirty_title()
    {
        var tab = new DocumentViewModel();
        tab.RestoreClosedBuffer(
            path: null,
            title: "Scratch",
            text: "hello",
            dirty: true,
            lineEnding: LineEndingKind.Lf,
            encoding: new UTF8Encoding(false),
            hasBom: false,
            encodingName: "UTF-8");

        Assert.Null(tab.FilePath);
        Assert.Equal("hello", tab.Text);
        Assert.True(tab.IsDirty);
        Assert.Equal("Scratch*", tab.Title);
    }

    [Fact]
    public void RestoreClosedBuffer_clean_path_keeps_filename_title()
    {
        var path = Path.Combine(Path.GetTempPath(), "notes.txt");
        var tab = new DocumentViewModel();
        tab.RestoreClosedBuffer(
            path: path,
            title: "ignored",
            text: "body",
            dirty: false,
            lineEnding: LineEndingKind.Crlf,
            encoding: new UTF8Encoding(false),
            hasBom: false,
            encodingName: "UTF-8");

        Assert.Equal(path, tab.FilePath);
        Assert.Equal("notes.txt", tab.Title);
        Assert.False(tab.IsDirty);
        Assert.Equal(LineEndingKind.Crlf, tab.LineEndingKind);
    }
}
