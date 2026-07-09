using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Saku_Overclock.Contracts.Services;
using Saku_Overclock.Core.Contracts;
using Saku_Overclock.Shared.Models;

namespace Saku_Overclock.Core.Services;

public class BackgroundDataUpdater(IDataProvider? dataProvider, 
    ICpuService cpuService,
    IApplyerService applyerService,
    IAppSettingsService appSettings,
    IRtssSettingsService rtssSettings,
    INotifyIconsService notifyIcons,
    IPresetManagerService presetManager,
    ILogger<BackgroundDataUpdater> logger) : IBackgroundDataUpdater
{
    private CancellationTokenSource? _cts;

    private bool _isRtssUpdated;

    private readonly List<string> _cachedStaticNvidiaGpuInfo =
    [
        "Unknown", "Unknown", "Unknown", "Unknown"
    ];
    
    private SensorsInformation _sensorsInformation = new();

    // TODO: Update NvidiaGpuMonitor
    //private NvidiaGpuMonitor? _nvidiaGpuMonitor;
    private bool _cachedNvidiaGpuUnavailable;

    public event EventHandler<SensorsInformation>? DataUpdated;

    private bool _cachedBatteryUnavailable;
    private int _currentUpdateErrorCycle;
    private const int MaxErrorsWhileUpdating = 5;
    private volatile string _lastAppliedPreset = string.Empty;
    
    // TODO: Update Battery Info
    //private GetSystemInfo.BatteryStatus _batteryStatus = GetSystemInfo.BatteryStatus.Undefined;
    private Timer? _debounceTimer;

    public void StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (appSettings.NiIconsEnabled && !notifyIcons.IsIconsCreated) notifyIcons.CreateNotifyIcons();
        }
        catch (Exception ex)
        {
            logger.LogError("Unable to create TrayMon icons {ex}", ex);
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _debounceTimer = new Timer(BatteryDebounceTimer_Tick, null, Timeout.Infinite, Timeout.Infinite);

        Task.Run(async () =>
        {
            // Инициализируем статические данные один раз перед входом в цикл
            try
            {
                // Получаем статические данные батареи
                var (batteryName, batteryHealth, batteryCycles, batteryCapacity, batteryUnavailable)
                    = await GetBatInfoStaticAsync();

                _sensorsInformation.BatteryName = batteryName;
                _sensorsInformation.BatteryHealth = batteryHealth;
                _sensorsInformation.BatteryCycles = batteryCycles;
                _sensorsInformation.BatteryCapacity = batteryCapacity;
                _sensorsInformation.BatteryUnavailable = batteryUnavailable;
            }
            catch (Exception ex)
            {
                _sensorsInformation.BatteryUnavailable = true;
                logger.LogError("Unknown battery info: {ex}", ex);
            }

            // Получаем статические данные RAM
            _sensorsInformation.RamTotal = GetRamTotal();

            // Получаем статические данные Nvidia GPU
            (_sensorsInformation.NvidiaDriverVersion, _sensorsInformation.NvidiaVramSize,
                    _sensorsInformation.NvidiaVramType, _sensorsInformation.NvidiaVramWidth)
                = GetNvidiaGpuStaticInfo();
            _sensorsInformation.IsNvidiaGpuAvailable = !_cachedNvidiaGpuUnavailable;

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    if (dataProvider == null)
                    {
                        logger.LogError("DataProvider is not defined");
                        await _cts.CancelAsync();
                        break;
                    }

                    dataProvider.GetData(ref _sensorsInformation);

                    // Обновляем только динамические данные
                    try
                    {
                        var (batteryPercent, batteryState, chargeRate, batteryLifeTime)
                            = await GetBatInfoDynamicAsync();

                        _sensorsInformation.BatteryPercent = batteryPercent;
                        _sensorsInformation.BatteryState = batteryState;
                        _sensorsInformation.BatteryChargeRate = chargeRate;
                        _sensorsInformation.BatteryLifeTime = batteryLifeTime;
                        
                        // TODO: Update Battery Info
                        /*if (_batteryStatus != (GetSystemInfo.BatteryStatus)batteryState)
                        {
                            _batteryStatus = (GetSystemInfo.BatteryStatus)batteryState;

                            // Перезапускаем таймер
                            _debounceTimer?.Change(350, Timeout.Infinite);
                        }*/
                    }
                    catch (Exception ex)
                    {
                        logger.LogError("Battery info update failed: {ex}", ex);
                    }

                    (_sensorsInformation.RamBusy, _sensorsInformation.RamUsagePercent, _sensorsInformation.RamUsage) =
                        GetRamInfoDynamic();

                    (_sensorsInformation.NvidiaGpuUsage, _sensorsInformation.NvidiaGpuTemperature,
                            _sensorsInformation.NvidiaGpuFrequency, _sensorsInformation.NvidiaVramFrequency)
                        = GetNvidiaGpuDynamicInfo();

                    DataUpdated?.Invoke(this, _sensorsInformation);

                    UpdateTrayMonAndRtss(_sensorsInformation);
                }
                catch (OperationCanceledException)
                {
                    // Это ожидаемое исключение при отмене задачи. Просто выходим из цикла.
                    break;
                }
                catch (Exception ex)
                {
                    _currentUpdateErrorCycle++;

                    if (_currentUpdateErrorCycle > MaxErrorsWhileUpdating)
                    {
                        await _cts.CancelAsync();
                        logger.LogError("Updater stopped due to errors count");
                        break;
                    }

                    logger.LogError("Data Update failed: {ex}", ex);
                }

                try
                {
                    await Task.Delay(300, _cts.Token);
                }
                catch (TaskCanceledException)
                {
                    break; // Выходим из цикла по запросу отмены
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, _cts.Token);
    }

    public void Stop()
    {
        if (_cts is { IsCancellationRequested: false })
        {
            _debounceTimer?.Dispose();
            _cts.Cancel();
            notifyIcons.DisposeAllNotifyIcons();
            // TODO: Update RtssHandler and OSD
            //RtssHandler.ResetOsdText();
        }
    }

    #region Update Battery information voids

    public bool IsBatteryUnavailable()
    {
        return _cachedBatteryUnavailable;
    }

    private async Task<(
        string BatteryName,
        string BatteryHealth,
        string BatteryCycles,
        string BatteryCapacity,
        bool BatteryUnavailable
        )> GetBatInfoStaticAsync()
    {
        // Если данные о батарее помечены как недоступные, сразу возвращаем флаг и пустые строки
        if (_cachedBatteryUnavailable) return (string.Empty, string.Empty, string.Empty, string.Empty, true);

        /*try
        {
            // TODO: Update Battery Info
            var batteryInfo = await Task.Run(() =>
            {
                // Получаем статические данные батареи
                var batteryHealth = $"{100 - GetSystemInfo.GetBatteryHealth() * 100:0.##}%";
                var batteryCycles = GetSystemInfo.GetBatteryCycle().ToString();

                var fullChargeCapacity = GetSystemInfo.ReadFullChargeCapacity();
                var designCapacity = GetSystemInfo.ReadDesignCapacity(out var notTrack);
                var batteryCapacity = $"{fullChargeCapacity}mWh/{designCapacity}mWh";

                var batteryName = GetSystemInfo.GetBatteryName() ?? "Unknown";

                // Кешируем
                _cachedBatteryUnavailable = notTrack;

                return (batteryName, batteryHealth, batteryCycles, batteryCapacity, notTrack);
            });

            return batteryInfo;
        }
        catch
        {*/
            // Батарея недоступна
            _cachedBatteryUnavailable = true;
            return (string.Empty, string.Empty, string.Empty, string.Empty, true);
        //}
    }

    private async Task<(
        int BatteryPercent,
        int BatteryState,
        double BatteryChargeRate,
        int BatteryLifeTime
        )> GetBatInfoDynamicAsync()
    {
        // Если данные о батарее помечены как недоступные, сразу возвращаем пустые значения
        if (_cachedBatteryUnavailable) return (0, 10, 0, 0);

        /*try
        {
            // TODO: Update Battery Info
            var batteryInfo = await Task.Run(() =>
            {
                // Получаем только часто меняющиеся параметры
                var batteryPercent = GetSystemInfo.GetBatteryPercent();
                var batteryState = (int)GetSystemInfo.GetBatteryStatus();
                var chargeRate = (double)(GetSystemInfo.GetBatteryRate() / 1000);
                var batteryLifeTime = GetSystemInfo.GetBatteryLifeTime();

                return ((int)batteryPercent, batteryState, chargeRate, batteryLifeTime);
            });

            return batteryInfo;
        }
        catch
        {*/
            // Батарея недоступна
            return (0, 10, 0, 0);
        //}
    }


    private void BatteryDebounceTimer_Tick(object? sender)
    {
        try
        {
            // 1. Определяем целевой ID пресета с помощью switch-выражения
            
            // TODO: Update Battery Info
            var targetPresetId = /*_batteryStatus switch
            {
                GetSystemInfo.BatteryStatus.Charging or
                    GetSystemInfo.BatteryStatus.ChargingAndHigh or
                    GetSystemInfo.BatteryStatus.ChargingAndLow or
                    GetSystemInfo.BatteryStatus.ChargingAndCritical or
                    GetSystemInfo.BatteryStatus.PartiallyCharged or
                    GetSystemInfo.BatteryStatus.AcConnected or
                    GetSystemInfo.BatteryStatus.FullyCharged => appSettings.AcPreset,

                GetSystemInfo.BatteryStatus.Undefined => null,

                _ => */appSettings.BatteryPreset // Все остальные статусы (разрядка)
            /*}*/;

            if (string.IsNullOrEmpty(targetPresetId) || targetPresetId == _lastAppliedPreset) return;

            var preset = presetManager.Presets.FirstOrDefault(p => p.PresetId.ToString() == targetPresetId);

            if (preset != null)
            {
                applyerService.ApplyPreset(preset);
                _lastAppliedPreset = targetPresetId;
            }
            else
            {
                _lastAppliedPreset = string.Empty;
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"[BatteryDebounceTimer_Tick] Ошибка применения пресета: {ex}");

            // Сбрасываем _lastAppliedPreset, чтобы при следующем тике попробовать снова
            _lastAppliedPreset = string.Empty;
        }
    }

    #endregion

    #region Update RAM information voids

    private static double GetRamTotal()
    {
        try
        {
            var memStatus = new MemoryStatusEx
            {
                dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>()
            };

            if (!GlobalMemoryStatusEx(ref memStatus)) return 0;

            // Преобразуем из байтов в гигабайты
            var totalRamGb = memStatus.ullTotalPhys / (1024.0 * 1024 * 1024);

            return totalRamGb;
        }
        catch
        {
            return 0;
        }
    }

    private static (
        double RamBusy,
        int RamUsagePercent,
        string RamUsage
        ) GetRamInfoDynamic()
    {
        try
        {
            var memStatus = new MemoryStatusEx
            {
                dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>()
            };

            if (!GlobalMemoryStatusEx(ref memStatus)) return (0, 0, "Error");

            // Преобразуем из байтов в гигабайты
            var totalRamGb = memStatus.ullTotalPhys / (1024.0 * 1024 * 1024);
            var availRamGb = memStatus.ullAvailPhys / (1024.0 * 1024 * 1024);
            var busyRamGb = totalRamGb - availRamGb;

            return (
                busyRamGb,
                (int)memStatus.dwMemoryLoad,
                $"{(int)memStatus.dwMemoryLoad}%\n{busyRamGb:F1}GB/{totalRamGb:F1}GB"
            );
        }
        catch
        {
            return (0, 0, "Error");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    #endregion

    #region Update Nvidia Gpu information voids

    private bool InitializeNvidiaGpu()
    {
        if (_cachedNvidiaGpuUnavailable) return false;

        try
        {
            // TODO: Update NvidiaGpuMonitor
            //_nvidiaGpuMonitor ??= new NvidiaGpuMonitor();
        }
        catch
        {
            _cachedNvidiaGpuUnavailable = true;
            return false;
        }

        return true;
    }

    private (
        string DriverVersion,
        string VramSize,
        string VramType,
        string VramWidth
        ) GetNvidiaGpuStaticInfo()
    {
        try
        {
            // TODO: Update NvidiaGpuMonitor
            /*if (InitializeNvidiaGpu() && _nvidiaGpuMonitor != null)
            {
                if (_cachedStaticNvidiaGpuInfo.Count > 0 && _cachedStaticNvidiaGpuInfo[0] == "Unknown")
                {
                    // Получить статические данные один раз
                    var staticData = _nvidiaGpuMonitor.GetStaticData();
                    _cachedStaticNvidiaGpuInfo[0] = staticData.DriverVersion;
                    _cachedStaticNvidiaGpuInfo[1] = $"{staticData.TotalMemory:0.###}GB";
                    _cachedStaticNvidiaGpuInfo[2] = staticData.MemoryType;
                    _cachedStaticNvidiaGpuInfo[3] = $"{staticData.MemoryBitWidth} bit";
                }


                return (
                    _cachedStaticNvidiaGpuInfo[0],
                    _cachedStaticNvidiaGpuInfo[1],
                    _cachedStaticNvidiaGpuInfo[2],
                    _cachedStaticNvidiaGpuInfo[3]
                );
            }*/

            return ("Error", "Error", "Error", "Error");
        }
        catch
        {
            return ("Error", "Error", "Error", "Error");
        }
    }

    private (
        double GpuUsageValue,
        double GpuTemperature,
        double GpuFrequency,
        double VramFrequency
        ) GetNvidiaGpuDynamicInfo()
    {
        try
        {
            // TODO: Update NvidiaGpuMonitor
            /*if (InitializeNvidiaGpu() && _nvidiaGpuMonitor != null)
            {
                var runtime = _nvidiaGpuMonitor.GetRuntimeData();

                return (
                    runtime.GpuLoad,
                    runtime.GpuTemperature,
                    runtime.GpuCoreClock,
                    runtime.MemoryClock
                );
            }*/

            return (0, 0, 0, 0);
        }
        catch
        {
            return (0, 0, 0, 0);
        }
    }

    #endregion

    #region Update Ni-Icons & RTSS information voids

    private void UpdateTrayMonAndRtss(SensorsInformation? sensorsInformation)
    {
        // Валидация входных данных
        if (sensorsInformation == null)
        {
            logger.LogWarning("UpdateTrayMonAndRtss: SensorsInformation is null");
            return;
        }

        try
        {
            // RTSS обновление
            if (appSettings.RtssMetricsEnabled)
                rtssSettings.UpdateRtssMetrics(sensorsInformation, applyerService.GetSelectedPresetName(),
                    (int?)cpuService.Cores);
            // Сброс RTSS если был включен, но теперь выключен
            else if (_isRtssUpdated)
                try
                {
                    // TODO: Update RtssHandler and OSD
                    //RtssHandler.ResetOsdText();
                    _isRtssUpdated = false;
                }
                catch (Exception rtssResetEx)
                {
                    logger.LogWarning("Failed to reset RTSS: {rtssResetEx.Message}", rtssResetEx.Message);
                }

            // Notify Icons обновление
            if (appSettings.NiIconsEnabled)
                notifyIcons.UpdateNotifyIcons(sensorsInformation);
            // Очистка иконок если были включены, но теперь выключены
            else if (notifyIcons.IsIconsUpdated)
                try
                {
                    notifyIcons.DisposeAllNotifyIcons();
                    notifyIcons.IsIconsCreated = false;
                    notifyIcons.IsIconsUpdated = false;
                }
                catch (Exception iconsDisposeEx)
                {
                    logger.LogWarning("Failed to dispose notify icons: {iconsDisposeEx.Message}", iconsDisposeEx.Message);
                }
        }
        catch (Exception ex)
        {
            logger.LogError("UpdateTrayMonAndRtss failed: {ex}", ex);

            // Попытаться очистить ресурсы при критической ошибке
            TryCleanupResources();
        }
    }


    private void TryCleanupResources()
    {
        try
        {
            if (_isRtssUpdated)
            {
                // TODO: Update RtssHandler and OSD
                //RtssHandler.ResetOsdText();
                _isRtssUpdated = false;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to cleanup Rtss resources: {ex.Message}", ex.Message);
        }

        try
        {
            if (notifyIcons.IsIconsUpdated)
            {
                notifyIcons.DisposeAllNotifyIcons();
                notifyIcons.IsIconsCreated = false;
                notifyIcons.IsIconsUpdated = false;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to update TrayMon icons: {ex.Message}", ex.Message);
        }
    }

    #endregion
}