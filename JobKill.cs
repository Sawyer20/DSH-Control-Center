// ============================================================================
//  JobKill.cs - keep the backend from ever being orphaned
//
//  The node backend is a CHILD process. Windows does not kill children when the
//  parent dies, so a crashed / force-killed / task-managed DSH.exe used to leave
//  `node bin.js web` running and holding port 3080. The next launch then saw the
//  port busy and reported "由其他实例运行" forever - exactly what happened today
//  with PID 20740 (started 17:09:46, parent 12276 long dead, no stop log).
//
//  Assigning the child to a Job Object with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
//  makes the OS terminate it whenever this process ends for ANY reason: the
//  kernel closes the job handle during process teardown, and that close kills
//  everything in the job.
// ============================================================================
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class JobKill
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    private static IntPtr _job = IntPtr.Zero;

    private static bool EnsureJob()
    {
        if (_job != IntPtr.Zero) return true;
        try
        {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return false;
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            int len = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            IntPtr buf = Marshal.AllocHGlobal(len);
            try
            {
                Marshal.StructureToPtr(info, buf, false);
                if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, buf, (uint)len)) return false;
            }
            finally { Marshal.FreeHGlobal(buf); }
            // deliberately never closed: the kernel closes it when we exit,
            // which is what kills the backend with us.
            _job = job;
            return true;
        }
        catch { return false; }
    }

    /// <summary>Put a freshly started child into the kill-on-close job (best effort).</summary>
    public static void Assign(Process child)
    {
        if (child == null) return;
        try
        {
            if (!EnsureJob()) return;
            AssignProcessToJobObject(_job, child.Handle);
        }
        catch { }
    }
}
