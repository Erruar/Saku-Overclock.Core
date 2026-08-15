using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Saku_Overclock.Core.Helpers;

public static partial class BatteryManager
{
    public enum BatteryStatus : ushort
    {
        Discharging = 1,
        AcConnected = 2,
        FullyCharged = 3,
        Low = 4,
        Critical = 5,
        Charging = 6,
        ChargingAndHigh = 7,
        ChargingAndLow = 8,
        ChargingAndCritical = 9,
        Undefined = 10,
        PartiallyCharged = 11
    }

    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceinterface = 0x00000010;

    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;

    private const uint ErrorNoMoreItems = 259;
    private const uint ErrorFileNotFound = 2;
    private const uint ErrorNoSuchDevice = 433;

    private const uint BatteryPowerOnLine = 0x00000001;
    private const uint BatteryDischarging = 0x00000002;
    private const uint BatteryCharging = 0x00000004;
    private const uint BatteryCritical = 0x00000008;

    private const int BatteryUnknownRate = unchecked((int)0xFFFFFFFF);

    private const uint IoctlBatteryQueryTag = 0x00294040;
    private const uint IoctlBatteryQueryInformation = 0x00294044;
    private const uint IoctlBatteryQueryStatus = 0x0029404C;

    private enum BatteryQueryInformationLevel : uint
    {
        BatteryInformation = 0,
        BatteryDeviceName = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryQueryInformation
    {
        public uint BatteryTag;
        public BatteryQueryInformationLevel InformationLevel;
        public int AtRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryWaitStatus
    {
        public uint BatteryTag;
        public uint Timeout;
        public uint PowerState;
        public uint LowCapacity;
        public uint HighCapacity;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryStatusData
    {
        public uint PowerState;
        public uint Capacity;
        public uint Voltage;
        public int Rate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryInformation
    {
        public uint Capabilities;

        public byte Technology;

        public byte Reserved0;
        public byte Reserved1;
        public byte Reserved2;

        public uint Chemistry;

        public uint DesignedCapacity;
        public uint FullChargedCapacity;

        public uint DefaultAlert1;
        public uint DefaultAlert2;
        public uint CriticalBias;

        public uint CycleCount;
    }

    private static Guid GetBatteryGuid()
    {
        return new Guid("72631E54-78A4-11D0-BCF7-00AA00B7B32A");
    }

    private static readonly Lock Sync = new();

    private static SafeFileHandle? _batteryHandle;
    private static uint _batteryTag;

    private static bool _doNotTrackBattery;

    private static uint _designCapacity;
    private static uint _fullCapacity;
    private static uint _cycleCount;

    private static bool _batteryInformationCached;

    // ------------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------------

    public static decimal GetBatteryPercent()
    {
        try
        {
            if (!GetSystemPowerStatus(out var status))
                return 0;

            if (status.BatteryLifePercent == 0xFF)
                return 0;

            return status.BatteryLifePercent;
        }
        catch
        {
            return 0;
        }
    }

    public static BatteryStatus GetBatteryStatus()
    {
        try
        {
            if (!TryQueryBatteryStatus(out var status))
                return BatteryStatus.Undefined;

            var powerState = status.PowerState;

            var charging = (powerState & BatteryCharging) != 0;
            var discharging = (powerState & BatteryDischarging) != 0;
            var onLine = (powerState & BatteryPowerOnLine) != 0;
            var critical = (powerState & BatteryCritical) != 0;

            var percent = (int)GetBatteryPercent();

            if (critical)
                return charging
                    ? BatteryStatus.ChargingAndCritical
                    : BatteryStatus.Critical;

            if (charging)
            {
                // BATTERY_STATUS не имеет отдельных WMI-состояний
                // High/Low, поэтому их приходится выводить из текущего
                // уровня заряда.
                if (percent >= 90)
                    return BatteryStatus.ChargingAndHigh;

                if (percent <= 10)
                    return BatteryStatus.ChargingAndLow;

                return BatteryStatus.Charging;
            }

            if (onLine)
            {
                if (percent >= 100)
                    return BatteryStatus.FullyCharged;

                return BatteryStatus.AcConnected;
            }

            if (discharging)
            {
                if (percent <= 5)
                    return BatteryStatus.Critical;

                if (percent <= 10)
                    return BatteryStatus.Low;

                return BatteryStatus.Discharging;
            }

            if (percent >= 100)
                return BatteryStatus.FullyCharged;

            return BatteryStatus.PartiallyCharged;
        }
        catch
        {
            return BatteryStatus.Undefined;
        }
    }

    public static bool HasBattery()
    {
        try
        {
            if (!GetSystemPowerStatus(out var status))
                return false;

            return status.BatteryFlag != 0xFF &&
                   status.BatteryLifePercent != 0xFF;
        }
        catch
        {
            return false;
        }
    }

    public static decimal GetBatteryRate()
    {
        if (_doNotTrackBattery)
            return 0;

        if (!HasBattery())
        {
            _doNotTrackBattery = true;
            return 0;
        }

        try
        {
            if (!TryQueryBatteryStatus(out var status))
                return 0;

            if (status.Rate == BatteryUnknownRate)
                return 0;

            return status.Rate;
        }
        catch
        {
            return 0;
        }
    }

    public static int GetBatteryLifeTime()
    {
        try
        {
            if (!GetSystemPowerStatus(out var status))
                return 0;

            if (status.ACLineStatus == 1)
                return -1;

            if (status.BatteryLifeTime < 0)
                return 0;

            return status.BatteryLifeTime;
        }
        catch
        {
            return 0;
        }
    }

    public static string ConvertBatteryLifeTime(int input)
    {
        var timeSpan = TimeSpan.FromSeconds(input);

        var batTime = string.Empty;

        if ((int)timeSpan.TotalHours > 0)
            batTime += $"{(int)timeSpan.TotalHours}h ";

        if (timeSpan.Minutes > 0)
            batTime += $"{timeSpan.Minutes}m ";

        if (timeSpan.Seconds > 0 || batTime.Length == 0)
            batTime += $"{timeSpan.Seconds}s";

        return batTime;
    }

    public static string? GetBatteryName()
    {
        if (_doNotTrackBattery)
            return string.Empty;

        try
        {
            if (!TryOpenBattery(out var handle, out var tag))
                return string.Empty;

            return QueryString(
                handle,
                tag,
                BatteryQueryInformationLevel.BatteryDeviceName);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static decimal GetBatteryHealth()
    {
        try
        {
            var design = ReadDesignCapacity(out var doNotTrack);

            if (doNotTrack || design == 0)
                return 0;

            var full = ReadFullChargeCapacity();

            if (full == 0)
                return 0;

            return (decimal)full / design;
        }
        catch
        {
            return 0;
        }
    }

    public static int GetBatteryCycle()
    {
        if (_doNotTrackBattery)
            return 0;

        try
        {
            if (!EnsureBatteryInformation())
                return 0;

            return unchecked((int)_cycleCount);
        }
        catch
        {
            return 0;
        }
    }

    public static uint ReadFullChargeCapacity()
    {
        if (_doNotTrackBattery)
            return 0;

        try
        {
            if (!EnsureBatteryInformation())
                return 0;

            return _fullCapacity;
        }
        catch
        {
            return 0;
        }
    }

    public static uint ReadDesignCapacity(out bool doNotTrack)
    {
        if (!HasBattery())
        {
            _doNotTrackBattery = true;
            doNotTrack = true;
            return 0;
        }

        if (_doNotTrackBattery)
        {
            doNotTrack = true;
            return 0;
        }

        try
        {
            if (!EnsureBatteryInformation())
            {
                _doNotTrackBattery = true;
                doNotTrack = true;
                return 0;
            }

            _doNotTrackBattery = false;
            doNotTrack = false;

            return _designCapacity;
        }
        catch
        {
            _doNotTrackBattery = true;
            doNotTrack = true;
            return 0;
        }
    }

    // ------------------------------------------------------------------------
    // Battery device handling
    // ------------------------------------------------------------------------

    private static bool EnsureBatteryInformation()
    {
        lock (Sync)
        {
            if (_batteryInformationCached)
                return _designCapacity != 0 ||
                       _fullCapacity != 0 ||
                       _cycleCount != 0;

            if (!TryOpenBattery(out var handle, out var tag))
                return false;

            if (!QueryBatteryInformation(handle, tag, out var info))
                return false;

            _designCapacity = info.DesignedCapacity;
            _fullCapacity = info.FullChargedCapacity;
            _cycleCount = info.CycleCount;

            _batteryInformationCached = true;

            return true;
        }
    }

    private static bool TryQueryBatteryStatus(out BatteryStatusData status)
    {
        status = default;

        lock (Sync)
        {
            if (!TryGetCachedBattery(out var handle, out var tag))
                if (!TryOpenBattery(out handle, out tag))
                    return false;

            return QueryBatteryStatus(handle, tag, out status);
        }
    }

    private static bool TryOpenBattery(
        out SafeFileHandle handle,
        out uint tag)
    {
        handle = new SafeFileHandle(IntPtr.Zero, false);
        tag = 0;

        var batteryGuid = GetBatteryGuid();

        var deviceInfoSet = SetupDiGetClassDevsW(
            ref batteryGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            DigcfPresent | DigcfDeviceinterface);

        if (deviceInfoSet == IntPtr.Zero ||
            deviceInfoSet == new IntPtr(-1))
            return false;

        try
        {
            for (uint index = 0;; index++)
            {
                var interfaceData = new SpDeviceInterfaceData
                {
                    cbSize = (uint)Marshal.SizeOf<SpDeviceInterfaceData>()
                };

                if (!SetupDiEnumDeviceInterfaces(
                        deviceInfoSet,
                        IntPtr.Zero,
                        ref batteryGuid,
                        index,
                        ref interfaceData))
                {
                    var error = Marshal.GetLastPInvokeError();

                    if (error == ErrorNoMoreItems)
                        break;

                    continue;
                }

                SetupDiGetDeviceInterfaceDetailW(
                    deviceInfoSet,
                    ref interfaceData,
                    IntPtr.Zero,
                    0,
                    out var requiredSize,
                    IntPtr.Zero);

                if (requiredSize == 0)
                    continue;

                var detailData = new byte[requiredSize];

                unsafe
                {
                    fixed (byte* pDetailData = detailData)
                    {
                        *(int*)pDetailData = 8;

                        if (!SetupDiGetDeviceInterfaceDetailW(
                                deviceInfoSet,
                                ref interfaceData,
                                (IntPtr)pDetailData,
                                requiredSize,
                                out _,
                                IntPtr.Zero))
                            continue;

                        // If something goes unexpected there was pDetailData + 8
                        var devicePath =
                            Marshal.PtrToStringUni(
                                (IntPtr)(pDetailData + 4));

                        if (string.IsNullOrEmpty(devicePath))
                            continue;

                        var candidate = CreateFileW(
                            devicePath,
                            GenericRead,
                            FileShareRead | FileShareWrite,
                            IntPtr.Zero,
                            OpenExisting,
                            FileAttributeNormal,
                            IntPtr.Zero);

                        if (candidate.IsInvalid)
                        {
                            candidate.Dispose();
                            continue;
                        }

                        if (!QueryBatteryTag(
                                candidate,
                                out var batteryTag))
                        {
                            candidate.Dispose();
                            continue;
                        }

                        ReplaceCachedBattery(
                            candidate,
                            batteryTag);

                        handle = candidate;
                        tag = batteryTag;

                        return true;
                    }
                }
            }

            return false;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static bool TryGetCachedBattery(
        out SafeFileHandle handle,
        out uint tag)
    {
        if (_batteryHandle is { IsInvalid: false, IsClosed: false } &&
            _batteryTag != 0)
        {
            handle = _batteryHandle;
            tag = _batteryTag;
            return true;
        }

        handle = new SafeFileHandle(IntPtr.Zero, false);
        tag = 0;

        return false;
    }

    private static void ReplaceCachedBattery(
        SafeFileHandle handle,
        uint tag)
    {
        if (_batteryHandle is { IsInvalid: false, IsClosed: false }) _batteryHandle.Dispose();

        _batteryHandle = handle;
        _batteryTag = tag;

        _batteryInformationCached = false;
        _designCapacity = 0;
        _fullCapacity = 0;
        _cycleCount = 0;
    }

    // ------------------------------------------------------------------------
    // IOCTL
    // ------------------------------------------------------------------------

    private static unsafe bool QueryBatteryTag(
        SafeFileHandle handle,
        out uint tag)
    {
        uint batteryTag = 0;

        var result = DeviceIoControl(
            handle,
            IoctlBatteryQueryTag,
            IntPtr.Zero,
            0,
            (IntPtr)(&batteryTag),
            sizeof(uint),
            out _,
            IntPtr.Zero);

        tag = batteryTag;
        return result;
    }

    private static unsafe bool QueryBatteryStatus(
        SafeFileHandle handle,
        uint tag,
        out BatteryStatusData status)
    {
        BatteryStatusData batteryStatus = default;

        var waitStatus = new BatteryWaitStatus
        {
            BatteryTag = tag,
            Timeout = 0,
            PowerState = 0,
            LowCapacity = 0,
            HighCapacity = 0
        };

        var ok = DeviceIoControl(
            handle,
            IoctlBatteryQueryStatus,
            (IntPtr)(&waitStatus),
            (uint)sizeof(BatteryWaitStatus),
            (IntPtr)(&batteryStatus),
            (uint)sizeof(BatteryStatusData),
            out _,
            IntPtr.Zero);

        status = batteryStatus;

        if (ok)
            return true;

        var error = Marshal.GetLastPInvokeError();

        if (error == ErrorFileNotFound ||
            error == ErrorNoSuchDevice)
            InvalidateCachedBattery();

        return false;
    }

    private static unsafe bool QueryBatteryInformation(
        SafeFileHandle handle,
        uint tag,
        out BatteryInformation information)
    {
        BatteryInformation batteryInformation = default;

        var query = new BatteryQueryInformation
        {
            BatteryTag = tag,
            InformationLevel =
                BatteryQueryInformationLevel.BatteryInformation,
            AtRate = 0
        };

        var ok = DeviceIoControl(
            handle,
            IoctlBatteryQueryInformation,
            (IntPtr)(&query),
            (uint)sizeof(BatteryQueryInformation),
            (IntPtr)(&batteryInformation),
            (uint)sizeof(BatteryInformation),
            out _,
            IntPtr.Zero);

        information = batteryInformation;

        if (ok)
            return true;

        var error = Marshal.GetLastPInvokeError();

        if (error == ErrorFileNotFound ||
            error == ErrorNoSuchDevice)
            InvalidateCachedBattery();

        return false;
    }

    private static string? QueryString(
        SafeFileHandle handle,
        uint tag,
        BatteryQueryInformationLevel level)
    {
        var query = new BatteryQueryInformation
        {
            BatteryTag = tag,
            InformationLevel = level,
            AtRate = 0
        };

        // BatteryDeviceName is expected to be a small UTF-16 string.
        unsafe
        {
            const int bufferChars = 256;

            var buffer = stackalloc char[bufferChars];

            var ok = DeviceIoControl(
                handle,
                IoctlBatteryQueryInformation,
                (IntPtr)(&query),
                (uint)sizeof(BatteryQueryInformation),
                (IntPtr)buffer,
                bufferChars * sizeof(char),
                out var returned,
                IntPtr.Zero);

            if (!ok)
                return null;

            var charCount = (int)(returned / sizeof(char));

            if (charCount <= 0)
                return string.Empty;

            if (charCount > bufferChars)
                charCount = bufferChars;

            var length = 0;

            while (length < charCount && buffer[length] != '\0')
                length++;

            return new string(buffer, 0, length);
        }
    }

    private static void InvalidateCachedBattery()
    {
        lock (Sync)
        {
            if (_batteryHandle is { IsInvalid: false, IsClosed: false }) _batteryHandle.Dispose();

            _batteryHandle = null;
            _batteryTag = 0;

            _batteryInformationCached = false;

            _designCapacity = 0;
            _fullCapacity = 0;
            _cycleCount = 0;
        }
    }

    // ------------------------------------------------------------------------
    // Win32 Power API
    // ------------------------------------------------------------------------

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(
        out SystemPowerStatus lpSystemPowerStatus);

    // ------------------------------------------------------------------------
    // SetupAPI
    // ------------------------------------------------------------------------

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
    private static partial IntPtr SetupDiGetClassDevsW(
        ref Guid classGuid,
        IntPtr enumerator,
        IntPtr hwndParent,
        uint flags);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiEnumDeviceInterfaces", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDeviceInterfaceDetailW(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial void SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    // ------------------------------------------------------------------------
    // Kernel32 / IOCTL
    // ------------------------------------------------------------------------

    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "CreateFileW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);
}