# AGENTS.md - x-pad

## Project Overview

x-pad is a simple cross-platform text editor built with Avalonia UI. It provides tabbed document editing with file open/save functionality, theme switching (Dark/Light/System), and session persistence.

## Useful Commands

### Build and Run
`ash
# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run

# Run in release mode
dotnet run -c Release

# Publish for distribution
dotnet publish -c Release -r win-x64 --self-contained
dotnet publish -c Release -r linux-x64 --self-contained
dotnet publish -c Release -r osx-x64 --self-contained
`

### Development
`ash
# Clean build artifacts
dotnet clean

# Watch for changes and rebuild (if using dotnet watch)
dotnet watch run
`

## Technologies

- **[.NET 10.0](https://dotnet.microsoft.com/)** - Target framework
- **[Avalonia UI 12.1.4](https://avaloniaui.net/)** - Cross-platform XAML-based UI framework
- **[Avalonia.Themes.Fluent](https://docs.avaloniaui.net/docs/styling/themes/fluent)** - Fluent design theme
- **[CommunityToolkit.Mvvm 8.4.2](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)** - MVVM framework with source generators
- **C# 13 / Nullable Reference Types** - Language features

## Architecture

The project follows MVVM (Model-View-ViewModel) pattern:

- **Views/** - XAML UI definitions (MainWindow.axaml)
- **ViewModels/** - UI logic and data binding (MainWindowViewModel, DocumentViewModel, ViewModelBase)
- **Services/** - Business logic (FileService for file I/O, SessionService for session persistence)
- **Models/** - Data models (currently empty folder)
- **Assets/** - Application resources (icons, etc.)
- **App.axaml/App.axaml.cs** - Application entry point and setup
- **Program.cs** - Main entry point with Avalonia configuration

## Features

- **Tabbed Editing**: Multiple documents in tabs with add/close functionality
- **File Operations**: New, Open (multi-select), Save, Save As
- **Theme Switching**: Dark mode, Light mode, and System theme support
- **Session Persistence**: Automatically saves and restores open files on app restart (stored in %APPDATA%/XPad/session.json)
- **Dirty State Tracking**: Visual indicator (*) for unsaved changes
- **Status Bar**: Shows status, encoding (UTF-8), line endings (LF), and line count

## Best Practices

### Code Style
- Use C# file-scoped namespaces (e.g., 
amespace x_pad.ViewModels;)
- Enable nullable reference types - handle nulls explicitly
- Use CommunityToolkit.Mvvm source generators ([ObservableProperty], [RelayCommand])
- Follow MVVM pattern - keep Views free of business logic
- Use async/await for I/O operations (file operations are async)

### Avalonia UI
- Use XAML data binding with x:DataType for compiled bindings
- Leverage Avalonia's styling system with DynamicResource for theme-aware UI
- Use DockPanel, Grid, TabControl for layout
- Theme switching via Application.Current.RequestedThemeVariant

### File Handling
- All files are read/written as UTF-8 (no BOM)
- Use StreamReader/StreamWriter for file operations
- Handle file paths properly with Path class
- Check for existing open files before opening duplicates

### Error Handling
- File operations should be wrapped in try-catch where appropriate
- Session save/load failures are silently ignored (MVP behavior)
- Validate null references before accessing UI elements

## Guidelines

### Adding Features
1. Follow MVVM pattern - add logic to ViewModels, UI to Views
2. Use dependency injection not required currently, but keep services in Services/ folder
3. Register new services as private fields in ViewModels as needed
4. Add commands using [RelayCommand] attribute
5. Update XAML bindings accordingly

### Modifying UI
1. Edit .axaml files for UI structure
2. Use design-time data contexts for better designer experience
3. Follow existing styling patterns (Fluent theme resources)
4. Test in both light and dark themes

### Working with Tabs/Documents
1. DocumentViewModel manages individual document state (title, content, file path, dirty state)
2. MainWindowViewModel manages the collection of tabs and coordinates file operations
3. Dirty state is tracked via OnContentChanged partial method
4. Session persistence only saves files with valid paths (not untitled documents)

### Development Notes
- Application uses single-instance style with classic desktop lifetime
- Status bar shows fixed values for encoding/line endings (UTF-8/LF) - not dynamically detected
- Close tab doesn't show confirmation for dirty files (TODO noted in code)
- Line count in status bar shows SelectedTab.Content (this appears to be a bug in binding - shows content string, not line count)
- Session is stored in user AppData folder
- The app uses Avalonia 12.1.4 which is a recent version
