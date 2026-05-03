using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace WunderBar
{
    public class ProcessInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public double RamUsage { get; set; }
        public bool IsApp { get; set; }
    }

    public class ProcessService
    {
        public List<ProcessInfo> GetProcesses()
        {
            var results = new List<ProcessInfo>();
            var processes = Process.GetProcesses();

            foreach (var p in processes)
            {
                try
                {
                    bool isApp = !string.IsNullOrEmpty(p.MainWindowTitle) && p.MainWindowHandle != IntPtr.Zero;
                    
                    results.Add(new ProcessInfo
                    {
                        Id = p.Id,
                        Name = p.ProcessName,
                        Title = p.MainWindowTitle,
                        RamUsage = Math.Round(p.WorkingSet64 / (1024.0 * 1024.0), 1),
                        IsApp = isApp
                    });
                }
                catch
                {
                    // Access denied for some system processes
                }
                finally
                {
                    p.Dispose();
                }
            }

            return results.OrderByDescending(r => r.RamUsage).ToList();
        }

        public void KillProcess(int id)
        {
            try
            {
                using var p = Process.GetProcessById(id);
                p.Kill();
            }
            catch { }
        }

        public void SuspendProcess(int id)
        {
            IntPtr hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_SUSPEND_RESUME, false, id);
            if (hProc != IntPtr.Zero)
            {
                try { NativeMethods.NtSuspendProcess(hProc); }
                finally { NativeMethods.CloseHandle(hProc); }
            }
        }

        public void ResumeProcess(int id)
        {
            IntPtr hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_SUSPEND_RESUME, false, id);
            if (hProc != IntPtr.Zero)
            {
                try { NativeMethods.NtResumeProcess(hProc); }
                finally { NativeMethods.CloseHandle(hProc); }
            }
        }

        private static class NativeMethods
        {
            [DllImport("ntdll.dll", PreserveSig = false)]
            public static extern void NtSuspendProcess(IntPtr processHandle);

            [DllImport("ntdll.dll", PreserveSig = false)]
            public static extern void NtResumeProcess(IntPtr processHandle);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CloseHandle(IntPtr hObject);

            public const uint PROCESS_SUSPEND_RESUME = 0x0800;
        }
    }
}
