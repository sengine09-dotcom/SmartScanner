using SmartScanner.Models;
using System.IO;
using System.Text.Json;

namespace SmartScanner.Services;

public class SettingsService : ISettingsService
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SmartScanner", "settings.json");

    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, _opts));
    }
}
