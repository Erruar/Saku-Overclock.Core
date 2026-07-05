using Saku_Overclock.Core.Contracts;
using Saku_Overclock.Shared;
using Saku_Overclock.Shared.Models;

namespace Saku_Overclock.Core.Services;

public class LocalThemeSettingsService(IFileService fileService, IpcHub hub) : ILocalThemeSettingsService
{
    private const string FolderPath = "Saku Overclock/Settings/Themes";
    private const string FileName = "ThemeSettings.json";
    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderPath);

    private LocalThemeSettingsOptions _settings = new()
    {
        AppBackgroundRequestedTheme = "Default",
        CustomThemes = DefaultThemesProvider.DefaultThemes
    };
    private readonly Lock _lock = new();

    private void Load()
    {
        var loaded = fileService.Read<LocalThemeSettingsOptions>(_folder, FileName);
        if (loaded != null) lock (_lock) _settings = loaded;
    }

    private LocalThemeSettingsOptions Snapshot() { lock (_lock) return _settings; }

    private void ApplyAndSave(LocalThemeSettingsOptions updated)
    {
        lock (_lock) _settings = updated;
        fileService.Save(_folder, FileName, updated);
    }

    public void RegisterIpcHandlers()
    {
        Load();
        SettingsIpcRegistrator.RegisterSimpleSettings(hub, "ThemeSettings",
            Snapshot, ApplyAndSave, IpcJsonContext.Default.LocalThemeSettingsOptions);
    }
}