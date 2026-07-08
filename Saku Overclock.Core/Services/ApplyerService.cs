using Microsoft.Extensions.Logging;
using Saku_Overclock.Core.Contracts;
using Saku_Overclock.Shared;
using Saku_Overclock.Shared.Models;

namespace Saku_Overclock.Core.Services;

public partial class ApplyerService(
    IAppSettingsService settingsService,
    IPresetManagerService presetManager,
    ICpuService cpuService,
    ILogger<ApplyerService> logger)
    : IApplyerService
{
    public event Action<List<ApplyResult>>? OnSettingsApplied;
    private Timer? _timer; // Таймер для пере-применения настроек разгона
    private readonly Lock _timerLock = new(); // lock-объект для таймера

    private CancellationTokenSource?
        _applyDebounceCts; // Токен отмены для отмены применения пресета при помощи горячих клавиш

    private readonly Lock _applyDebounceLock = new(); // lock-объект для применения пресета при помощи горячих клавиш
    private int _pendingCustomPresetIndex = -1; // Индекс кастомного пресета для применения

    private string _selectedPreset = "Unknown"; // Применённый пресет

    public async Task ApplyPreset(Preset preset, bool saveInfo = false)
    {
        try
        {
            _selectedPreset = preset.PresetName;
            var results = await Task.Run(() => cpuService.ApplyPresetInternal(preset));
            OnSettingsApplied?.Invoke(results);
            ManageReapplyTimer(preset);
        }
        catch (Exception ex)
        {
            logger.LogError("ApplyPreset failed {ex}", ex);
        }
    }
    
    private void ManageReapplyTimer(Preset preset)
    {
        lock (_timerLock)
        {
            _timer?.Dispose();
            _timer = null;

            if (settingsService.ReapplyOverclock)
            {
                var intervalMs = (int)(settingsService.ReapplyOverclockTimer * 1000);
                if (intervalMs <= 0) intervalMs = 3000;

                _timer = new Timer(async void (_) =>
                {
                    try
                    {
                        if (settingsService.ReapplyOverclock)
                        {
                            // Переприменяем объект Preset напрямую, без строковых костылей
                            await Task.Run(() => cpuService.ApplyPresetInternal(preset));
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Overclock reapply loop failed");
                    }
                }, null, intervalMs, intervalMs);
            }
        }
    }
    
    public async Task RestoreAppliedSettings()
    {
        if (settingsService.ReapplyLatestSettingsOnAppLaunch)
        {
            if (settingsService.Preset != -1 && settingsService.Preset < presetManager.Presets.Length)
            {
                await ApplyPreset(presetManager.Presets[settingsService.Preset]);
            }
        }
    }

    public PresetId SwitchNextPreset()
    {
        var presetId = presetManager.GetNextPreset();
        if (!string.IsNullOrEmpty(presetId.PresetName))
        {
            lock (_applyDebounceLock)
            {
                _pendingCustomPresetIndex = presetId.PresetIndex;
            }
            ScheduleApplyPreset();
        }
        return presetId;
    }

    public string GetSelectedPresetName()
    {
        return _selectedPreset;
    }
    
    private void ScheduleApplyPreset()
    {
        lock (_applyDebounceLock)
        {
            _applyDebounceCts?.Cancel();
            _applyDebounceCts = new CancellationTokenSource();
            var token = _applyDebounceCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1500, token);
                    int customIndex;

                    lock (_applyDebounceLock)
                    {
                        customIndex = _pendingCustomPresetIndex;
                        presetManager.ResetPresetStateAfterApply();
                    }

                    if (customIndex >= 0 && customIndex < presetManager.Presets.Length)
                    {
                        settingsService.Preset = customIndex;
                        await ApplyPreset(presetManager.Presets[customIndex]);
                    }
                }
                catch (TaskCanceledException) { /* Игнорируем отмену */ }
            }, token);
        }
    }
    
    public void Dispose()
    {
        lock (_timerLock)
        {
            _timer?.Dispose();
            _timer = null;
        }

        GC.SuppressFinalize(this);
    }
}