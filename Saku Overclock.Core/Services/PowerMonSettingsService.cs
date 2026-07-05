using Saku_Overclock.Core.Contracts;
using Saku_Overclock.Shared;

namespace Saku_Overclock.Core.Services;

public class PowerMonSettingsService(IFileService fileService)
{
    private const string FolderPath = "Saku Overclock/Settings";
    private const string FileName = "PowerMon.json";
    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderPath);

    private List<string> _notelist = [];
    private readonly Lock _lock = new();

    public void Load()
    {
        var loaded = fileService.Read<List<string>>(_folder, FileName);
        if (loaded != null) lock (_lock) _notelist = loaded;
    }

    private List<string> Snapshot() { lock (_lock) return _notelist; }

    private void ApplyAndSave(List<string> updated)
    {
        lock (_lock) _notelist = updated;
        fileService.Save(_folder, FileName, updated);
    }

    public void RegisterIpcHandlers(IpcHub hub) =>
        SettingsIpcRegistrator.RegisterSimpleSettings(hub, "PowerMon",
            Snapshot, ApplyAndSave, IpcJsonContext.Default.ListString);
}