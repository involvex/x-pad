using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace x_pad.Services;

public static class KeyBindingApplier
{
    public static void Apply(Window window, object viewModel, IReadOnlyDictionary<string, string>? overrides)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(viewModel);

        window.KeyBindings.Clear();

        var commandToGesture = new Dictionary<ICommand, KeyGesture>();
        foreach (var def in KeyBindingCatalog.All)
        {
            var gestureText = KeyBindingCatalog.ResolveGesture(def.Id, overrides);
            if (!KeyBindingCatalog.TryParseGesture(gestureText, out var gesture) || gesture == null)
                continue;

            var command = ResolveCommand(viewModel, def.CommandName);
            if (command == null)
                continue;

            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = gesture,
                Command = command
            });

            commandToGesture[command] = gesture;

            if (!string.IsNullOrWhiteSpace(def.AlternateGesture) &&
                string.Equals(gestureText, def.DefaultGesture, StringComparison.OrdinalIgnoreCase) &&
                KeyBindingCatalog.TryParseGesture(def.AlternateGesture, out var alt) &&
                alt != null)
            {
                window.KeyBindings.Add(new KeyBinding
                {
                    Gesture = alt,
                    Command = command
                });
            }
        }

        foreach (var menuItem in window.GetVisualDescendants().OfType<MenuItem>())
        {
            if (menuItem.Command != null && commandToGesture.TryGetValue(menuItem.Command, out var gesture))
                menuItem.InputGesture = gesture;
        }
    }

    private static ICommand? ResolveCommand(object viewModel, string commandName)
    {
        var prop = viewModel.GetType().GetProperty(commandName, BindingFlags.Instance | BindingFlags.Public);
        return prop?.GetValue(viewModel) as ICommand;
    }
}
