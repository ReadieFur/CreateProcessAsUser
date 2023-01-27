using System;
using System.Configuration.Install;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ServiceProcess;

#nullable enable
namespace CreateProcessAsUser.Service
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new PlatformNotSupportedException("This program is only supported on Windows.");

            //Check if we are running as a service or not.
            //We should also check that no command line arguments were passed as the service has none to use (for now anyway).
            if (!Environment.UserInteractive && args.Length == 0)
            {
                RunService();
            }
            else
            {
                CommandLineHelper(args);
            }
        }

        private static void RunService()
        {
            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new Service()
            };
            ServiceBase.Run(ServicesToRun);
        }

        private static void CommandLineHelper(string[] args)
        {
            //Detect if the service is installed or not.
            bool isInstalled = ServiceController.GetServices().Any(s => s.ServiceName == AssemblyInfo.TITLE);

            if (args.Length > 0)
            {
                if (args[0] == "/install")
                {
                    if (isInstalled)
                    {
                        Console.WriteLine("The service is already installed.");
                        Environment.Exit(-1);
                    }
                    else
                    {
                        InstallService(true);
                    }
                }
                else if (args[0] == "/uninstall")
                {
                    if (!isInstalled)
                    {
                        Console.WriteLine("The service is not installed.");
                        Environment.Exit(-1);
                    }
                    else
                    {
                        InstallService(false);
                    }
                }
                else if (args[0] == "/help")
                {
                    Console.WriteLine($"Usage: {Path.GetFileName(Assembly.GetExecutingAssembly().Location)} [Argument] [Options...]"
                        + "\n\t/help"
                            + "\n\t\tShows this message."
                        + "\n\n\t/install"
                            + "\n\t\tInstalls the service. The service cannot be installed from a UNC path."
                        + "\n\n\t/uninstall"
                            + "\n\t\tUninstalls the service."
                        + "\n\n\t/interactive"
                            + "\n\t\tRuns the command line tool. If no option is passed and a UserInteractive session is detected then this option is used."
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
                                + "\n\n\tinstall"
                                    + "\n\t\tInstalls the service. The service cannot be installed from a UNC path."
                                + "\n\n\tuninstall"
                                    + "\n\t\tUninstalls the service."
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
                    case "install":
                        {
                            InstallService(true);
                            return;
                        }
                    case "uninstall":
                        {
                            InstallService(false);
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

        private static void InstallService(bool install)
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
