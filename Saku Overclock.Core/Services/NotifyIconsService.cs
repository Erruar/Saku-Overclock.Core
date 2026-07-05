using Saku_Overclock.Core.Contracts;
using Saku_Overclock.Shared;
using Saku_Overclock.Shared.Models;

namespace Saku_Overclock.Core.Services;

public class NotifyIconsService(IFileService fileService)
{
    private const string FolderPath = "Saku Overclock/Settings";
    private const string FileName = "NotifyIcons.json";
    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderPath);

    private List<NiIconsElements> _elements = [];
    private readonly Lock _lock = new();

    public void Load()
    {
        var loaded = fileService.Read<List<NiIconsElements>>(_folder, FileName);
        if (loaded != null) lock (_lock) _elements = loaded;
    }

    public List<NiIconsElements> Snapshot() { lock (_lock) return _elements; }

    public void ApplyAndSave(List<NiIconsElements> updated)
    {
        lock (_lock) _elements = updated;
        fileService.Save(_folder, FileName, updated);
    }

    public void RegisterIpcHandlers(IpcHub hub) =>
        SettingsIpcRegistrator.RegisterSimpleSettings(hub, "NotifyIcons",
            Snapshot, ApplyAndSave, IpcJsonContext.Default.ListNiIconsElements);
}