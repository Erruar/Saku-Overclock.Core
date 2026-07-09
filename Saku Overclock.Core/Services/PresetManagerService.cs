using System.Text.Json;
using Microsoft.Extensions.Logging;
using Saku_Overclock.Contracts.Services;
using Saku_Overclock.Core.Contracts;
using Saku_Overclock.Shared;
using Saku_Overclock.Shared.Ipc;
using Saku_Overclock.Shared.Models;

namespace Saku_Overclock.Core.Services;

public class PresetManagerService(IFileService fileService, 
    IAppSettingsService appSettings, 
    ILogger<PresetManagerService> logger, 
    IpcHub hub) : IPresetManagerService
{
    private const string FolderPath = "Saku Overclock/Presets";
    private const string FileName = "UserPresets.json";
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private readonly string _applicationDataFolder = Path.Combine(LocalAppData, FolderPath);
    private readonly Lock _lock = new();

    private int _virtualCustomPresetIndex = -1;
    private bool _isVirtualStateActive;

    public Preset[] Presets { get; set; } = [];

    public void LoadSettings()
    {
        try
        {
            lock (_lock) Presets = fileService.Read<Preset[]>(_applicationDataFolder, FileName) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load presets, resetting to empty");
            lock (_lock) Presets = [];
            SaveSettings();
        }
    }

    public void SaveSettings()
    {
        Preset[] snapshot;
        lock (_lock) snapshot = Presets;
        fileService.Save(_applicationDataFolder, FileName, snapshot);
    }

    public void AddPresetInternal(Preset preset)
    {
        lock (_lock)
        {
            var newPresets = new Preset[Presets.Length + 1];
            Array.Copy(Presets, newPresets, Presets.Length);
            newPresets[Presets.Length] = preset;
            Presets = newPresets;
        }
    }

    public void RemovePresetInternal(int index)
    {
        lock (_lock)
        {
            if (index < 0 || index >= Presets.Length) return;
            var newPresets = new Preset[Presets.Length - 1];
            Array.Copy(Presets, 0, newPresets, 0, index);
            Array.Copy(Presets, index + 1, newPresets, index, Presets.Length - index - 1);
            Presets = newPresets;
        }
    }

    public void RemovePresetsInternal(int[]? indices)
    {
        lock (_lock)
        {
            var sorted = indices?.OrderByDescending(i => i).ToArray();
            var temp = Presets;
            
            if (sorted == null) return;
            
            foreach (var index in sorted)
            {
                if (index < 0 || index >= temp.Length) continue;
                var newPresets = new Preset[temp.Length - 1];
                Array.Copy(temp, 0, newPresets, 0, index);
                Array.Copy(temp, index + 1, newPresets, index, temp.Length - index - 1);
                temp = newPresets;
            }
            Presets = temp;
        }
    }

    public bool UpdatePresetInternal(int index, Preset preset)
    {
        lock (_lock)
        {
            if (index < 0 || index >= Presets.Length) return false;
            Presets[index] = preset;
            return true;
        }
    }

    public PresetId GetNextPreset()
    {
        try
        {
            if (Presets.Length == 0)
            {
                logger.LogError("No custom presets available");
                return new PresetId
                {
                    PresetName = "Balance",
                    PresetDesc = string.Empty,
                    PresetIcon = "\uE783",
                    PresetIndex = -1
                };

            }

            int nextPresetIndex;
            // Определяем текущую позицию

            if (_isVirtualStateActive && _virtualCustomPresetIndex >= 0)
                nextPresetIndex = (_virtualCustomPresetIndex + 1) % Presets.Length;
            else
            {
                if (appSettings.Preset == -1)
                {
                    // Сейчас активен готовый пресет - начинаем с первого кастомного
                    nextPresetIndex = 0;
                    _virtualCustomPresetIndex = -1; // Чтобы следующий был 0
                }
                else
                {
                    // Уже выбран кастомный пресет
                    nextPresetIndex = (appSettings.Preset + 1) % Presets.Length;
                    _virtualCustomPresetIndex = appSettings.Preset;
                }
                _isVirtualStateActive = true;
            }

            // Обновляем виртуальную позицию
            _virtualCustomPresetIndex = nextPresetIndex;

            // Проверяем корректность индекса и данных пресета
            if (nextPresetIndex >= 0 && nextPresetIndex < Presets.Length &&
                !string.IsNullOrEmpty(Presets[nextPresetIndex].PresetName))
            {
                var preset = Presets[nextPresetIndex];
                var presetName = preset.PresetName; 
                var presetDesc = preset.PresetDesc;
                
                return new PresetId
                {
                    PresetName = presetName,
                    PresetDesc = presetDesc,
                    PresetIcon = preset.PresetIcon,
                    PresetIndex = nextPresetIndex
                };
            }
            logger.LogError("Invalid preset index: {nextPresetIndex}", nextPresetIndex);
        }
        catch (Exception ex)
        {
            logger.LogError("Error getting next custom preset: {ex.Message}",  ex.Message);
        }

        return new PresetId
        {
            PresetName = "Balance",
            PresetDesc = string.Empty,
            PresetIcon = "\uE783",
            PresetIndex = -1
        };
    }

    public void ResetPresetStateAfterApply()
    {
        _isVirtualStateActive = false;
        _virtualCustomPresetIndex = -1;
    }

    #region IPC Communication

    public void RegisterIpcHandlers()
    {
        LoadSettings();
        
        hub.RegisterHandler("Get_Presets", (cmd, _) =>
        {
            Preset[] snapshot;
            lock (_lock) snapshot = Presets;
            var payload = JsonSerializer.Serialize(snapshot, IpcJsonContext.Default.PresetArray);
            return Task.FromResult(new IpcMessage { Kind = IpcMessageKind.Response, Id = cmd.Id, Payload = payload });
        });

        hub.RegisterHandler("Set_Preset", async (cmd, ct) =>
        {
            var msg = JsonSerializer.Deserialize(cmd.Payload, IpcJsonContext.Default.PresetUpdateMessage);
            if (msg is null || !UpdatePresetInternal(msg.Index, msg.Preset))
                return Fail(cmd.Id, "Invalid preset index");

            SaveSettings();
            await hub.BroadcastEventAsync("PresetChanged", cmd.Payload, ct);
            return Ok(cmd.Id, cmd.Payload);
        });

        hub.RegisterHandler("Add_Preset", async (cmd, ct) =>
        {
            var preset = JsonSerializer.Deserialize(cmd.Payload, IpcJsonContext.Default.Preset);
            if (preset is null) return Fail(cmd.Id, "Bad payload");

            AddPresetInternal(preset);
            SaveSettings();
            return await BroadcastFullAndReturn(cmd.Id, ct);
        });

        hub.RegisterHandler("Remove_Preset", async (cmd, ct) =>
        {
            var index = JsonSerializer.Deserialize(cmd.Payload, IpcJsonContext.Default.Int32);
            RemovePresetInternal(index);
            SaveSettings();
            return await BroadcastFullAndReturn(cmd.Id, ct);
        });

        hub.RegisterHandler("Remove_Presets", async (cmd, ct) =>
        {
            var indices = JsonSerializer.Deserialize(cmd.Payload, IpcJsonContext.Default.Int32Array);
            if (indices is { Length: > 0 }) RemovePresetsInternal(indices);
            SaveSettings();
            return await BroadcastFullAndReturn(cmd.Id, ct);
        });

        hub.RegisterHandler("Import_Presets", async (cmd, ct) =>
        {
            var req = JsonSerializer.Deserialize(cmd.Payload, IpcJsonContext.Default.ImportPresetsRequest);
            if (req is null) return Fail(cmd.Id, "Bad payload");

            try
            {
                var imported = fileService.Read<Preset[]>(req.Folder, req.File);
                if (imported is null or []) return Fail(cmd.Id, "No valid presets found in file");

                lock (_lock)
                {
                    Presets = req.Append
                        ? [..Presets, ..imported]
                        : imported;
                }
                SaveSettings();
            }
            catch (Exception ex)
            {
                return Fail(cmd.Id, ex.Message);
            }

            return await BroadcastFullAndReturn(cmd.Id, ct);
        });

        hub.RegisterHandler("Export_Preset", (cmd, _) =>
        {
            var req = JsonSerializer.Deserialize(cmd.Payload, IpcJsonContext.Default.ExportPresetRequest);
            if (req is null) return Task.FromResult(Fail(cmd.Id, "Bad payload"));
            try
            {
                Preset preset;
                lock (_lock)
                {
                    if (req.Index < 0 || req.Index >= Presets.Length)
                        return Task.FromResult(Fail(cmd.Id, "Invalid index"));
                    preset = Presets[req.Index];
                }
                fileService.Save(req.Folder, req.File, preset);
                return Task.FromResult(Ok(cmd.Id, ""));
            }
            catch (Exception ex) { return Task.FromResult(Fail(cmd.Id, ex.Message)); }
        });

        hub.RegisterHandler("Export_Presets", (cmd, _) =>
        {
            var req = JsonSerializer.Deserialize(cmd.Payload, IpcJsonContext.Default.ExportPresetsRequest);
            if (req is null) return Task.FromResult(Fail(cmd.Id, "Bad payload"));
            try
            {
                Preset[] snapshot;
                lock (_lock) snapshot = Presets;
                var selected = req.Indices.Where(i => i >= 0 && i < snapshot.Length).Select(i => snapshot[i]).ToArray();
                if (selected.Length == 0) return Task.FromResult(Fail(cmd.Id, "No valid indices"));
                fileService.Save(req.Folder, req.File, selected);
                return Task.FromResult(Ok(cmd.Id, ""));
            }
            catch (Exception ex) { return Task.FromResult(Fail(cmd.Id, ex.Message)); }
        });

        hub.RegisterHandler("Export_All_Presets", (cmd, _) =>
        {
            var req = JsonSerializer.Deserialize(cmd.Payload, IpcJsonContext.Default.ExportAllPresetsRequest);
            if (req is null) return Task.FromResult(Fail(cmd.Id, "Bad payload"));
            try
            {
                Preset[] snapshot;
                lock (_lock) snapshot = Presets;
                fileService.Save(req.Folder, req.File, snapshot);
                return Task.FromResult(Ok(cmd.Id, ""));
            }
            catch (Exception ex) { return Task.FromResult(Fail(cmd.Id, ex.Message)); }
        });

        hub.RegisterHandler("Get_Next_Preset", (cmd, _) =>
        {
            var next = GetNextPreset();
            var payload = JsonSerializer.Serialize(next, IpcJsonContext.Default.PresetId);
            return Task.FromResult(Ok(cmd.Id, payload));
        });

        hub.RegisterHandler("Reset_Preset_State", (cmd, _) =>
        {
            ResetPresetStateAfterApply();
            return Task.FromResult(Ok(cmd.Id, ""));
        });
    }

    private async Task<IpcMessage> BroadcastFullAndReturn(string cmdId, CancellationToken ct)
    {
        Preset[] snapshot;
        lock (_lock) snapshot = Presets;
        var payload = JsonSerializer.Serialize(snapshot, IpcJsonContext.Default.PresetArray);
        await hub.BroadcastEventAsync("PresetsChanged", payload, ct);
        return Ok(cmdId, payload);
    }

    private static IpcMessage Ok(string id, string payload) =>
        new() { Kind = IpcMessageKind.Response, Id = id, Payload = payload };

    private static IpcMessage Fail(string id, string error) =>
        new() { Kind = IpcMessageKind.Response, Id = id, IsSuccess = false, Error = error };

    #endregion
}