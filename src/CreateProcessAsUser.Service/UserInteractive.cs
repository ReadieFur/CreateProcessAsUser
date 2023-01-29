using CreateProcessAsUser.Shared;
using System;
using System.Collections.Generic;
using System.Configuration.Install;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
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
            //Possibly use reflection to check the parameter attributes and then get input at that time instead?
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

        private static void RegisterService(bool install, bool skipSecurityChecks = false)
        {
            try
            {
                //I am not sure if this will work for trusted installers (aka uers that are not in the administrators group but have the privileges).
                if (!WindowsIdentity.GetCurrent().Owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid))
                    throw new UnauthorizedAccessException("The program must be running with elevated privileges to perform this action.");

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
                    catch (Exception)
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

                    if (!skipSecurityChecks)
                    {
                        try
                        {
                            string[] verifiedGroups = new string[]
                            {
                                @"NT AUTHORITY\SYSTEM",
                                @"BUILTIN\Administrators",
                                @"NT SERVICE\TrustedInstaller"
                            };

                            //Get all of the users on the local machine.
                            string localDomain = WindowsIdentity.GetCurrent().Name.Split('\\')[0];
                            List<string> users = GetUserList(localDomain).ToList();

                            foreach (string user in users)
                            {
                                string username = $"{localDomain}\\{user}";

                                //Check if the user should be blacklisted from the further checks.
                                if (GetUserLocalGroups(username).Any(group => verifiedGroups.Any(verifiedGroup => verifiedGroup.EndsWith(group))))
                                {
                                    //This user belongs to a known group that has access to restricted folders.
                                    continue;
                                }

                                //Make sure that the user does not have write access to the path.
                                External.ACCESS_MASK userEffectiveAccess = GetEffectiveAccess(Path.GetDirectoryName(assemblyLocation), username);
                                if (userEffectiveAccess.HasFlag(External.ACCESS_MASK.FILE_ADD_FILE)
                                    || userEffectiveAccess.HasFlag(External.ACCESS_MASK.FILE_WRITE_DATA)
                                    || userEffectiveAccess.HasFlag(External.ACCESS_MASK.FILE_ADD_SUBDIRECTORY)
                                    || userEffectiveAccess.HasFlag(External.ACCESS_MASK.FILE_APPEND_DATA)
                                    || userEffectiveAccess.HasFlag(External.ACCESS_MASK.FILE_WRITE_ATTRIBUTES)
                                    || userEffectiveAccess.HasFlag(External.ACCESS_MASK.FILE_WRITE_EA)
                                    || userEffectiveAccess.HasFlag(External.ACCESS_MASK.DELETE)
                                    || userEffectiveAccess.HasFlag(External.ACCESS_MASK.FILE_DELETE_CHILD)
                                    || userEffectiveAccess.HasFlag(External.ACCESS_MASK.WRITE_DAC)
                                    || userEffectiveAccess.HasFlag(External.ACCESS_MASK.WRITE_OWNER))
                                    throw new SecurityException("The install directory is not secure.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to install: {ex.Message}");
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

        //I would've used IEnumerable here with yield return but I wasnt sure if inside of a try/finally block the finally block would always run.
        //The finally block would've been for freeing the unmanaged resources.
        //Modified from source at: https://www.pinvoke.net/default.aspx/netapi32/NetQueryDisplayInformation.html
        private static List<string> GetUserList(string domain)
        {
            const int ERROR_MORE_DATA = 234;
            const uint UF_ACCOUNTDISABLE = 0x00000002;

            IntPtr item;
            uint lread;
            uint lindex = 0;
            uint lret = ERROR_MORE_DATA;
            uint reqRecs = 100; //Number of records to get (Win2K+ only allows 100 at a time).
            List<string> userData = new();
            External.NET_DISPLAY_USER user = new();

            int StructSize = Marshal.SizeOf(typeof(External.NET_DISPLAY_USER));

            //Get user information.
            lret = ERROR_MORE_DATA;
            while (lret == ERROR_MORE_DATA)
            {
                //Get batches of 100 users.
                lret = External.NetQueryDisplayInformation(domain, 1, lindex, reqRecs, 4294967294, out lread, out IntPtr userBuffer);
                if (lread > 0)
                {
                    //Get users from buffer.
                    item = userBuffer;

                    //lread contains the number of records returned.
                    for (; lread > 0; lread--)
                    {
                        if (item != IntPtr.Zero)
                        {
                            //Convert strings and load UserInfo structure.
                            user = (External.NET_DISPLAY_USER)Marshal.PtrToStructure(item, typeof(External.NET_DISPLAY_USER));

                            //Check if the account is disabled, if it is, in this use case, don't add the user to the list.
                            if ((user.usri1_flags & UF_ACCOUNTDISABLE) != UF_ACCOUNTDISABLE)
                                userData.Add(Marshal.PtrToStringUni(user.usri1_name));
                        }

                        //Update index (used in GetInfo call to set offset for next batch).
                        lindex = user.usri1_next_index;

                        //Increment UserInfo pointer.
                        item = (IntPtr)(item.ToInt64() + StructSize);
                    }

                    //Free buffer (allocated by system).
                    if (userBuffer != IntPtr.Zero)
                        External.NetApiBufferFree(userBuffer);
                }

            }

            return userData;
        }

        //Modified from source at: https://www.pinvoke.net/default.aspx/advapi32/GetEffectiveRightsFromAcl.html
        private static External.ACCESS_MASK GetEffectiveAccess(string path, string username)
        {
            External.GetNamedSecurityInfo(path,
                External.SE_OBJECT_TYPE.SE_FILE_OBJECT,
                External.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION
                | External.SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION
                | External.SECURITY_INFORMATION.GROUP_SECURITY_INFORMATION,
                out _, out _, out _, out _, out IntPtr pSecurityDescriptor);

            if (!External.AuthzInitializeResourceManager(1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, null, out IntPtr hManager))
                throw new Exception($"Error in '{nameof(External.AuthzInitializeResourceManager)}'. Win32 error: {Marshal.GetLastWin32Error()}");

            SecurityIdentifier securityIdentifier = (SecurityIdentifier)new NTAccount(username).Translate(typeof(SecurityIdentifier));
            byte[] bytes = new byte[securityIdentifier.BinaryLength];
            securityIdentifier.GetBinaryForm(bytes, 0);
            string _psUserSid = string.Empty;
            foreach (byte si in bytes)
                _psUserSid += si;

            IntPtr UserSid = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, UserSid, bytes.Length);
            IntPtr pClientContext;

            if (!External.AuthzInitializeContextFromSid(0, UserSid, hManager, IntPtr.Zero, new(), IntPtr.Zero, out pClientContext))
                throw new Exception($"Error in '{nameof(External.AuthzInitializeContextFromSid)}'. Win32 error: {Marshal.GetLastWin32Error()}");

            External.AUTHZ_ACCESS_REQUEST request = new();
            request.DesiredAccess = 0x02000000;
            request.PrincipalSelfSid = null;
            request.ObjectTypeList = null;
            request.ObjectTypeListLength = 0;
            request.OptionalArguments = IntPtr.Zero;

            External.AUTHZ_ACCESS_REPLY reply = new();
            reply.ResultListLength = 0;
            reply.SaclEvaluationResults = IntPtr.Zero;
            reply.Error = Marshal.AllocHGlobal(1020);
            reply.GrantedAccessMask = Marshal.AllocHGlobal(sizeof(uint));
            reply.ResultListLength = 1;

            if (!External.AuthzAccessCheck(0, pClientContext, ref request, IntPtr.Zero, pSecurityDescriptor, null, 0, ref reply, out _))
                throw new Exception($"Error in '{nameof(External.AuthzAccessCheck)}'. Win32 error: {Marshal.GetLastWin32Error()}");
            External.ACCESS_MASK accessMask = (External.ACCESS_MASK)Marshal.ReadInt32(reply.GrantedAccessMask);

            Marshal.FreeHGlobal(reply.GrantedAccessMask);

            return accessMask;
        }

        //Modified from source at: https://www.pinvoke.net/default.aspx/netapi32.netusergetlocalgroups
        //I should probably try and change this out for something else as it dosen't return the group domain
        //(So presumably this ONLY gets local groups).
        private static List<string> GetUserLocalGroups(string username)
        {
            List<string> groups = new();

            string[] domainUser = username.Split('\\');
            if (External.NetUserGetLocalGroups(domainUser[0], domainUser[1], 0, 0, out IntPtr bufferPointer, -1, out int entriesRead, out _) != 0)
                throw new Exception($"Error in '{nameof(External.NetUserGetLocalGroups)}'. Win32 error: {Marshal.GetLastWin32Error()}");

            if (entriesRead > 0)
            {
                External.LOCALGROUP_USERS_INFO_0[] returnedGroups = new External.LOCALGROUP_USERS_INFO_0[entriesRead];
                for (int i = 0; i < entriesRead; i++)
                {
                    IntPtr itemPtr = bufferPointer + (Marshal.SizeOf(typeof(External.LOCALGROUP_USERS_INFO_0)) * i);
                    returnedGroups[i] = (External.LOCALGROUP_USERS_INFO_0)Marshal.PtrToStructure(itemPtr, typeof(External.LOCALGROUP_USERS_INFO_0));
                    groups.Add(returnedGroups[i].groupname);
                }
                External.NetApiBufferFree(bufferPointer);
            }

            return groups;
        }
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
        [CommandParameter("Unsafe", "Skips the security checks. It is not advised to set this flag.", required: true, takesValue: true)]
        private static void Install(Dictionary<string, string?> args)
        {
            bool unsafeFlag = false;
            if (args.ContainsKey("unsafe"))
                unsafeFlag = true;
            else if (inInteractiveMode)
                unsafeFlag = new string[] { "y", "yes", "true" }.Contains(InputValue("Skip security checks").ToLower());

            RegisterService(true, unsafeFlag);
        }

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
        [CommandParameter("Unsafe", "Allow for passing of the password on the command line. It is not advised to set this flag.")]
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
                        domain = WindowsIdentity.GetCurrent().Name.Split('\\')[0];
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
