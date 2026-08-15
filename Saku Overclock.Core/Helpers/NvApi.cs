using System.Runtime.InteropServices;
using System.Text;

namespace Saku_Overclock.Core.Helpers;

internal static unsafe class NvApi
{
    private const int MaxGpuUtilization = 8;
    public const int MaxPhysicalGpus = 64;
    public const int MaxThermalSensorsPerGpu = 3;
    private const int MaxGpuPublicClocks = 32;
    private const int ShortStringMax = 64;

    private const string DllName = "nvapi.dll";
    private const string DllName64 = "nvapi64.dll";

    public static delegate* unmanaged[Cdecl]<NvPhysicalGpuHandle*, int*, NvStatus> NvApiEnumPhysicalGpUs { get; private set; }
    public static delegate* unmanaged[Cdecl]<int, NvDisplayHandle*, NvStatus> NvApiEnumNvidiaDisplayHandle { get; private set; }
    public static delegate* unmanaged[Cdecl]<NvPhysicalGpuHandle, int, NvThermalSettings*, NvStatus> NvApiGpuGetThermalSettings { get; private set; }
    public static delegate* unmanaged[Cdecl]<NvPhysicalGpuHandle, NvGpuClockFrequencies*, NvStatus> NvApiGpuGetAllClockFrequencies { get; private set; }
    public static delegate* unmanaged[Cdecl]<NvPhysicalGpuHandle, NvDynamicPStatesInfo*, NvStatus> NvApiGpuGetDynamicPstatesInfoEx { get; private set; }
    public static delegate* unmanaged[Cdecl]<NvDisplayHandle, NvMemoryInfo*, NvStatus> NvApiGpuGetMemoryInfo { get; private set; }
    public static delegate* unmanaged[Cdecl]<NvDisplayHandle, NvDisplayDriverVersion*, NvStatus> NvApiGetDisplayDriverVersion { get; private set; }
    
    private static delegate* unmanaged[Cdecl]<NvPhysicalGpuHandle, byte*, NvStatus> _nvApiGpuGetFullName;

    public static bool IsAvailable { get; private set; }

    public static void Initialize()
    {
        try
        {
            var initPtr = GetPtr(0x0150E828);
            if (initPtr == IntPtr.Zero) return;

            var nvApiInitialize = (delegate* unmanaged[Cdecl]<NvStatus>)initPtr;

            if (nvApiInitialize() == NvStatus.Ok)
            {
                NvApiEnumPhysicalGpUs = (delegate* unmanaged[Cdecl]<NvPhysicalGpuHandle*, int*, NvStatus>)GetPtr(0xE5AC921F);
                NvApiEnumNvidiaDisplayHandle = (delegate* unmanaged[Cdecl]<int, NvDisplayHandle*, NvStatus>)GetPtr(0x9ABDD40D);
                NvApiGpuGetThermalSettings = (delegate* unmanaged[Cdecl]<NvPhysicalGpuHandle, int, NvThermalSettings*, NvStatus>)GetPtr(0xE3640A56);
                NvApiGpuGetAllClockFrequencies = (delegate* unmanaged[Cdecl]<NvPhysicalGpuHandle, NvGpuClockFrequencies*, NvStatus>)GetPtr(0xDCB616C3);
                NvApiGpuGetDynamicPstatesInfoEx = (delegate* unmanaged[Cdecl]<NvPhysicalGpuHandle, NvDynamicPStatesInfo*, NvStatus>)GetPtr(0x60DED2ED);
                NvApiGpuGetMemoryInfo = (delegate* unmanaged[Cdecl]<NvDisplayHandle, NvMemoryInfo*, NvStatus>)GetPtr(0x774AA982);
                NvApiGetDisplayDriverVersion = (delegate* unmanaged[Cdecl]<NvDisplayHandle, NvDisplayDriverVersion*, NvStatus>)GetPtr(0xF951A4D1);
                _nvApiGpuGetFullName = (delegate* unmanaged[Cdecl]<NvPhysicalGpuHandle, byte*, NvStatus>)GetPtr(0xCEEE8E9F);

                IsAvailable = true;
            }
        }
        catch
        {
            IsAvailable = false;
        }
    }

    [DllImport(DllName, EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NvAPI32_QueryInterface(uint interfaceId);

    [DllImport(DllName64, EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NvAPI64_QueryInterface(uint interfaceId);

    private static IntPtr GetPtr(uint id) => 
        Environment.Is64BitProcess ? NvAPI64_QueryInterface(id) : NvAPI32_QueryInterface(id);

    public static NvStatus NvAPI_GPU_GetFullName(NvPhysicalGpuHandle gpuHandle, out string name)
    {
        if (_nvApiGpuGetFullName == null)
        {
            name = string.Empty;
            return NvStatus.FunctionNotFound;
        }

        // Избавляемся от StringBuilder. Память выделяется на стеке, 0 аллокаций в куче.
        byte* buffer = stackalloc byte[ShortStringMax];
        var status = _nvApiGpuGetFullName(gpuHandle, buffer);
        
        if (status == NvStatus.Ok)
        {
            int length = 0;
            while (length < ShortStringMax && buffer[length] != 0) length++;
            name = Encoding.ASCII.GetString(buffer, length);
        }
        else
        {
            name = string.Empty;
        }

        return status;
    }

    // Natively sizeof(T) вместо маршалинга
    internal static uint MAKE_NVAPI_VERSION<T>(uint ver) where T : unmanaged => (uint)sizeof(T) | (ver << 16);

    // Enums
    public enum NvStatus
    {
        Ok = 0,
        FunctionNotFound = -136
    }
    public enum NvThermalTarget { All = 15 }
    public enum NvThermalController { None = 0 }
    public enum NvGpuPublicClockId { Graphics = 0, Memory = 4 }

    // Structs
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct NvPhysicalGpuHandle { private readonly IntPtr ptr; }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct NvDisplayHandle { private readonly IntPtr ptr; }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NvThermalSettings
    {
        public uint Version;
        public uint Count;
        
        public NvSensor Sensor0, Sensor1, Sensor2;

        public Span<NvSensor> GetSensors()
        {
            fixed (NvSensor* p = &Sensor0) return new Span<NvSensor>(p, MaxThermalSensorsPerGpu);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NvSensor
    {
        public NvThermalController Controller;
        public uint DefaultMinTemp;
        public uint DefaultMaxTemp;
        public uint CurrentTemp;
        public NvThermalTarget Target;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NvGpuClockFrequencies
    {
        public uint Version;
        private readonly uint _reserved;

        public fixed ulong ClocksRaw[MaxGpuPublicClocks];

        public Span<NvGpuClockFrequenciesDomain> GetClocks()
        {
            fixed (ulong* p = ClocksRaw)
                return new Span<NvGpuClockFrequenciesDomain>(p, MaxGpuPublicClocks);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NvGpuClockFrequenciesDomain
    {
        private readonly uint _isPresent;
        public uint Frequency;
        public bool IsPresent => (_isPresent & 1) != 0;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NvDynamicPStatesInfo
    {
        public uint Version;
        public uint Flags;

        public NvDynamicPState Util0, Util1, Util2, Util3, Util4, Util5, Util6, Util7;

        public Span<NvDynamicPState> GetUtilizations()
        {
            fixed (NvDynamicPState* p = &Util0) return new Span<NvDynamicPState>(p, MaxGpuUtilization);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NvDynamicPState
    {
        public bool IsPresent;
        public int Percentage;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NvMemoryInfo
    {
        public uint Version;
        public uint DedicatedVideoMemory;
        public uint AvailableDedicatedVideoMemory;
        public uint SystemVideoMemory;
        public uint SharedSystemMemory;
        public uint CurrentAvailableDedicatedVideoMemory;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NvDisplayDriverVersion
    {
        public uint Version;
        public uint DriverVersion;
        public uint BldChangeListNum;

        public fixed byte BuildBranch[ShortStringMax];
        public fixed byte Adapter[ShortStringMax];
    }
}