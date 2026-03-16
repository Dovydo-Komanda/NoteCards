using NoteCards.Models;
using System.IO;
using System.Text.Json;

namespace NoteCards.Services;

public static class AppSettingsService
{
    private static string GetSettingsFilePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoteCards");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public static AppSettings Load()
    {
        try
        {
            var path = GetSettingsFilePath();
            if (!File.Exists(path))
                return new AppSettings();

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return settings ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, opts);
            File.WriteAllText(GetSettingsFilePath(), json);
        }
        catch
        {
            // Ignore persistence errors.
        }
    }
}
