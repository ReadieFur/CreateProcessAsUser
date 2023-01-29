using CreateProcessAsUser.Shared;
using System;
using System.Collections.Generic;
using System.Configuration.Install;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;

#nullable enable
namespace CreateProcessAsUser.Service
{
    public static class UserInteractive
    {
        private static readonly List<(CommandAttribute, MethodInfo)> COMMANDS = CommandAttribute.GetCommands(typeof(UserInteractive));
        private static bool inInteractiveMode = false;
        private static string inputPrefix = "> ";

        public static void Run(string[] args)
        {
            //Check if we should enter user interactive mode or parse the command line.
            if (inInteractiveMode = args.Length == 0)
                RunInteractiveMode();
            else
                RunCommandLineOptionsMode(args);
        }

        #region Internal methods
        private static void RunCommandLineOptionsMode(string[] args)
        {
            Dictionary<string, string?> parsedArguments = args.Select(arg =>
            {
                string key = arg;
                string? value = null;

                int splitLocation = arg.IndexOf('=');
                if (splitLocation != -1)
                {
                    key = arg.Substring(0, splitLocation).ToLower();
                    value = arg.Substring(splitLocation + 1);
                }

                return new KeyValuePair<string, string?>(key, value);
            }).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            //The first argument is always the first option that dosen't start with '/'.
            string desiredCommand = parsedArguments.First(arg => !arg.Key.StartsWith("/")).Key;

            //Check if there is a matching command.
            int commandIndex = COMMANDS.FindIndex(command => command.Item1.Name.ToLower() == desiredCommand);
            if (commandIndex == -1)
            {
                Console.WriteLine("Invalid command. Please type 'help' inside of interactive mode or pass 'help' to the command line to see the options.");
                Environment.Exit(-1);
            }

            //Run the command.
            inputPrefix = $"{COMMANDS[commandIndex].Item2.Name}> ";
            List<object> methodParameters = new();
            if (COMMANDS[commandIndex].Item2.GetParameters().Length == 1
                && COMMANDS[commandIndex].Item2.GetParameters()[0].ParameterType == typeof(Dictionary<string, string?>))
                    methodParameters.Add(parsedArguments);
            COMMANDS[commandIndex].Item2.Invoke(null, methodParameters.ToArray());
        }

        private static void RunInteractiveMode()
        {
            Console.WriteLine("Interactive Mode (type 'help' for options):");
            while (true)
            {
                inputPrefix = "> ";
                Console.Write(inputPrefix);
                string userInput = Console.ReadLine();

                //Check if there is a matching command.
                int commandIndex = COMMANDS.FindIndex(command => command.Item1.Name.ToLower() == userInput);
                if (commandIndex == -1)
                {
                    Console.WriteLine("Invalid command. Please type 'help' to see the options.");
                    continue;
                }

                //Run the command.
                inputPrefix = $"{COMMANDS[commandIndex].Item2.Name}> ";
                List<object> methodParameters = new();
                if (COMMANDS[commandIndex].Item2.GetParameters().Length == 1
                    && COMMANDS[commandIndex].Item2.GetParameters()[0].ParameterType == typeof(Dictionary<string, string?>))
                    methodParameters.Add(new Dictionary<string, string?>());
                COMMANDS[commandIndex].Item2.Invoke(null, methodParameters.ToArray());
            }
        }

        private static void RegisterService(bool install, bool ignoreUnsecureDirectory = false)
        {
            try
            {
                string assemblyLocation = Assembly.GetExecutingAssembly().Location;

                if (install)
                {
                    #region Registered check
                    if (IsServiceRegistered())
                    {
                        Console.WriteLine("The service is already registered.");
                        Environment.Exit(-1);
                    }
                    #endregion

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
                        catch (Exception) { /*Assume we are not on a network path (use default value: false).*/ }
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
                    #region Registered check
                    if (!IsServiceRegistered())
                    {
                        Console.WriteLine("The service is not registered.");
                        Environment.Exit(-1);
                    }
                    #endregion

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

        private static bool IsServiceRegistered() =>
            ServiceController.GetServices().Any(s => s.ServiceName == AssemblyInfo.TITLE);

        private static string InputValue(string valueName)
        {
            Console.WriteLine($"{valueName}:");
            Console.Write(inputPrefix);
            return Console.ReadLine();
        }

        //Check if the args contains a valid value first. If not then check if we can are allowed to get the value from the user via interactive mode.
        private static bool TryGetValue(string keyName, bool allowInteraction, Dictionary<string, string?> args, Func<string, bool> predicament) =>
            (args.ContainsKey($"/{keyName.ToLower()}") && predicament(args[$"/{keyName.ToLower()}"]!))
            || (allowInteraction && predicament(InputValue(keyName)));
        #endregion

        #region Commands
        [Command("Shows a list of commands and their options.")]
        private static void Help()
        {
            string helpMessage = string.Empty;

            helpMessage += $"Usage: {Path.GetFileName(Assembly.GetExecutingAssembly().Location)} [Command] [/Option<=value>...]";

            foreach ((CommandAttribute, MethodInfo) command in COMMANDS)
            {
                helpMessage += $"\n\t{command.Item1.Name}"
                    + $"\n\t\t{command.Item1.Description}";

                foreach (CommandParameterAttribute parameter in command.Item2.GetCustomAttributes<CommandParameterAttribute>())
                {
                    helpMessage += "\n\t\t\t"
                        + $"/{parameter.Name}"
                        + (parameter.TakesValue ? "=<value>" : string.Empty)
                        + " "
                        + "(" + (parameter.Required ? "Required" : "Optional") + ")"
                        + string.Join("", parameter.Description.Split('\n').Select(line => $"\n\t\t\t\t{line}"));
                }
            }

            Console.WriteLine(helpMessage);
        }

        [Command("Registers the service. The service cannot be registered from a UNC path. Requires elevated permissions.")]
        private static void Install() => RegisterService(true);

        [Command("Unregisters the service. Requires elevated permissions.")]
        private static void Uninstall() => RegisterService(false);

        [Command("Runs the process launcher.")]
        [CommandParameter("AuthenticationMode",
            "The authentication mode to use. Allowed values are:"
            + "\nInherit - Use the account and privileges that this process is running under."
            + "\nCredentials - Use the provided user credentials. If this mode is used an interactive session is launched to take the credentials, unless the '/unsafe' flag is passed.",
            required: true, takesValue: true)]
        [CommandParameter("ExecutablePath", "The path to the executable.", required: true, takesValue: true)]
        [CommandParameter("Arguments", "The arguments to pass to the executable.", takesValue: true)]
        [CommandParameter("WorkingDirectory", "The working directory to use.", takesValue: true)]
        [CommandParameter("Domain", "The domain that the account exists under.", takesValue: true)]
        [CommandParameter("Username", "The username of the account to use.", takesValue: true)]
        [CommandParameter("Unsafe", "Allow for passing of the password on the command line, this is not advised.")]
        [CommandParameter("Password", "The password of the account to use.", takesValue: true)]
        private static void Launch(Dictionary<string, string?> args)
        {
            //Check if the user has passed a password onto the command line here so that we can terminate asap if the unsafe flag has not been set.
            if (!inInteractiveMode && args.ContainsKey("/password") && !args.ContainsKey("/unsafe"))
            {
                Console.WriteLine("Aborting due to a password being passed without the 'unsafe' flag set.");
                return;
            }

            //Default values will be set here in order to satisfy the compiler.

            EAuthenticationMode authenticationMode = EAuthenticationMode.INHERIT;
            if (!TryGetValue("AuthenticationMode", true, args, str => Enum.TryParse(str, true, out authenticationMode)))
            {
                Console.WriteLine("Invalid value for AuthenticationMode");
                return;
            }

            string executablePath = string.Empty;
            if (!TryGetValue("ExecutablePath", true, args, str =>
            {
                if (!File.Exists(str))
                    return false;
                executablePath = str;
                return true;
            }))
            {
                Console.WriteLine("Invalid value for ExecutablePath");
                return;
            }

            string arguments = string.Empty;
            if (args.ContainsKey("arguments"))
                arguments = args["/arguments"] ?? string.Empty;
            else if (inInteractiveMode)
                arguments = InputValue("Arguments");

            string workingDirectory = string.Empty;
            bool pathWasChecked = false;
            if (!TryGetValue("WorkingDirectory", inInteractiveMode, args, str =>
            {
                if (string.IsNullOrEmpty(str))
                    return true;

                if (Directory.Exists(str))
                {
                    workingDirectory = str;
                    return true;
                }
                pathWasChecked = true;
                return false;
            }))
            {
                //If the path wasn't checked because no value was found then we can leave it empty as it is an optional value.
                //Otherwise the value was invalid.
                if (pathWasChecked)
                {
                    Console.WriteLine("Invalid value for WorkingDirectory");
                    return;
                }
            }

            string domain = string.Empty;
            string username = string.Empty;
            string password = string.Empty;
            if (authenticationMode == EAuthenticationMode.CREDENTIALS)
            {
                TryGetValue("Domain", true, args, str =>
                {
                    if (string.IsNullOrEmpty(str))
                    {
                        //Use the local domain.
                        domain = System.Security.Principal.WindowsIdentity.GetCurrent().Name.Split('\\')[0];
                    }
                    domain = str;
                    return true;
                });

                if (!TryGetValue("Username", true, args, str =>
                {
                    if (string.IsNullOrEmpty(str))
                        return false;
                    username = str;
                    return true;
                }))
                {
                    Console.WriteLine("Invalid value for Username");
                    return;
                }

                if (!TryGetValue("Password", true, args, str =>
                {
                    if (string.IsNullOrEmpty(str))
                        return false;
                    password = str;
                    return true;
                }))
                {
                    Console.WriteLine("Invalid value for Password");
                    return;
                }
            }

            SParameters parameters = new()
            {
                authenticationMode = authenticationMode,
                credentials = new()
                {
                    //These values are ignored if the authenticationMode is not set to CREDENTIALS.
                    domain = domain.ToCharArray(),
                    username = username.ToCharArray(),
                    password = password.ToCharArray()
                },
                processInformation = new()
                {
                    executablePath = executablePath.ToCharArray(),
                    arguments = arguments.ToCharArray(),
                    workingDirectory = workingDirectory.ToCharArray()
                }
            };

            SResult result = new();
            try
            {
                //Run the method synchronously (fine for an application as simple as this (also because I couldn't be bothered to go back and add async support).
                result = Client.Helper.CreateProcessAsUser(parameters, TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (System.TimeoutException)
            {
                Console.WriteLine("The service didn't respond in a timely manner.");
                return;
            }

            if (result.result != EResult.CREATED_PROCESS)
            {
                Console.WriteLine($"Failed to create the process: {result.result}");
                return;
            }

            Console.WriteLine($"Successfully created the process. PID: {result.processID}");
        }

        [Command("Exits the program.")]
        private static void Exit() => Environment.Exit(0);
        #endregion
    }
}
