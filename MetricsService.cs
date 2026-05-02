using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace MofoBar
{
    public class MetricsService
    {
        private PerformanceCounter? _cpuCounter;
        private bool _isInitialized;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        public MetricsService()
        {
            Initialize();
        }

        private void Initialize()
        {
            Task.Run(() =>
            {
                try
                {
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _cpuCounter.NextValue();
                    _isInitialized = true;
                }
                catch { }
            });
        }

        public double GetCpuUsage()
        {
            if (!_isInitialized || _cpuCounter == null) return 0;
            try { return Math.Round(_cpuCounter.NextValue(), 1); }
            catch { return 0; }
        }

        public double GetRamUsage()
        {
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                return memStatus.dwMemoryLoad;
            }
            return 0;
        }

        public object GetVitals()
        {
            return new
            {
                Cpu = GetCpuUsage(),
                Ram = GetRamUsage()
            };
        }
    }
}
