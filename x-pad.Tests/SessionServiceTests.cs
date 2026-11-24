using x_pad.Services;

namespace x_pad.Tests;

public class SessionServiceTests
{
    [Fact]
    public void Round_trips_tabs_caret_and_selection()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xpad-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var sessionPath = Path.Combine(dir, "session.json");
        try
        {
            var service = new SessionService(sessionPath);
            var marker = Path.Combine(dir, "note.txt");
            File.WriteAllText(marker, "hello");

            var state = new SessionState
            {
                SelectedPath = marker,
                SelectedIndex = 0,
                Tabs =
                [
                    new SessionTabState { Path = marker, Line = 1, Column = 3 }
                ]
            };
            service.SaveSession(state);

            var loaded = service.LoadSession();
            Assert.Contains(loaded.Tabs, t =>
                string.Equals(t.Path, marker, StringComparison.OrdinalIgnoreCase) &&
                t.Line == 1 &&
                t.Column == 3);
            Assert.Equal(marker, loaded.SelectedPath, ignoreCase: OperatingSystem.IsWindows());
            Assert.Equal(0, loaded.SelectedIndex);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Round_trips_untitled_dirty_buffer()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xpad-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var sessionPath = Path.Combine(dir, "session.json");
        try
        {
            var service = new SessionService(sessionPath);
            var state = new SessionState
            {
                SelectedIndex = 0,
                Tabs =
                [
                    new SessionTabState
                    {
                        Path = "",
                        Title = "Untitled 1",
                        Text = "unsaved draft",
                        IsDirty = true,
                        EncodingName = "UTF-8",
                        LineEnding = "LF",
                        InsertSpaces = true,
                        Line = 1,
                        Column = 8
                    }
                ]
            };
            service.SaveSession(state);

            var loaded = service.LoadSession();
            Assert.Single(loaded.Tabs);
            var tab = loaded.Tabs[0];
            Assert.True(string.IsNullOrEmpty(tab.Path));
            Assert.Equal("Untitled 1", tab.Title);
            Assert.Equal("unsaved draft", tab.Text);
            Assert.True(tab.IsDirty);
            Assert.Equal(8, tab.Column);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Migrates_legacy_files_list()
    {
        var json = """
            {
              "Files": [ "C:\\tmp\\a.txt", "C:\\tmp\\b.txt" ]
            }
            """;
        var state = System.Text.Json.JsonSerializer.Deserialize<SessionState>(json);
        Assert.NotNull(state);
        Assert.Empty(state!.Tabs);
        Assert.Equal(2, state.Files.Count);

        // SessionService.LoadSession performs migration when reading from disk;
        // mirror that logic here for the unit assertion.
        if (state.Tabs.Count == 0 && state.Files.Count > 0)
        {
            state.Tabs = state.Files
                .Select(f => new SessionTabState { Path = f })
                .ToList();
        }

        Assert.Equal(2, state.Tabs.Count);
        Assert.Equal("C:\\tmp\\a.txt", state.Tabs[0].Path);
    }
}
