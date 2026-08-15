using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Saku_Overclock.Core.Contracts;
using Saku_Overclock.Core.Helpers;
using Saku_Overclock.Shared;
using Saku_Overclock.Shared.Ipc;

namespace Saku_Overclock.Core.Services;

public unsafe class RawSharedMemoryWriterService(IpcHub hub, ICpuService cpu, ILogger<RawSharedMemoryWriterService> logger) : IRawSharedMemoryWriterService, IDisposable
{
    private IntPtr _mappingHandle;
    private IntPtr _viewPtr;
    private int _currentCapacity;
    private bool _isRawUpdateActive;
    
    public bool IsRawUpdateActive => _isRawUpdateActive;

    public void RegisterIpcHandlers()
    {
        hub.RegisterHandler("RawData_StartUpdate", (cmd, _) =>
        {
            _isRawUpdateActive = true;
    
            var initialData = (int)cpu.PowerTableSize;
            InitializeMapping(initialData);
    
            return Task.FromResult(Ok(cmd.Id, JsonSerializer.Serialize(initialData, IpcJsonContext.Default.Int32))); 
        });

        hub.RegisterHandler("RawData_StopUpdate", (cmd, _) =>
        {
            _isRawUpdateActive = false;
            Dispose();
            return Task.FromResult(Ok(cmd.Id, ""));
        });   
        
        hub.ClientDisconnected += HubOnAllClientsDisconnected;
    }

    private void HubOnAllClientsDisconnected(object? sender, Guid e)
    {
        if (_isRawUpdateActive)
        {
            logger.LogInformation("All clients disconnected. Stopping update sensors");
            _isRawUpdateActive = false;
            Dispose();
        }
    }

    private static IpcMessage Ok(string id, string payload) =>
        new() { Kind = IpcMessageKind.Response, Id = id, Payload = payload };
    
    private void InitializeMapping(int elementCount)
    {
        if (_mappingHandle != IntPtr.Zero && _currentCapacity == elementCount) 
            return; 

        Dispose(); 

        _currentCapacity = elementCount;

        var sizeInBytes = 2 * sizeof(int) + elementCount * sizeof(float);

        const string sddl = "D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;GR;;;AU)";
        if (!NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptor(
                sddl, 1, out var pSecurityDescriptor, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            var sa = new NativeMethods.SecurityAttributes
            {
                nLength = Marshal.SizeOf<NativeMethods.SecurityAttributes>(),
                lpSecurityDescriptor = pSecurityDescriptor,
                bInheritHandle = 0
            };

            _mappingHandle = NativeMethods.CreateFileMapping(
                new IntPtr(-1), ref sa, NativeMethods.PageReadwrite, 0, (uint)sizeInBytes, @"Global\SakuOverclock_RawSensors");

            if (_mappingHandle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            NativeMethods.LocalFree(pSecurityDescriptor);
        }

        _viewPtr = NativeMethods.MapViewOfFile(_mappingHandle, NativeMethods.FileMapAllAccess, 0, 0, (nuint)sizeInBytes);
        if (_viewPtr == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void UpdateRawData(float[]? rawData)
    {
        if (!_isRawUpdateActive || rawData ==null || _viewPtr == IntPtr.Zero) return;

        var header = (int*)_viewPtr;
        
        header[0]++; 

        var dataPtr = (float*)(header + 2);
        var count = Math.Min(rawData.Length, _currentCapacity);
        
        for (var i = 0; i < count; i++)
        {
            dataPtr[i] = rawData[i];
        }
        header[0]++;

        header[1] = header[0];
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
        _currentCapacity = 0;
    }
}