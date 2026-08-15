using Microsoft.Win32;

namespace Saku_Overclock.Core.Helpers;

public sealed unsafe class NvidiaGpuMonitor
{
    private readonly NvApi.NvPhysicalGpuHandle _handle;
    private readonly NvApi.NvDisplayHandle _displayHandle;
    private readonly uint _clockVersion;
    private readonly bool _hasDisplayHandle;

    public struct RuntimeData
    {
        public float GpuLoad; // %
        public float GpuCoreClock; // MHz
        public float GpuTemperature; // °C
        public float MemoryClock; // MHz
    }

    public struct StaticData
    {
        public double TotalMemory; // GB
        public string MemoryType;
        public int MemoryBitWidth; // bits
        public string DriverVersion;
        public string GpuName;
    }

    public NvidiaGpuMonitor(int adapterIndex = 0)
    {
        if (!NvApi.IsAvailable)
        {
            NvApi.Initialize();
        }

        if (!NvApi.IsAvailable)
        {
            throw new Exception("NvApi not available");
        }

        // Используем stackalloc вместо аллокации массива
        var handles = stackalloc NvApi.NvPhysicalGpuHandle[NvApi.MaxPhysicalGpus];
        int count = 0;
        
        if (NvApi.NvApiEnumPhysicalGpUs == null || 
            NvApi.NvApiEnumPhysicalGpUs(handles, &count) != NvApi.NvStatus.Ok || count == 0)
        {
            throw new Exception("Failed to enumerate GPUs");
        }

        if (adapterIndex >= count)
        {
            throw new Exception($"GPU index {adapterIndex} not found (available: 0-{count - 1})");
        }

        _handle = handles[adapterIndex];

        NvApi.NvDisplayHandle tempHandle = default;
        _hasDisplayHandle = NvApi.NvApiEnumNvidiaDisplayHandle != null &&
                            NvApi.NvApiEnumNvidiaDisplayHandle(adapterIndex, &tempHandle) == NvApi.NvStatus.Ok;
        if (_hasDisplayHandle)
        {
            _displayHandle = tempHandle;
        }

        _clockVersion = 0;
        for (uint ver = 1; ver <= 3; ver++)
        {
            var clockFreq = new NvApi.NvGpuClockFrequencies
            {
                Version = NvApi.MAKE_NVAPI_VERSION<NvApi.NvGpuClockFrequencies>(ver)
            };

            // Передаем по указателю
            if (NvApi.NvApiGpuGetAllClockFrequencies != null &&
                NvApi.NvApiGpuGetAllClockFrequencies(_handle, &clockFreq) == NvApi.NvStatus.Ok)
            {
                _clockVersion = ver;
                break;
            }
        }

        if (_clockVersion == 0)
        {
            throw new Exception("Failed to detect clock frequency API version");
        }
    }

    /// <summary>
    ///     Получить данные реального времени (выделений в куче: 0 байт)
    /// </summary>
    public RuntimeData GetRuntimeData()
    {
        var data = new RuntimeData();

        // 1. Загрузка GPU
        var pStatesInfo = new NvApi.NvDynamicPStatesInfo
        {
            Version = NvApi.MAKE_NVAPI_VERSION<NvApi.NvDynamicPStatesInfo>(1)
        };

        if (NvApi.NvApiGpuGetDynamicPstatesInfoEx != null &&
            NvApi.NvApiGpuGetDynamicPstatesInfoEx(_handle, &pStatesInfo) == NvApi.NvStatus.Ok)
        {
            var util = pStatesInfo.GetUtilizations();
            if (util[0].IsPresent)
            {
                data.GpuLoad = util[0].Percentage;
            }
        }

        // 2. Частоты (GPU Core и Memory)
        var clockFreq = new NvApi.NvGpuClockFrequencies
        {
            Version = NvApi.MAKE_NVAPI_VERSION<NvApi.NvGpuClockFrequencies>(_clockVersion)
        };

        if (NvApi.NvApiGpuGetAllClockFrequencies != null &&
            NvApi.NvApiGpuGetAllClockFrequencies(_handle, &clockFreq) == NvApi.NvStatus.Ok)
        {
            var clocks = clockFreq.GetClocks();
            
            if (clocks[(int)NvApi.NvGpuPublicClockId.Graphics].IsPresent)
                data.GpuCoreClock = clocks[(int)NvApi.NvGpuPublicClockId.Graphics].Frequency / 1000f / 1000f;

            if (clocks[(int)NvApi.NvGpuPublicClockId.Memory].IsPresent)
                data.MemoryClock = clocks[(int)NvApi.NvGpuPublicClockId.Memory].Frequency / 1000f / 1000f;
        }

        // 3. Температура GPU
        var thermalSettings = new NvApi.NvThermalSettings
        {
            Version = NvApi.MAKE_NVAPI_VERSION<NvApi.NvThermalSettings>(2),
            Count = NvApi.MaxThermalSensorsPerGpu
        };

        if (NvApi.NvApiGpuGetThermalSettings != null &&
            NvApi.NvApiGpuGetThermalSettings(_handle, (int)NvApi.NvThermalTarget.All, &thermalSettings) == NvApi.NvStatus.Ok)
        {
            if (thermalSettings.Count > 0)
            {
                data.GpuTemperature = thermalSettings.Sensor0.CurrentTemp;
            }
        }

        return data;
    }

    public StaticData GetStaticData()
    {
        var data = new StaticData();

        if (NvApi.NvAPI_GPU_GetFullName(_handle, out var gpuName) == NvApi.NvStatus.Ok)
        {
            data.GpuName = gpuName.Trim();
            if (!data.GpuName.StartsWith("NVIDIA", StringComparison.OrdinalIgnoreCase))
                data.GpuName = "NVIDIA " + data.GpuName;
        }
        else
        {
            data.GpuName = "NVIDIA GPU";
        }

        if (_hasDisplayHandle)
        {
            var memoryInfo = new NvApi.NvMemoryInfo
            {
                Version = NvApi.MAKE_NVAPI_VERSION<NvApi.NvMemoryInfo>(2)
            };

            if (NvApi.NvApiGpuGetMemoryInfo != null &&
                NvApi.NvApiGpuGetMemoryInfo(_displayHandle, &memoryInfo) == NvApi.NvStatus.Ok)
            {
                data.TotalMemory = ClampValue((double)memoryInfo.DedicatedVideoMemory / 1024 / 1024);
            }

            var driverVersion = new NvApi.NvDisplayDriverVersion
            {
                Version = NvApi.MAKE_NVAPI_VERSION<NvApi.NvDisplayDriverVersion>(1)
            };

            if (NvApi.NvApiGetDisplayDriverVersion != null &&
                NvApi.NvApiGetDisplayDriverVersion(_displayHandle, &driverVersion) == NvApi.NvStatus.Ok)
            {
                var major = driverVersion.DriverVersion / 100;
                var minor = driverVersion.DriverVersion % 100;
                data.DriverVersion = $"{major}.{minor:00}";
            }
        }

        data.MemoryType = DetermineMemoryType(data.GpuName);
        data.MemoryBitWidth = EstimateMemoryBusWidth(data.GpuName);

        if (string.IsNullOrWhiteSpace(data.DriverVersion) || data.TotalMemory == 0)
        {
            var regInfo = GetRegistryGpuInformation(data.GpuName);
            
            if (string.IsNullOrWhiteSpace(data.DriverVersion) && !string.IsNullOrEmpty(regInfo.DriverVersion))
                data.DriverVersion = regInfo.DriverVersion;

            if (data.TotalMemory == 0 && regInfo.MemoryGb > 0)
                data.TotalMemory = ClampValue(regInfo.MemoryGb);
        }

        return data;
    }

    /// <summary>
    ///     Универсальный метод, собирающий память и драйвер за один проход по реестру
    /// </summary>
    private static (double MemoryGb, string DriverVersion) GetRegistryGpuInformation(string targetGpuName)
    {
        try
        {
            using var videoKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\ControlSet001\Control\Video");
            if (videoKey == null) return (0, string.Empty);

            foreach (var provider in videoKey.GetSubKeyNames())
            {
                using var providerKey = videoKey.OpenSubKey(provider);
                if (providerKey == null) continue;

                foreach (var gpu in providerKey.GetSubKeyNames())
                {
                    using var gpuKey = providerKey.OpenSubKey(gpu);
                    if (gpuKey == null) continue;

                    // Смотрим, совпадает ли имя видеокарты
                    var adapterStr = gpuKey.GetValue("HardwareInformation.AdapterString") as string;
                    var driverDesc = gpuKey.GetValue("DriverDesc") as string;

                    if (!string.Equals(adapterStr, targetGpuName, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(driverDesc, targetGpuName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Совпадение найдено. Читаем драйвер
                    bool isNvidia = targetGpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
                    var rawDriverVer = gpuKey.GetValue(isNvidia ? "DriverVersion" : "RadeonSoftwareVersion") as string;
                    
                    var driverVersion = isNvidia ? ParseNvidiaDriverVersion(rawDriverVer) : (rawDriverVer ?? string.Empty);

                    // Читаем объем памяти
                    double memoryGb = 0;
                    var memObj = gpuKey.GetValue("HardwareInformation.qwMemorySize") ?? 
                                 gpuKey.GetValue("HardwareInformation.MemorySize");

                    if (memObj is long memLong and > 0)
                        memoryGb = memLong / 1024.0 / 1024.0 / 1024.0;
                    else if (memObj is int memInt and > 0)
                        memoryGb = memInt / 1024.0 / 1024.0 / 1024.0;
                    else if (memObj is string memStr && long.TryParse(memStr, out var memStrLong))
                        memoryGb = memStrLong / 1024.0 / 1024.0 / 1024.0;

                    if (memoryGb > 0 || !string.IsNullOrEmpty(driverVersion))
                    {
                        return (memoryGb, driverVersion);
                    }
                }
            }
        }
        catch
        {
            // Игнорируем ошибки прав доступа
        }

        return (0, string.Empty);
    }
    
    private static string ParseNvidiaDriverVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return string.Empty;

        var parts = version.Split('.');
        if (parts.Length != 4) return string.Empty;

        if (!int.TryParse(parts[2], out var c) || !int.TryParse(parts[3], out var dddd))
            return string.Empty;

        var internalMajor = c * 100 + (dddd / 100);
        var major = internalMajor - 1000;
        var minor = dddd % 100;

        return $"{major}.{minor:D2}";
    }

    private static double ClampValue(double input)
    {
        var truncated = Math.Truncate(input);
        var fractionalPart = input - truncated;
        return fractionalPart >= 0.95 ? Math.Ceiling(input) : input;
    }

    private static string DetermineMemoryType(string gpuName)
    {
        var name = gpuName.ToLowerInvariant();
        if (name.Contains("rtx 50")) return "GDDR7";
        if (name.Contains("rtx 40") || name.Contains("3090") || name.Contains("3080")) return "GDDR6X";
        if (name.Contains("rtx 30") || name.Contains("rtx 20") || name.Contains("gtx 16")) return "GDDR6";
        if (name.Contains("1080")) return "GDDR5X";
        if (name.Contains("gtx 10")) return "GDDR5";
        return "Unknown";
    }

    private static int EstimateMemoryBusWidth(string gpuName)
    {
        var name = gpuName.ToLowerInvariant();
        if (name.Contains("90") || name.Contains("80 ti")) return 384;
        if (name.Contains("80") || name.Contains("70 ti") || name.Contains("70") || name.Contains("60 ti")) return 256;
        return 128;
    }
}