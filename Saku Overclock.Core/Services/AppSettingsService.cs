using System.Text.Json;
using Saku_Overclock.Core.Contracts;
using Saku_Overclock.Shared;
using Saku_Overclock.Shared.Contracts;
using Saku_Overclock.Shared.Ipc;
using Saku_Overclock.Shared.Models;

namespace Saku_Overclock.Core.Services;

public class AppSettingsService(IFileService fileService, IpcHub hub) : IAppSettingsService
{
    private const string FolderPath = "Saku Overclock/Settings";
    private const string FileName = "AppSettings.json";
    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderPath);

    private AppSettings _settings = new();
    private readonly Lock _lock = new();

    public Task LoadSettingsAsync()
    {
        RegisterIpcHandlers();
        return Task.CompletedTask;
    }

    public bool FixedTitleBar
    {
        get => _settings.FixedTitleBar;
        set => _settings.FixedTitleBar = value;
    }

    public int AutostartType
    {
        get => _settings.AutostartType;
        set => _settings.AutostartType = value;
    }

    public bool HideToTray
    {
        get => _settings.HideToTray;
        set => _settings.HideToTray = value;
    }

    public bool CheckForUpdates
    {
        get => _settings.CheckForUpdates;
        set => _settings.CheckForUpdates = value;
    }

    public bool HotkeysEnabled
    {
        get => _settings.HotkeysEnabled;
        set => _settings.HotkeysEnabled = value;
    }

    public bool ReapplyLatestSettingsOnAppLaunch
    {
        get => _settings.ReapplyLatestSettingsOnAppLaunch;
        set => _settings.ReapplyLatestSettingsOnAppLaunch = value;
    }

    public bool ReapplyOverclock
    {
        get => _settings.ReapplyOverclock;
        set => _settings.ReapplyOverclock = value;
    }

    public double ReapplyOverclockTimer
    {
        get => _settings.ReapplyOverclockTimer;
        set => _settings.ReapplyOverclockTimer = value;
    }

    public int ThemeType
    {
        get => _settings.ThemeType;
        set => _settings.ThemeType = value;
    }

    public bool NiIconsEnabled
    {
        get => _settings.NiIconsEnabled;
        set => _settings.NiIconsEnabled = value;
    }

    public bool RtssMetricsEnabled
    {
        get => _settings.RtssMetricsEnabled;
        set => _settings.RtssMetricsEnabled = value;
    }

    public int NiIconsType
    {
        get => _settings.NiIconsType;
        set => _settings.NiIconsType = value;
    }

    public int Preset
    {
        get => _settings.Preset;
        set => _settings.Preset = value;
    }

    public bool PremadePresetsAdded
    {
        get => _settings.PremadePresetsAdded;
        set => _settings.PremadePresetsAdded = value;
    }

    public string AcPreset
    {
        get => _settings.AcPreset;
        set => _settings.AcPreset = value;
    }

    public string BatteryPreset
    {
        get => _settings.BatteryPreset;
        set => _settings.BatteryPreset = value;
    }

    public string RyzenAdjLine
    {
        get => _settings.RyzenAdjLine;
        set => _settings.RyzenAdjLine = value;
    }

    public bool AppFirstRun
    {
        get => _settings.AppFirstRun;
        set => _settings.AppFirstRun = value;
    }
    
    public void LoadSettings()
    {
        var loaded = fileService.Read<AppSettings>(_folder, FileName);
        if (loaded != null) _settings = loaded;
    }

    private AppSettings Snapshot()
    {
        lock (_lock) return _settings; // AppSettings — DTO, копию можно не делать, если её никто не мутирует напрямую
    }

    private async Task ApplyAndSaveAsync(AppSettings updated, CancellationToken ct)
    {
        lock (_lock) { _settings = updated; }
        fileService.Save(_folder, FileName, updated);

        var payload = JsonSerializer.Serialize(updated, IpcJsonContext.Default.AppSettings);
        await hub.BroadcastEventAsync("AppSettingsChanged", payload, ct);
    }

    public void RegisterIpcHandlers()
    {
        LoadSettings();
        hub.RegisterHandler("Get_AppSettings", (cmd, _) =>
        {
            var payload = JsonSerializer.Serialize(Snapshot(), IpcJsonContext.Default.AppSettings);
            return Task.FromResult(new IpcMessage { Kind = IpcMessageKind.Response, Id = cmd.Id, Payload = payload });
        });

        hub.RegisterHandler("Set_AppSettings", async (cmd, ct) =>
        {
            var incoming = JsonSerializer.Deserialize(cmd.Payload, IpcJsonContext.Default.AppSettings);
            if (incoming is null)
                return new IpcMessage { Kind = IpcMessageKind.Response, Id = cmd.Id, IsSuccess = false, Error = "Bad payload" };

            await ApplyAndSaveAsync(incoming, ct);
            return new IpcMessage { Kind = IpcMessageKind.Response, Id = cmd.Id, Payload = cmd.Payload };
        });
    }
}