using CaeStudio.Application;
using System.Text.Json;

namespace CaeStudio.Infrastructure;

/// <summary>
/// <see cref="ISettingsService"/> の JSON 実装。
/// %APPDATA%\CaeStudio\settings.json に保存する(spec 6.26.6)。
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _filePath;

    public JsonSettingsService(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CaeStudio", "settings.json");
        Current = Load();
    }

    public UserSettings Current { get; private set; }

    public void Update(Func<UserSettings, UserSettings> mutate)
    {
        Current = mutate(Current);
        Save();
    }

    private UserSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                return JsonSerializer.Deserialize<UserSettings>(
                    File.ReadAllText(_filePath), Options) ?? new UserSettings();
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            // 設定破損時は既定値で継続(致命的にしない)
        }

        return new UserSettings();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(Current, Options));
    }
}
