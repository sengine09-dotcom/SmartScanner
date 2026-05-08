using SmartScanner.Models;

namespace SmartScanner.Services;

public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
}
