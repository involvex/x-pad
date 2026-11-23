using System.Collections.Generic;
using x_pad.Services;
using Xunit;

namespace x_pad.Tests;

public class KeyBindingCatalogTests
{
    [Fact]
    public void ResolveGesture_falls_back_to_default_when_override_missing()
    {
        var gesture = KeyBindingCatalog.ResolveGesture("SaveFile", null);
        Assert.Equal("Ctrl+S", gesture);
    }

    [Fact]
    public void ResolveGesture_uses_override_when_present()
    {
        var overrides = new Dictionary<string, string> { ["SaveFile"] = "Ctrl+Alt+S" };
        var gesture = KeyBindingCatalog.ResolveGesture("SaveFile", overrides);
        Assert.Equal("Ctrl+Alt+S", gesture);
    }

    [Fact]
    public void ResolveGesture_blank_override_uses_default()
    {
        var overrides = new Dictionary<string, string> { ["SaveFile"] = "  " };
        var gesture = KeyBindingCatalog.ResolveGesture("SaveFile", overrides);
        Assert.Equal("Ctrl+S", gesture);
    }

    [Fact]
    public void TryParseGesture_accepts_catalog_defaults()
    {
        foreach (var def in KeyBindingCatalog.All)
        {
            Assert.True(
                KeyBindingCatalog.TryParseGesture(def.DefaultGesture, out var gesture),
                $"Failed to parse default for {def.Id}: {def.DefaultGesture}");
            Assert.NotNull(gesture);

            if (!string.IsNullOrWhiteSpace(def.AlternateGesture))
            {
                Assert.True(
                    KeyBindingCatalog.TryParseGesture(def.AlternateGesture, out var alt),
                    $"Failed to parse alternate for {def.Id}: {def.AlternateGesture}");
                Assert.NotNull(alt);
            }
        }
    }

    [Fact]
    public void ValidateOverrides_rejects_duplicate_gestures()
    {
        var overrides = new Dictionary<string, string>
        {
            ["SaveFile"] = "Ctrl+O",
        };

        var error = KeyBindingCatalog.ValidateOverrides(overrides);
        Assert.NotNull(error);
        Assert.Contains("Ctrl+O", error);
    }

    [Fact]
    public void ValidateOverrides_accepts_empty_map()
    {
        Assert.Null(KeyBindingCatalog.ValidateOverrides(null));
        Assert.Null(KeyBindingCatalog.ValidateOverrides(new Dictionary<string, string>()));
    }

    [Fact]
    public void ValidateOverrides_rejects_unknown_id()
    {
        var overrides = new Dictionary<string, string> { ["NotARealCommand"] = "Ctrl+K" };
        var error = KeyBindingCatalog.ValidateOverrides(overrides);
        Assert.NotNull(error);
        Assert.Contains("Unknown", error);
    }

    [Fact]
    public void ValidateOverrides_rejects_invalid_gesture()
    {
        var overrides = new Dictionary<string, string> { ["SaveFile"] = "NotAGesture!!!" };
        var error = KeyBindingCatalog.ValidateOverrides(overrides);
        Assert.NotNull(error);
        Assert.Contains("Invalid", error);
    }
}
