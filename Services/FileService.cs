using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace x_pad.Services;

public class FileService
{
    public async Task<string> ReadFileAsync(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    public async Task WriteFileAsync(string path, string content)
    {
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        await writer.WriteAsync(content);
    }
}
