using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Saku_Overclock.Core.Contracts;

namespace Saku_Overclock.Core.Services;

public sealed class SafeGuardsService(ILogger<SafeGuardsService> logger) : ISafeGuardsService
{
    public bool PreviousSessionCrashed { get; private set; }
    public event EventHandler? CrashDetected;

    public void EnsureInitialized()
    {
        try
        {
            var eventLog = OpenEventLog(null, "System");
            if (eventLog == IntPtr.Zero)
            {
                logger.LogError("Не удалось открыть журнал System. Ошибка: {Error}",
                    new Win32Exception(Marshal.GetLastWin32Error()).Message);
                return;
            }

            try
            {
                var bufferSize = 8192;
                var buffer = Marshal.AllocHGlobal(bufferSize);

                try
                {
                    while (true)
                    {
                        var success = ReadEventLog(
                            eventLog,
                            EventLogReadFlags.EventLogSequentialRead | EventLogReadFlags.EventLogBackwardsRead,
                            0,
                            buffer,
                            bufferSize,
                            out var bytesRead,
                            out var minBytesNeeded);

                        if (!success)
                        {
                            const int errorInsufficientBuffer = 122;
                            if (Marshal.GetLastWin32Error() == errorInsufficientBuffer &&
                                minBytesNeeded > bufferSize)
                            {
                                bufferSize = minBytesNeeded;
                                Marshal.FreeHGlobal(buffer);
                                buffer = Marshal.AllocHGlobal(bufferSize);
                                continue;
                            }

                            break;
                        }

                        // Native AOT оптимизация: используем unsafe для парсинга без аллокаций
                        unsafe
                        {
                            byte* ptr = (byte*)buffer.ToPointer();
                            byte* endPtr = ptr + bytesRead;

                            while (ptr < endPtr)
                            {
                                var record = (EventLogRecord*)ptr;
                                var eventId = record->EventID & 0xFFFF;

                                // 6005 (EventLog Started) и 6009 (Boot Info) — маркеры старта службы.
                                // Так как мы читаем файл задом наперед по RecordNumber, 
                                // встреча этого ID означает, что мы дошли до старта текущей сессии ОС.
                                if (eventId is 6005 or 6009)
                                {
                                    return; // Дошли до загрузки ОС, инцидентов от прошлой сессии нет
                                }

                                if (eventId is 41 or 6008 or 1001)
                                {
                                    PreviousSessionCrashed = true;
                                    var reason = eventId switch
                                    {
                                        41 => "Kernel-Power (41)",
                                        1001 => "BugCheck/BSOD (1001)",
                                        _ => "Unexpected Shutdown (6008)"
                                    };

                                    logger.LogWarning("SafeGuards: Обнаружен краш. Причина: {Reason}", reason);
                                    CrashDetected?.Invoke(this, EventArgs.Empty);
                                    return;
                                }

                                ptr += record->Length;
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseEventLog(eventLog);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SafeGuards: Ошибка при анализе журнала событий.");
        }
    }

    [Flags]
    public enum EventLogReadFlags
    {
        EventLogSequentialRead = 0x0001,
        EventLogBackwardsRead = 0x0008
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventLogRecord
    {
        public int Length;
        public int Reserved;
        public int RecordNumber;
        public int TimeGenerated;
        public int TimeWritten;
        public int EventID;
        public short EventType;
        public short NumStrings;
        public short EventCategory;
        public short ReservedFlags;
        public int ClosingRecordNumber;
        public int StringOffset;
        public int UserSidLength;
        public int UserSidOffset;
        public int DataLength;
        public int DataOffset;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr OpenEventLog(string? lpUncServerName, string lpSourceName);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool ReadEventLog(
        IntPtr hEventLog,
        EventLogReadFlags dwReadFlags,
        int dwRecordOffset,
        IntPtr lpBuffer,
        int nNumberOfBytesToRead,
        out int pnBytesRead,
        out int pnMinNumberOfBytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseEventLog(IntPtr hEventLog);
}