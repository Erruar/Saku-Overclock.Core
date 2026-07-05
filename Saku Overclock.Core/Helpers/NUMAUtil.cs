using System.Runtime.InteropServices;

namespace Saku_Overclock.Core.Helpers;

public abstract partial class NumaUtil
{
    public static ulong HighestNumaNode
    {
        get
        {
            ulong n = 0;
            GetNumaHighestNodeNumber(ref n);
            return n;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct GroupAffinity(ushort[]? reserved)
    {
        public UIntPtr Mask;
        [MarshalAs(UnmanagedType.U2)] public ushort Group;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.U2)]
        public ushort[]? Reserved = reserved;
    }

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool SetThreadGroupAffinity(
        IntPtr hThread,
        ref GroupAffinity groupAffinity,
        ref GroupAffinity previousGroupAffinity);

    [LibraryImport("kernel32", SetLastError = true)]
    private static partial IntPtr GetCurrentThread();

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial void GetNumaHighestNodeNumber(ref ulong highestNodeNumer);


    // Sets the processor group and the processor cpu affinity of the current thread.
    public static void SetThreadProcessorAffinity(ushort groupId, params int[] cpus)
    {
        ArgumentNullException.ThrowIfNull(cpus);
        if (cpus.Length == 0)
        {
            throw new ArgumentException("You must specify at least one CPU.", nameof(cpus));
        }

        // Supports up to 64 processors
        long cpuMask = 0;
        foreach (var cpu in cpus)
        {
            if (cpu < 0 || cpu >= Environment.ProcessorCount)
            {
                throw new ArgumentException("Invalid CPU number.");
            }

            cpuMask |= 1L << cpu;
        }

        var hThread = GetCurrentThread();
        var previousAffinity = new GroupAffinity { Reserved = new ushort[3] };
        var newAffinity = new GroupAffinity
        {
            Group = groupId,
            Mask = new UIntPtr((ulong)cpuMask),
            Reserved = new ushort[3]
        };

        SetThreadGroupAffinity(hThread, ref newAffinity, ref previousAffinity);
    }
}