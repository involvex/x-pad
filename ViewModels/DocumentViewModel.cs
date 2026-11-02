using CommunityToolkit.Mvvm.ComponentModel;

namespace x_pad.ViewModels;

public partial class DocumentViewModel : ViewModelBase
{
    [ObservableProperty]
    private string title = "Untitled";

    [ObservableProperty]
    private string content = "";

    [ObservableProperty]
    private string? filePath;

    [ObservableProperty]
    private bool isDirty;

    partial void OnContentChanged(string value)
    {
        if (FilePath == null)
        {
            if (Title != "Untitled*" && !Title.EndsWith("*"))
            {
                Title = "Untitled*";
            }
        }
        else if (!IsDirty)
        {
            Title = System.IO.Path.GetFileName(FilePath) + "*";
        }
        IsDirty = true;
    }

    public void MarkClean(string filePath)
    {
        FilePath = filePath;
        IsDirty = false;
        Title = System.IO.Path.GetFileName(filePath);
    }
}
