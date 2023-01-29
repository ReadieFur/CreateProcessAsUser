using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;

#nullable enable
namespace CreateProcessAsUser.Service
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new PlatformNotSupportedException("This program is only supported on Windows.");

            if (RunningAsService())
                RunService();
            else
                UserInteractive.Run(args);
        }

        private static void RunService()
        {
            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                //Arguments passed to the service are injected by ServiceBase.
                new Service()
            };
            ServiceBase.Run(ServicesToRun);
        }

        private static bool RunningAsService()
        {
            const bool DEFAULT_RETURN_VALUE = true; //Default to assume we are running as a service.

            //https://www.codeproject.com/Articles/9893/Get-Parent-Process-PID
            External.PROCESSENTRY32 procentry = new();

            IntPtr snapshot = External.CreateToolhelp32Snapshot(External.SnapshotFlags.Process, 0);
            if (snapshot == new IntPtr(-1))
            {
                Debug.WriteLine($"Invalid snapshot. {Marshal.GetLastWin32Error()}");
                return DEFAULT_RETURN_VALUE;
            }

            uint procentrySize = (uint)Marshal.SizeOf<External.PROCESSENTRY32>();
            procentry.dwSize = procentrySize;
            bool cont = External.Process32First(snapshot, ref procentry);
            uint parentPID = 0;
            uint currentProcessID = (uint)Process.GetCurrentProcess().Id;
            while (cont)
            {
                if (currentProcessID == procentry.th32ProcessID)
                    parentPID = procentry.th32ParentProcessID;

                procentry.dwSize = procentrySize;
                cont = External.Process32Next(snapshot, ref procentry);
            }

            //https://stackoverflow.com/questions/1933113/c-windows-how-to-get-process-path-from-its-pid
            IntPtr parentHandle = External.OpenProcess(
                (int)(External.ProcessAccessFlags.QueryInformation | External.ProcessAccessFlags.VirtualMemoryRead),
                false, parentPID);
            if (parentHandle == IntPtr.Zero)
            {
                Debug.WriteLine($"Failed to get parent process handle. {Marshal.GetLastWin32Error()}");
                return DEFAULT_RETURN_VALUE;
            }

            StringBuilder parentProcessPath = new(260);
            if (External.GetModuleFileNameEx(parentHandle, IntPtr.Zero, parentProcessPath, parentProcessPath.Capacity) == 0)
            {
                Debug.WriteLine($"Failed to get the parent process path. {Marshal.GetLastWin32Error()}");
                return DEFAULT_RETURN_VALUE;
            }

            return parentProcessPath.ToString() == Path.Combine(Environment.SystemDirectory, "services.exe");
        }
    }
}
