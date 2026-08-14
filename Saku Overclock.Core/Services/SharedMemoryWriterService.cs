using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Saku_Overclock.Core.Contracts;
using Saku_Overclock.Core.Helpers;
using Saku_Overclock.Shared.Models; 

namespace Saku_Overclock.Core.Services;

public unsafe class SharedMemoryWriterService : ISharedMemoryWriterService, IDisposable
{
    private IntPtr _mappingHandle;
    private IntPtr _viewPtr;
    private readonly SensorsInformationShared* _sharedData;

    public SharedMemoryWriterService()
    {
        int size = Unsafe.SizeOf<SensorsInformationShared>();

        // SY = SYSTEM (rw access), BA = Administrators (rw access), AU = Authenticated Users (r access)
        const string sddl = "D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;GR;;;AU)";

        if (!NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptor(
                sddl, 1 /* Sddl_REVISION_1 */, out IntPtr pSecurityDescriptor, out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var sa = new NativeMethods.SecurityAttributes()
            {
                nLength = Marshal.SizeOf<NativeMethods.SecurityAttributes>(),
                lpSecurityDescriptor = pSecurityDescriptor,
                bInheritHandle = 0
            };

            _mappingHandle = NativeMethods.CreateFileMapping(
                new IntPtr(-1), // INVALID_HANDLE_VALUE — Mapping on paging file
                ref sa,
                NativeMethods.PageReadwrite,
                0,
                (uint)size,
                @"Global\SakuOverclock_Sensors");

            if (_mappingHandle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            // ERROR_ALREADY_EXISTS (183) — не ошибка, значит объект уже был создан ранее
        }
        finally
        {
            NativeMethods.LocalFree(pSecurityDescriptor);
        }

        _viewPtr = NativeMethods.MapViewOfFile(
            _mappingHandle,
            NativeMethods.FileMapAllAccess,
            0, 0, (nuint)size);

        if (_viewPtr == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            NativeMethods.CloseHandle(_mappingHandle);
            throw new Win32Exception(error);
        }

        _sharedData = (SensorsInformationShared*)_viewPtr;
    }

    public void UpdateSharedMemory(SensorsInformation data)
    {
        _sharedData->Iteration++;

        // CPU / GPU / VRM Information
        _sharedData->CpuStapmLimit = data.CpuStapmLimit;
        _sharedData->CpuStapmValue = data.CpuStapmValue;
        _sharedData->CpuFastLimit = data.CpuFastLimit;
        _sharedData->CpuFastValue = data.CpuFastValue;
        _sharedData->CpuSlowLimit = data.CpuSlowLimit;
        _sharedData->CpuSlowValue = data.CpuSlowValue;
        _sharedData->ApuSlowLimit = data.ApuSlowLimit;
        _sharedData->ApuSlowValue = data.ApuSlowValue;
        
        _sharedData->VrmTdcValue = data.VrmTdcValue;
        _sharedData->VrmTdcLimit = data.VrmTdcLimit;
        _sharedData->VrmEdcValue = data.VrmEdcValue;
        _sharedData->VrmEdcLimit = data.VrmEdcLimit;
        _sharedData->VrmPsiValue = data.VrmPsiValue;
        _sharedData->VrmPsiSocValue = data.VrmPsiSocValue;
        
        _sharedData->SocTdcValue = data.SocTdcValue;
        _sharedData->SocTdcLimit = data.SocTdcLimit;
        _sharedData->SocEdcValue = data.SocEdcValue;
        _sharedData->SocEdcLimit = data.SocEdcLimit;
        
        _sharedData->CpuTempValue = data.CpuTempValue;
        _sharedData->CpuTempLimit = data.CpuTempLimit;
        _sharedData->ApuTempValue = data.ApuTempValue;
        _sharedData->ApuTempLimit = data.ApuTempLimit;
        _sharedData->DgpuTempValue = data.DgpuTempValue;
        _sharedData->DgpuTempLimit = data.DgpuTempLimit;
        
        _sharedData->CpuStapmTimeValue = data.CpuStapmTimeValue;
        _sharedData->CpuSlowTimeValue = data.CpuSlowTimeValue;
        _sharedData->CpuUsage = data.CpuUsage;

        _sharedData->ApuFrequency = data.ApuFrequency;
        _sharedData->ApuVoltage = data.ApuVoltage;
        _sharedData->MemFrequency = data.MemFrequency;
        _sharedData->FabricFrequency = data.FabricFrequency;
        _sharedData->SocPower = data.SocPower;
        _sharedData->SocVoltage = data.SocVoltage;
        _sharedData->CpuFrequency = data.CpuFrequency;
        _sharedData->CpuVoltage = data.CpuVoltage;

        // Battery Information
        _sharedData->BatteryUnavailable = data.BatteryUnavailable;
        _sharedData->BatteryPercent = data.BatteryPercent;
        _sharedData->BatteryState = data.BatteryState;
        _sharedData->BatteryChargeRate = data.BatteryChargeRate;
        _sharedData->BatteryLifeTime = data.BatteryLifeTime;

        // RAM
        _sharedData->RamTotal = data.RamTotal;
        _sharedData->RamBusy = data.RamBusy;
        _sharedData->RamUsagePercent = data.RamUsagePercent;

        // Nvidia GPU Information
        _sharedData->IsNvidiaGpuAvailable = data.IsNvidiaGpuAvailable;
        _sharedData->NvidiaVramFrequency = data.NvidiaVramFrequency;
        _sharedData->NvidiaGpuUsage = data.NvidiaGpuUsage;
        _sharedData->NvidiaGpuFrequency = data.NvidiaGpuFrequency;
        _sharedData->NvidiaGpuTemperature = data.NvidiaGpuTemperature;

        // Arrays
        if (data.CpuFrequencyPerCore != null)
        {
            int coreCount = Math.Min(data.CpuFrequencyPerCore.Length, 32);
            for (int i = 0; i < coreCount; i++) _sharedData->CpuFrequencyPerCore[i] = data.CpuFrequencyPerCore[i];
        }
        if (data.CpuVoltagePerCore != null)
        {
            int coreCount = Math.Min(data.CpuVoltagePerCore.Length, 32);
            for (int i = 0; i < coreCount; i++) _sharedData->CpuVoltagePerCore[i] = data.CpuVoltagePerCore[i];
        }
        if (data.CpuPowerPerCore != null)
        {
            int coreCount = Math.Min(data.CpuPowerPerCore.Length, 32);
            for (int i = 0; i < coreCount; i++) _sharedData->CpuPowerPerCore[i] = data.CpuPowerPerCore[i];
        }
        if (data.CpuTemperaturePerCore != null)
        {
            int coreCount = Math.Min(data.CpuTemperaturePerCore.Length, 32);
            for (int i = 0; i < coreCount; i++) _sharedData->CpuTemperaturePerCore[i] = data.CpuTemperaturePerCore[i];
        }

        WriteStringToSafeString16(data.CpuFamily, ref _sharedData->CpuCodeName);
        WriteStringToSafeString32(data.BatteryName, ref _sharedData->BatteryName);
        WriteStringToSafeString16(data.BatteryHealth, ref _sharedData->BatteryHealth);
        WriteStringToSafeString32(data.BatteryCycles, ref _sharedData->BatteryCycles);
        WriteStringToSafeString32(data.BatteryCapacity, ref _sharedData->BatteryCapacity);
        WriteStringToSafeString16(data.NvidiaDriverVersion, ref _sharedData->NvidiaDriverVersion);
        WriteStringToSafeString16(data.NvidiaVramSize, ref _sharedData->NvidiaVramSize);
        WriteStringToSafeString16(data.NvidiaVramType, ref _sharedData->NvidiaVramType);
        WriteStringToSafeString16(data.NvidiaVramWidth, ref _sharedData->NvidiaVramWidth);

        _sharedData->IterationEnd = _sharedData->Iteration;
    }

    private void WriteStringToSafeString16(string? text, ref SafeString16 target)
    {
        if (text == null) { target[0] = '\0'; return; }
        int length = Math.Min(text.Length, 16);
        for (int i = 0; i < length; i++) target[i] = text[i];
        if (length < 16) target[length] = '\0';
    }

    private void WriteStringToSafeString32(string? text, ref SafeString32 target)
    {
        if (text == null) { target[0] = '\0'; return; }
        int length = Math.Min(text.Length, 32);
        for (int i = 0; i < length; i++) target[i] = text[i];
        if (length < 32) target[length] = '\0';
    }

    public void Dispose()
    {
        if (_viewPtr != IntPtr.Zero)
        {
            NativeMethods.UnmapViewOfFile(_viewPtr);
            _viewPtr = IntPtr.Zero;
        }
        if (_mappingHandle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_mappingHandle);
            _mappingHandle = IntPtr.Zero;
        }
    }
}