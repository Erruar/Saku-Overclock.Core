using Microsoft.Win32;

namespace Saku_Overclock.Core.Helpers;

public sealed class NvidiaGpuMonitor
{
    private readonly NvApi.NvPhysicalGpuHandle _handle;
    private readonly NvApi.NvDisplayHandle _displayHandle;
    private readonly int _clockVersion;
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
        // Инициализация NvApi
        if (!NvApi.IsAvailable)
        {
            NvApi.Initialize();
        }

        if (!NvApi.IsAvailable)
        {
            throw new Exception("NvApi not available");
        }

        // Получаем GPU handles
        var handles = new NvApi.NvPhysicalGpuHandle[NvApi.MaxPhysicalGpus];
        var count = 0;
        if ((NvApi.NvApiEnumPhysicalGpUs != null &&
             NvApi.NvApiEnumPhysicalGpUs(handles, out count) != NvApi.NvStatus.Ok) || count == 0)
        {
            throw new Exception("Failed to enumerate GPUs");
        }

        if (adapterIndex >= count)
        {
            throw new Exception($"GPU index {adapterIndex} not found (available: 0-{count - 1})");
        }

        _handle = handles[adapterIndex];

        // Получаем display handle
        NvApi.NvDisplayHandle tempHandle = default;
        _hasDisplayHandle = NvApi.NvApiEnumNvidiaDisplayHandle != null &&
                            NvApi.NvApiEnumNvidiaDisplayHandle(adapterIndex, ref tempHandle) == NvApi.NvStatus.Ok;
        if (_hasDisplayHandle)
        {
            _displayHandle = tempHandle;
        }

        // Определяем версию Clock API (пробуем версии 1, 2, 3)
        _clockVersion = 0;
        for (var ver = 1; ver <= 3; ver++)
        {
            var clockFreq = new NvApi.NvGpuClockFrequencies
            {
                Version = (uint)NvApi.MAKE_NVAPI_VERSION<NvApi.NvGpuClockFrequencies>(ver)
            };

            if (NvApi.NvApiGpuGetAllClockFrequencies != null &&
                NvApi.NvApiGpuGetAllClockFrequencies(_handle, ref clockFreq) == NvApi.NvStatus.Ok)
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
    ///     Получить данные реального времени (оптимизировано для частого вызова)
    /// </summary>
    public RuntimeData GetRuntimeData()
    {
        var data = new RuntimeData();

        // 1. Загрузка GPU
        var pStatesInfo = new NvApi.NvDynamicPStatesInfo
        {
            Version = (uint)NvApi.MAKE_NVAPI_VERSION<NvApi.NvDynamicPStatesInfo>(1),
            Utilizations = new NvApi.NvDynamicPState[NvApi.MaxGpuUtilization]
        };

        if (NvApi.NvApiGpuGetDynamicPstatesInfoEx != null &&
            NvApi.NvApiGpuGetDynamicPstatesInfoEx(_handle, ref pStatesInfo) == NvApi.NvStatus.Ok)
        {
            // Index 0 = GPU Core utilization
            if (pStatesInfo.Utilizations[0].IsPresent)
            {
                data.GpuLoad = pStatesInfo.Utilizations[0].Percentage;
            }
        }

        // 2. Частоты (GPU Core и Memory)
        var clockFreq = new NvApi.NvGpuClockFrequencies
        {
            Version = (uint)NvApi.MAKE_NVAPI_VERSION<NvApi.NvGpuClockFrequencies>(_clockVersion),
            Clocks = new NvApi.NvGpuClockFrequenciesDomain[NvApi.MaxGpuPublicClocks]
        };

        if (NvApi.NvApiGpuGetAllClockFrequencies != null &&
            NvApi.NvApiGpuGetAllClockFrequencies(_handle, ref clockFreq) == NvApi.NvStatus.Ok)
        {
            // Index 0 = Graphics (GPU Core)
            if (clockFreq.Clocks[(int)NvApi.NvGpuPublicClockId.Graphics].IsPresent)
            {
                data.GpuCoreClock = clockFreq.Clocks[(int)NvApi.NvGpuPublicClockId.Graphics].Frequency / 1000f / 1000f;
            }

            // Index 4 = Memory
            if (clockFreq.Clocks[(int)NvApi.NvGpuPublicClockId.Memory].IsPresent)
            {
                data.MemoryClock = clockFreq.Clocks[(int)NvApi.NvGpuPublicClockId.Memory].Frequency / 1000f / 1000f;
            }
        }

        // 3. Температура GPU
        var thermalSettings = new NvApi.NvThermalSettings
        {
            Version = (uint)NvApi.MAKE_NVAPI_VERSION<NvApi.NvThermalSettings>(2),
            Count = NvApi.MaxThermalSensorsPerGpu,
            Sensor = new NvApi.NvSensor[NvApi.MaxThermalSensorsPerGpu]
        };

        if (NvApi.NvApiGpuGetThermalSettings != null &&
            NvApi.NvApiGpuGetThermalSettings(_handle, (int)NvApi.NvThermalTarget.All, ref thermalSettings) ==
            NvApi.NvStatus.Ok)
        {
            if (thermalSettings.Count > 0)
            {
                data.GpuTemperature = thermalSettings.Sensor[0].CurrentTemp;
            }
        }

        return data;
    }

    /// <summary>
    ///     Получить статические данные (вызывать один раз)
    /// </summary>
    public StaticData GetStaticData()
    {
        var data = new StaticData();

        // 1. Имя GPU
        if (NvApi.NvAPI_GPU_GetFullName(_handle, out var gpuName) == NvApi.NvStatus.Ok)
        {
            data.GpuName = gpuName.Trim();
            if (!data.GpuName.StartsWith("NVIDIA", StringComparison.OrdinalIgnoreCase))
            {
                data.GpuName = "NVIDIA " + data.GpuName;
            }
        }
        else
        {
            data.GpuName = "NVIDIA GPU";
        }

        // 2. Объём памяти
        if (_hasDisplayHandle)
        {
            var memoryInfo = new NvApi.NvMemoryInfo
            {
                Version = (uint)NvApi.MAKE_NVAPI_VERSION<NvApi.NvMemoryInfo>(2)
            };

            if (NvApi.NvApiGpuGetMemoryInfo != null &&
                NvApi.NvApiGpuGetMemoryInfo(_displayHandle, ref memoryInfo) == NvApi.NvStatus.Ok)
            {
                var totalMemory = (double)memoryInfo.DedicatedVideoMemory / 1024 / 1024; // из KB в GB, есть погрешность

                if (totalMemory == 0)
                {
                    totalMemory = GetGpuVramSize(data.GpuName);
                }

                data.TotalMemory = ClampValue(totalMemory);
            }
        }

        // 3. Тип памяти (определяем по имени GPU)
        data.MemoryType = DetermineMemoryType(data.GpuName);

        // 4. Битность шины памяти (примерная оценка)
        data.MemoryBitWidth = EstimateMemoryBusWidth(data.GpuName);

        // 5. Версия драйвера
        if (_hasDisplayHandle)
        {
            var driverVersion = new NvApi.NvDisplayDriverVersion
            {
                Version = (uint)NvApi.MAKE_NVAPI_VERSION<NvApi.NvDisplayDriverVersion>(1)
            };

            if (NvApi.NvApiGetDisplayDriverVersion != null &&
                NvApi.NvApiGetDisplayDriverVersion(_displayHandle, ref driverVersion) == NvApi.NvStatus.Ok)
            {
                var major = (int)(driverVersion.DriverVersion / 100);
                var minor = (int)(driverVersion.DriverVersion % 100);
                data.DriverVersion = $"{major}.{minor:00}";
            }
        }

        if (string.IsNullOrWhiteSpace(data.DriverVersion) || data.TotalMemory == 0) 
        {
            var (memSize, driver) = GetRegistryGpuDriverInformation(data.GpuName, true);
            if (string.IsNullOrWhiteSpace(data.DriverVersion))
            {
                data.DriverVersion = driver;
            }

            if (data.TotalMemory == 0)
            {
                memSize = memSize.Replace("GB",string.Empty).Replace("-",string.Empty);
                if (double.TryParse(memSize, out var registryMemorySize))
                {
                    data.TotalMemory = ClampValue(registryMemorySize);
                }
            }
        }

        return data;
    }
    
    public static double GetGpuVramSize(string gpuName)
    {
        try
        {
            using var videoKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\ControlSet001\Control\Video");

            if (videoKey == null)
            {
                return 0;
            }

            foreach (var providerKey in videoKey.GetSubKeyNames())
            {
                using var providerSubKey = videoKey.OpenSubKey(providerKey);
                if (providerSubKey == null)
                {
                    continue;
                }

                foreach (var gpuKey in providerSubKey.GetSubKeyNames())
                {
                    using var gpuSubKey = providerSubKey.OpenSubKey(gpuKey);
                    if (gpuSubKey == null)
                    {
                        continue;
                    }

                    var registryGpuName = gpuSubKey.GetValue("HardwareInformation.AdapterString");
                    if (registryGpuName as string == gpuName)
                    {
                        var memorySizeValue = gpuSubKey.GetValue("HardwareInformation.qwMemorySize");
                        if (memorySizeValue != null && memorySizeValue is long memorySizeBytes)
                        {
                            if (memorySizeBytes > 0)
                            {
                                // Делим на 1024 три раза для перевода в гигабайты
                                return memorySizeBytes / 1024.0 / 1024.0 / 1024.0;
                            }
                        }
                    }
                }
            }
        }
        catch 
        {
            //
        }

        return 0;
    }
    
    public static (string, string) GetRegistryGpuDriverInformation(string gpuName, bool isNvidia = false)
    {
        try
        {
            using var videoKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\ControlSet001\Control\Video");

            if (videoKey == null)
            {
                return ("-GB", "Unknown");
            }

            foreach (var providerKey in videoKey.GetSubKeyNames())
            {
                using var providerSubKey = videoKey.OpenSubKey(providerKey);
                if (providerSubKey == null)
                {
                    continue;
                }

                foreach (var gpuKey in providerSubKey.GetSubKeyNames())
                {
                    using var gpuSubKey = providerSubKey.OpenSubKey(gpuKey);
                    if (gpuSubKey == null)
                    {
                        continue;
                    }

                    var registryGpuName = gpuSubKey.GetValue(isNvidia 
                        ? "HardwareInformation.AdapterString" : "DriverDesc");
                    if (registryGpuName as string == gpuName)
                    {
                        var driverVersion = gpuSubKey.GetValue(isNvidia ? "DriverVersion" : "RadeonSoftwareVersion") as string;
                        var memorySizeValue = gpuSubKey.GetValue("HardwareInformation.qwMemorySize");
                        if (!string.IsNullOrEmpty(driverVersion))
                        {
                            var memorySize = "-GB";
                            if (memorySizeValue is long memorySizeBytes)
                            {
                                if (memorySizeBytes > 0)
                                {
                                    // Делим на 1024 три раза для перевода в гигабайты
                                    memorySize = (memorySizeBytes / 1024.0 / 1024.0 / 1024.0) + "GB";
                                }
                            }
                            else
                            {
                                if (int.TryParse(memorySizeValue as string, out var memorySizeFromString))
                                {
                                    memorySize = (memorySizeFromString / 1024.0 / 1024.0 / 1024.0) + "GB";
                                }
                            }

                            if (isNvidia)
                            {
                                driverVersion = ParseNvidiaDriverVersion(driverVersion);
                            }

                            return (memorySize, driverVersion);
                        }
                    }
                }
            }
        }
        catch 
        {
            //
        }

        return ("-GB", "Unknown");
    }
    
    public static string ParseNvidiaDriverVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        var parts = version.Split('.');

        if (parts.Length != 4)
        {
            return string.Empty;
        }


        if (!int.TryParse(parts[2], out var c) || !int.TryParse(parts[3], out var dddd))
        {
            return string.Empty;
        }

        var internalMajor = c * 100 + (dddd / 100);
        var major = internalMajor - 1000;
        var minor = dddd % 100;

        return $"{major}.{minor:D2}";
    }

    private static double ClampValue(double input)
    {
        var truncated = Math.Truncate(input);

        // Получаем дробную часть числа
        var fractionalPart = input - truncated;

        // Проверяем, близка ли дробная часть к 1 (если дробная часть >= 0.95)
        // Округляем до следующего целого числа
        return fractionalPart >= 0.95
            ? Math.Ceiling(input)
            :
            // Исходное значение без изменений
            input;
    }

    private static string DetermineMemoryType(string gpuName)
    {
        var name = gpuName.ToLowerInvariant();

        // RTX 50xx - GDDR7
        if (name.Contains("rtx 50"))
        {
            return "GDDR7";
        }

        // RTX 40xx - GDDR6X
        if (name.Contains("rtx 40"))
        {
            return "GDDR6X";
        }

        // RTX 30xx - GDDR6/GDDR6X
        if (name.Contains("rtx 30"))
        {
            if (name.Contains("3090") || name.Contains("3080"))
            {
                return "GDDR6X";
            }

            return "GDDR6";
        }

        // RTX 20xx, GTX 16xx - GDDR6
        if (name.Contains("rtx 20") || name.Contains("gtx 16"))
        {
            return "GDDR6";
        }

        // GTX 10xx - GDDR5/GDDR5X
        if (name.Contains("gtx 10"))
        {
            if (name.Contains("1080"))
            {
                return "GDDR5X";
            }

            return "GDDR5";
        }

        // Default
        return "Unknown";
    }

    private static int EstimateMemoryBusWidth(string gpuName)
    {
        var name = gpuName.ToLowerInvariant();

        // High-end карты (xx90, xx80 Ti) - обычно 384-bit
        if (name.Contains("90") || name.Contains("80 ti"))
        {
            return 384;
        }

        // Upper mid-range (xx80, xx70 Ti) - обычно 256-bit
        if (name.Contains("80") || name.Contains("70 ti"))
        {
            return 256;
        }

        // Mid-range (xx70, xx60 Ti) - обычно 192-bit или 256-bit
        if (name.Contains("70") || name.Contains("60 ti"))
        {
            return 256;
        }

        // Entry-level (xx60 и ниже) - обычно 128-bit или 192-bit
        if (name.Contains("60") || name.Contains("50"))
        {
            return 128;
        }

        // Default
        return 128;
    }
}