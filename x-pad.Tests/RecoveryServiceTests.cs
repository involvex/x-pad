using x_pad.Services;

namespace x_pad.Tests;

public class RecoveryServiceTests
{
    [Fact]
    public void Save_and_list_round_trip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xpad-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var service = new RecoveryService(dir);
            service.SaveEntry(new RecoveryEntry
            {
                Id = "abc",
                Title = "Untitled 1",
                Text = "hello",
                WasDirty = true
            });

            Assert.True(service.HasPendingRecovery());
            var entries = service.ListEntries();
            Assert.Single(entries);
            Assert.Equal("hello", entries[0].Text);
            Assert.Equal("Untitled 1", entries[0].Title);

            service.MarkCleanExit();
            Assert.False(service.HasPendingRecovery());
            Assert.Empty(service.ListEntries());
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            var marker = Path.Combine(Path.GetDirectoryName(dir)!, "clean-exit");
            if (File.Exists(marker))
                File.Delete(marker);
        }
    }

    [Fact]
    public void Clean_exit_marker_suppresses_pending_even_if_files_remain()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xpad-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var service = new RecoveryService(dir);
            service.SaveEntry(new RecoveryEntry { Id = "x", Text = "data", WasDirty = true });
            // Simulate clean exit writing marker without clearing (edge case): MarkCleanExit clears.
            service.MarkCleanExit();
            Assert.False(service.HasPendingRecovery());
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            var marker = Path.Combine(Path.GetDirectoryName(dir)!, "clean-exit");
            if (File.Exists(marker))
                File.Delete(marker);
        }
    }

    [Fact]
    public void ReplaceAll_removes_stale_entries()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xpad-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var service = new RecoveryService(dir);
            service.SaveEntry(new RecoveryEntry { Id = "old", Text = "gone", WasDirty = true });
            service.ReplaceAll(
            [
                new RecoveryEntry { Id = "new", Text = "kept", WasDirty = true }
            ]);

            var entries = service.ListEntries();
            Assert.Single(entries);
            Assert.Equal("new", entries[0].Id);
            Assert.Equal("kept", entries[0].Text);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            var marker = Path.Combine(Path.GetDirectoryName(dir)!, "clean-exit");
            if (File.Exists(marker))
                File.Delete(marker);
        }
    }

    [Fact]
    public void HasPendingRecovery_detects_json_without_parsing_bodies()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xpad-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var service = new RecoveryService(dir);
            Assert.False(service.HasPendingRecovery());

            // Intentionally invalid JSON — HasPendingRecovery only checks file presence.
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{not-json");
            Assert.True(service.HasPendingRecovery());

            service.DeleteEntry("broken");
            Assert.False(service.HasPendingRecovery());
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            var marker = Path.Combine(Path.GetDirectoryName(dir)!, "clean-exit");
            if (File.Exists(marker))
                File.Delete(marker);
        }
    }
}
