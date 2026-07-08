using Saku_Overclock.Core.Contracts;
using Saku_Overclock.Shared;
using Saku_Overclock.Shared.Models;

namespace Saku_Overclock.Core.Services;

public class RtssSettingsService(IFileService fileService, IpcHub hub) : IRtssSettingsService
{
    private const string FolderPath = "Saku Overclock/Settings";
    private const string FileName = "RtssSettings.json";
    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderPath);

    private RtssSettings _settings = new();
    private readonly Lock _lock = new();

    private void Load()
    {
        var loaded = fileService.Read<RtssSettings>(_folder, FileName);
        if (loaded != null) lock (_lock) _settings = loaded;
    }

    private RtssSettings Snapshot() { lock (_lock) return _settings; }

    private void ApplyAndSave(RtssSettings updated)
    {
        lock (_lock) _settings = updated;
        fileService.Save(_folder, FileName, updated);
    }

    public void RegisterIpcHandlers()
    {
        Load();
        SettingsIpcRegistrator.RegisterSimpleSettings(hub, "RtssSettings",
            Snapshot, ApplyAndSave, IpcJsonContext.Default.RtssSettings);
    }

    public bool IsRtssUpdated { get; set; }
    public void UpdateRtssMetrics(SensorsInformation sensorsInformation, string? appliedPreset, int? coreCount)
    {
        throw new NotImplementedException();
    }
}