using Saku_Overclock.Core.Contracts;
using Saku_Overclock.Shared;
using Saku_Overclock.Shared.Models;

namespace Saku_Overclock.Core.Services;

public class NotifyIconsService(IFileService fileService, IpcHub hub) : INotifyIconsService
{
    private const string FolderPath = "Saku Overclock/Settings";
    private const string FileName = "NotifyIcons.json";
    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderPath);

    private List<NiIconsElements> _elements = [];
    private readonly Lock _lock = new();

    private void Load()
    {
        var loaded = fileService.Read<List<NiIconsElements>>(_folder, FileName);
        if (loaded != null) lock (_lock) _elements = loaded;
    }

    private List<NiIconsElements> Snapshot() { lock (_lock) return _elements; }

    private void ApplyAndSave(List<NiIconsElements> updated)
    {
        lock (_lock) _elements = updated;
        fileService.Save(_folder, FileName, updated);
    }

    public void RegisterIpcHandlers()
    {
        Load();
        SettingsIpcRegistrator.RegisterSimpleSettings(hub, "NotifyIcons",
            Snapshot, ApplyAndSave, IpcJsonContext.Default.ListNiIconsElements);
    }

    public void CreateNotifyIcons()
    {
        throw new NotImplementedException();
    }

    public void UpdateNotifyIcons(SensorsInformation sensorsInformation)
    {
        throw new NotImplementedException();
    }

    public void DisposeAllNotifyIcons()
    {
        throw new NotImplementedException();
    }

    public bool IsIconsCreated { get; set; }
    public bool IsIconsUpdated { get; set; }
}