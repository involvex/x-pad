using CommunityToolkit.Mvvm.ComponentModel;
using x_pad.Services;

namespace x_pad.ViewModels;

public partial class ShortcutBindingItem : ViewModelBase
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string DefaultGesture { get; init; }

    [ObservableProperty]
    private string gesture = "";

    public bool IsCustom =>
        !string.IsNullOrWhiteSpace(Gesture) &&
        !string.Equals(Gesture.Trim(), DefaultGesture, System.StringComparison.OrdinalIgnoreCase);

    partial void OnGestureChanged(string value) => OnPropertyChanged(nameof(IsCustom));

    public void Reset() => Gesture = DefaultGesture;

    public static ShortcutBindingItem FromDefinition(KeyBindingDefinition def, string resolved) =>
        new()
        {
            Id = def.Id,
            Title = def.Title,
            DefaultGesture = def.DefaultGesture,
            Gesture = resolved
        };
}
