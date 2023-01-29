using System;
using System.Configuration.Install;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
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

            //Check if we are running as a service (the parent process should be svchost.exe).
            bool runningAsService = RunningAsService(); //Default to assume we are running as a service.

            if (runningAsService)
                RunService();
            else
                CommandLineHelper(args);
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

        private static void CommandLineHelper(string[] args)
        {
            //Detect if the service is registered or not.
            bool isRegistered = ServiceController.GetServices().Any(s => s.ServiceName == AssemblyInfo.TITLE);

            if (args.Length > 0)
            {
                if (args[0] == "/register")
                {
                    if (isRegistered)
                    {
                        Console.WriteLine("The service is already registered.");
                        Environment.Exit(-1);
                    }
                    else
                    {
                        RegisterService(true);
                    }
                }
                else if (args[0] == "/unregister")
                {
                    if (!isRegistered)
                    {
                        Console.WriteLine("The service is not registered.");
                        Environment.Exit(-1);
                    }
                    else
                    {
                        RegisterService(false);
                    }
                }
                else if (args[0] == "/help")
                {
                    Console.WriteLine($"Usage: {Path.GetFileName(Assembly.GetExecutingAssembly().Location)} [Argument] [Options...]"
                        + "\n\t/help"
                            + "\n\t\tShows this message."
                        + "\n\n\t/register"
                            + "\n\t\tRegisters the service. The service cannot be registered from a UNC path."
                        + "\n\n\t/unregister"
                            + "\n\t\tUnregisters the service."
                    );
                    return;
                }
            }

            //This is only reached if none of the argument checks were passed above.
            RunInteractiveMode();
        }

        private static void RunInteractiveMode()
        {
            Console.WriteLine("Interactive Mode (type help for options):");
            while (true)
            {
                Console.Write("> ");
                switch (Console.ReadLine().ToLower())
                {
                    case "help":
                        {
                            Console.WriteLine("Help:"
                                + "\n\thelp"
                                    + "\n\t\tShows this message."
                                + "\n\n\tregister"
                                    + "\n\t\tRegisters the service. The service cannot be registered from a UNC path."
                                + "\n\n\tunregister"
                                    + "\n\t\tUnregisters the service."
                                + "\n\n\tlaunch"
                                    + "\n\t\tRuns the interactive process launcher."
                                + "\n\texit"
                                    + "\n\t\tExits the program."
                            );
                            break;
                        }
                    case "exit":
                        {
                            return;
                        }
                    case "register":
                        {
                            RegisterService(true);
                            return;
                        }
                    case "unregister":
                        {
                            RegisterService(false);
                            return;
                        }
                    case "launch":
                        {
                            RunInteractiveProcessLauncher();
                            return;
                        }
                    default:
                        {
                            Console.WriteLine("Invalid argument. Please type help inside of interactive mode or pass /help to the command line to see the options.");
                            break;
                        }
                }
            }
        }

        //TODO:
        //Modify this (possibly with an overload) that handles the command line as well as interactive interactions
        //Passwords shoulkd not be sent through the command line, though can optionally be if really desired.
        private static void RunInteractiveProcessLauncher()
        {
            throw new NotImplementedException();
        }

        private static void RegisterService(bool install, bool ignoreUnsecureDirectory = false)
        {
            try
            {
                string assemblyLocation = Assembly.GetExecutingAssembly().Location;

                if (install)
                {
                    #region UNC path check
                    //I have decided to run the path checks here instead of inside the ServiceInstaller class.
                    //Incase the user wants do do a custom install using a different tool like InstallUtil.exe or sc.exe.
                    //So only my in-app installer will run these checks.
                    //The reason for doing this is that by default, in Windows a service cannot be run from a network share. You will get error code 2.

                    bool isOnNetworkPath = false;
                    try { isOnNetworkPath = new DriveInfo(Path.GetPathRoot(assemblyLocation)).DriveType == DriveType.Network; }
                    catch
                    {
                        try { isOnNetworkPath = new Uri(assemblyLocation).IsUnc; }
                        catch { /*Assume we are not on a network path (use default value: false).*/ }
                    }

                    if (isOnNetworkPath)
                    {
                        Console.WriteLine("This service cannot be installed on a UNC path.");
                        Environment.Exit(-1);
                    }
                    #endregion

                    #region Secure path check
                    //Because this program will be run with system privileges,
                    //It is HIGHLY reccommended that it is installed under a secure directory,
                    //So it cannot easily be replaced with some other malicious software.

                    if (!ignoreUnsecureDirectory)
                    {
                        try
                        {
                            //TODO.
                        }
                        catch
                        {
                            Console.WriteLine("Couldn't determine the security of this folder, skipping install.");
                            Environment.Exit(-1);
                        }
                    }
                    #endregion
                }
                else
                {
                    //Stop the service.
                    ServiceController service = new(AssemblyInfo.TITLE);
                    if (service.Status != ServiceControllerStatus.Stopped)
                        service.Stop();
                }

                ManagedInstallerClass.InstallHelper(new string[]
                {
                    install ? "/i" : "/u",
                    assemblyLocation
                });

                if (install)
                {
                    //Start the service.
                    ServiceController service = new(AssemblyInfo.TITLE);
                    if (service.Status == ServiceControllerStatus.Stopped)
                        service.Start();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to " + (install ? "" : "un") + $"install the service:\n{ex.Message}");
            }
        }
    }
}
