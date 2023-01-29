//#define WAIT_FOR_DEBUGGER

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using CreateProcessAsUser.Shared;
using CSharpTools.Pipes;

#nullable enable
namespace CreateProcessAsUser.Service
{
    public partial class Service : ServiceBase
    {
        private readonly Mutex serviceMutex;

        private PipeServerManager? pipeServerManager;

        //Add a command line option to allow net users.
        public Service()
        {
            InitializeComponent();

            //The service shouldn't be able to start up multiple time but just incase it does, we'll use a mutex to prevent it.
            serviceMutex = new(true, $"service_mutex_{Properties.PIPE_NAME}", out bool createdNew);
            if (!createdNew)
            {
                //The autologger will log this exception to the event viewer and the process will terminate here.
                throw new Exception("Failed to acquire service mutex.");
            }
        }

        protected override void OnStart(string[] args)
        {
#if DEBUG && WAIT_FOR_DEBUGGER
            Task.Run(() =>
            {
                while (!Debugger.IsAttached)
                    Thread.Sleep(100);
                Debugger.Break();
#else
            //This set of braces here is just to keep the auto-indentation consistent.
            {
#endif
                PipeSecurity pipeSecurity = new();
                //Allow local users to read and write to the pipe.
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSid, null),
                    PipeAccessRights.ReadWrite, AccessControlType.Allow));
                
                if (args.Any(arg => arg.ToLower() == "/AllowNetUsers".ToLower()))
                {
                    //Allow network users to access the pipe.
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                        PipeAccessRights.ReadWrite, AccessControlType.Allow));
                }
                else
                {
                    //Deny network users access to the pipe.
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                        PipeAccessRights.FullControl, AccessControlType.Deny));
                }

                pipeServerManager = new(Properties.PIPE_NAME, Properties.BUFFER_SIZE, pipeSecurity: pipeSecurity);
                pipeServerManager.OnMessage += PipeServerManager_OnMessage;

#if DEBUG && true
                Task.Run(Testing);
#endif

#if DEBUG && WAIT_FOR_DEBUGGER
            });
#else
            }
#endif
            }

        protected override void OnStop()
        {
            pipeServerManager?.Dispose();
            pipeServerManager = null;
        }

        //TODO: Clean this method up.
        private void PipeServerManager_OnMessage(Guid id, ReadOnlyMemory<byte> data)
        {
            List<IntPtr> otherOpenHandles = new();
            IntPtr token = IntPtr.Zero;
            IntPtr enviroment = IntPtr.Zero;
            External.PROCESS_INFORMATION processInformation = new();
            SMessage formattedData = new();

            try
            {
                //TODO: Move these options to the message parameters.
                External.STARTUPINFO startInfo = new();
                startInfo.cb = Marshal.SizeOf(typeof(External.STARTUPINFO));
                startInfo.lpDesktop = "winsta0\\default";
                startInfo.wShowWindow = 1;

                try { formattedData = CSharpTools.Pipes.Helpers.Deserialize<SMessage>(data.ToArray()); }
                catch (Exception)
                {
                    formattedData = new();
                    return;
                }
                formattedData.result = new();

                if (pipeServerManager == null || pipeServerManager.IsDisposed)
                {
                    //Return as the server has ended between receiving the message and now.
                    return;
                }

                if (!pipeServerManager.PipeServers.TryGetValue(id, out PipeServer? clientPipe) || clientPipe == null)
                {
                    formattedData.result.result = EResult.UNKNOWN;
                    return;
                }

                string applicationName = formattedData.parameters.processInformation.executablePath.FromCharArray();
                string? commandLine = formattedData.parameters.processInformation.arguments.FromCharArray();
                string workingDirectory = formattedData.parameters.processInformation.workingDirectory.FromCharArray();
                if (!File.Exists(applicationName))
                {
                    formattedData.result.result = EResult.INVALID_PROCESS_INFORMATION;
                    return;
                }
                if (string.IsNullOrWhiteSpace(commandLine))
                    commandLine = null;
                if (string.IsNullOrWhiteSpace(workingDirectory))
                    workingDirectory = Path.GetDirectoryName(applicationName);

                //This step is to verify the client's credentials. Tokens are not duplicated from here.
                string clientUsername;
                switch (formattedData.parameters.authenticationMode)
                {
                    case EAuthenticationMode.INHERIT:
                        {
                            //Get the calling process's PID.
                            PipeStream pipe = (PipeStream)typeof(PipeServer)
                                .GetProperty("_pipe", BindingFlags.NonPublic | BindingFlags.Instance)!
                                .GetValue(clientPipe)!;

                            if (!External.GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint clientProcessID))
                            {
                                Debug.WriteLine(Marshal.GetLastWin32Error());
                                formattedData.result.result = EResult.FAILED_TO_GET_CALLER_PID;
                                return;
                            }

                            //Get the process information.
                            //This will throw if the client process has ended, this is fine as we will catch this later.
                            Process clientProcess = Process.GetProcessById((int)clientProcessID);

                            //Get the token.
                            IntPtr clientProcessToken = IntPtr.Zero;
                            otherOpenHandles.Add(clientProcessToken);
                            if (!External.OpenProcessToken(
                                clientProcess.Handle,
                                (int)(External.TOKEN_ACCESS.TOKEN_QUERY | External.TOKEN_ACCESS.TOKEN_DUPLICATE | External.TOKEN_ACCESS.TOKEN_IMPERSONATE),
                                out clientProcessToken))
                            {
                                Debug.WriteLine(Marshal.GetLastWin32Error());
                                formattedData.result.result = EResult.FAILED_TO_GET_TOKEN;
                                return;
                            }

                            //Get the owning user of this process.
                            //https://stackoverflow.com/questions/2686096/c-get-username-from-process
                            //https://www.pinvoke.net/default.aspx/advapi32.gettokeninformation
                            //This first call gets the size required to store the token information.
                            External.GetTokenInformation(clientProcessToken, External.TOKEN_INFORMATION_CLASS.TokenUser,
                                IntPtr.Zero, 0, out uint tokenInformationBufferSize);
                            IntPtr tokenInformationPtr = Marshal.AllocHGlobal((int)tokenInformationBufferSize);
                            otherOpenHandles.Add(tokenInformationPtr);
                            if (!External.GetTokenInformation(clientProcessToken, External.TOKEN_INFORMATION_CLASS.TokenUser,
                                tokenInformationPtr, tokenInformationBufferSize, out _))
                            {
                                Debug.WriteLine(Marshal.GetLastWin32Error());
                                formattedData.result.result = EResult.FAILED_TO_GET_TOKEN;
                                return;
                            }

                            External.TOKEN_USER tokenUser = Marshal.PtrToStructure<External.TOKEN_USER>(tokenInformationPtr);

                            IntPtr strPtr = IntPtr.Zero;
                            otherOpenHandles.Add(strPtr);
                            if (!External.ConvertSidToStringSid(tokenUser.User.Sid, out strPtr))
                            {
                                Debug.WriteLine(Marshal.GetLastWin32Error());
                                formattedData.result.result = EResult.FAILED_TO_GET_TOKEN;
                                return;
                            }

                            string sid = Marshal.PtrToStringAuto(strPtr);
                            External.LocalFree(strPtr);

                            clientUsername = new SecurityIdentifier(sid).Translate(typeof(NTAccount)).ToString();
                            break;
                        }
                    case EAuthenticationMode.CREDENTIALS:
                        {
                            if (!External.LogonUser(
                                formattedData.parameters.credentials.username.FromCharArray(),
                                formattedData.parameters.credentials.domain.FromCharArray(),
                                formattedData.parameters.credentials.password.FromCharArray(),
                                (int)External.LOGON_TYPE.LOGON32_LOGON_INTERACTIVE,
                                (int)External.LOGON_PROVIDER.LOGON32_PROVIDER_DEFAULT,
                                out _))
                            {
                                Debug.WriteLine(Marshal.GetLastWin32Error());
                                formattedData.result.result = EResult.INVALID_CREDENTIALS;
                                return;
                            }

                            clientUsername = formattedData.parameters.credentials.domain.FromCharArray() + "\\"
                                + formattedData.parameters.credentials.username.FromCharArray();
                            break;
                        }
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                //Get the active desktop sessions.
                //Modified from source at: https://github.com/murrayju/CreateProcessAsUser/blob/master/ProcessExtensions/ProcessExtensions.cs#L174-L189
                IntPtr sessionInfoPtr = IntPtr.Zero;
                otherOpenHandles.Add(sessionInfoPtr);
                int sessionCount = 0;
                List<(string, uint)> activeSessions = new();
                if (External.WTSEnumerateSessions(IntPtr.Zero, 0, 1, ref sessionInfoPtr, ref sessionCount) != 0)
                {
                    int arrayElementSize = Marshal.SizeOf(typeof(External.WTS_SESSION_INFO));
                    IntPtr currentSessionInfoPtr = sessionInfoPtr;
                    otherOpenHandles.Add(currentSessionInfoPtr);

                    for (int i = 0; i < sessionCount; i++)
                    {
                        External.WTS_SESSION_INFO currentSessionInfo = Marshal.PtrToStructure<External.WTS_SESSION_INFO>(currentSessionInfoPtr);
                        currentSessionInfoPtr += arrayElementSize;

                        if (currentSessionInfo.State != External.WTS_CONNECTSTATE_CLASS.WTSActive)
                            continue;

                        string? username = QuerySessionUsername(currentSessionInfo.SessionID);
                        if (string.IsNullOrEmpty(username))
                            continue;

                        activeSessions.Add((username!, currentSessionInfo.SessionID));
                    }
                }
                if (activeSessions.Count == 0)
                {
                    //Fall back to using an old method to get the active desktop session (not session**S**).
                    //This is more reliable according to the MS docs however it won't get all sessions.
                    uint sessionID = External.WTSGetActiveConsoleSessionId();
                    if (sessionID != 0xFFFFFFFF)
                    {
                        string? username = QuerySessionUsername(sessionID);
                        if (!string.IsNullOrEmpty(username))
                            activeSessions.Add((username!, sessionID));
                    }
                }
                if (activeSessions.Count == 0)
                {
                    formattedData.result.result = EResult.FAILED_TO_GET_DESKTOP_SESSIONS;
                    return;
                }

                //Check for an active desktop for the provided user.
                int sessionIndex = activeSessions.FindIndex(session => session.Item1.ToLower() == clientUsername.ToLower());
                if (sessionIndex == -1)
                {
                    formattedData.result.result = EResult.FAILED_TO_GET_DESKTOP_SESSIONS;
                    return;
                }

                IntPtr impersonationToken = IntPtr.Zero;
                otherOpenHandles.Add(impersonationToken);
                if (External.WTSQueryUserToken(activeSessions[sessionIndex].Item2, ref impersonationToken) == 0)
                {
                    Debug.WriteLine(Marshal.GetLastWin32Error());
                    formattedData.result.result = EResult.FAILED_TO_GET_TOKEN;
                    return;
                }

                //Convert the impersonation token to a primary token.
                if (!External.DuplicateTokenEx(
                    impersonationToken,
                    (int)(External.TOKEN_ACCESS.TOKEN_QUERY | External.TOKEN_ACCESS.TOKEN_DUPLICATE
                    | External.TOKEN_ACCESS.TOKEN_ASSIGN_PRIMARY | External.TOKEN_ACCESS.TOKEN_ADJUST_PRIVILEGES),
                    IntPtr.Zero,
                    (int)External.SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                    (int)External.TOKEN_TYPE.TokenPrimary,
                    out token))
                {
                    Debug.WriteLine(Marshal.GetLastWin32Error());
                    formattedData.result.result = EResult.FAILED_TO_GET_TOKEN;
                    return;
                }

                //Look into https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-token_information_class
                //To possibly create a process as a different user and place the process under a different user's session.
                //Or look into this https://learn.microsoft.com/en-gb/windows/win32/api/winnt/ns-winnt-security_descriptor?redirectedfrom=MSDN
                //Which may be able to set the owner of the process?

                //https://learn.microsoft.com/en-us/windows/win32/secauthz/enabling-and-disabling-privileges-in-c--
                //TODO:
                /*External.TOKEN_PRIVILEGES tp = new();
                External.LUID luid = new();
                if (External.LookupPrivilegeValue(null, "SeAssignPrimaryTokenPrivilege", ref luid))
                {
                    tp.PrivilegeCount = 1;
                    tp.Privileges = new LUID_AND_ATTRIBUTES[1];
                    tp.Privileges[0].Luid = luid;
                    tp.Privileges[0].Attributes = 0x00000002;

                    if (External.AdjustTokenPrivileges(token, false, ref tp, (uint)Marshal.SizeOf<External.TOKEN_PRIVILEGES>(), IntPtr.Zero, out _))
                    {
                        Debug.WriteLine("+");
                    }
                    else
                    {
                        Debug.WriteLine("|" + Marshal.GetLastWin32Error());
                    }
                }
                else
                {
                    Debug.WriteLine(Marshal.GetLastWin32Error());
                }*/

                //Load the users enviroment variables.
                if (!External.CreateEnvironmentBlock(ref enviroment, token, false))
                {
                    Debug.WriteLine(Marshal.GetLastWin32Error());
                    formattedData.result.result = EResult.FAILED_TO_GET_ENVIRONMENT;
                    return;
                }

                //Create the process.
                if (!External.CreateProcessAsUser(
                    token,
                    applicationName,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    (uint)(External.PROCESS_CREATION_FLAGS.CREATE_UNICODE_ENVIRONMENT | External.PROCESS_CREATION_FLAGS.CREATE_NEW_CONSOLE),
                    enviroment,
                    workingDirectory,
                    ref startInfo,
                    out processInformation))
                {
                    Debug.WriteLine(Marshal.GetLastWin32Error());
                    formattedData.result.result = EResult.FAILED_TO_CREATE_PROCESS;
                    return;
                }

                formattedData.result.processID = processInformation.dwProcessId;
                formattedData.result.result = EResult.CREATED_PROCESS;
            }
            catch (Exception)
            {
                formattedData.result.result = EResult.UNKNOWN;
            }
            finally
            {
                //Send response.
                try { pipeServerManager?.SendMessage(id, CSharpTools.Pipes.Helpers.Serialize(formattedData)); }
                catch (Exception) {}

                //Cleanup.
                if (token != IntPtr.Zero)
                    External.CloseHandle(token);
                if (enviroment != IntPtr.Zero)
                    External.DestroyEnvironmentBlock(enviroment);
                if (processInformation.hThread != IntPtr.Zero)
                    External.CloseHandle(processInformation.hThread);
                if (processInformation.hProcess != IntPtr.Zero)
                    External.CloseHandle(processInformation.hProcess);
                foreach (IntPtr handle in otherOpenHandles)
                {
                    if (handle != IntPtr.Zero)
                    {
                        try { External.CloseHandle(handle); }
                        catch (Exception) {}
                    }
                }
            }
        }

        private static string? QuerySessionUsername(uint sessionID)
        {
            //Query the session information.
            //Modified from source at: https://www.pinvoke.net/default.aspx/wtsapi32.wtsquerysessioninformation
            IntPtr buffer;
            int strLen;
            string username = string.Empty;
            if (!(External.WTSQuerySessionInformation(IntPtr.Zero, (int)sessionID, External.WTS_INFO_CLASS.WTSUserName, out buffer, out strLen) && strLen > 1))
                return null;

            //Don't need length as these are null terminated strings.
            username = Marshal.PtrToStringAnsi(buffer);
            External.WTSFreeMemory(buffer);

            if (!(External.WTSQuerySessionInformation(IntPtr.Zero, (int)sessionID, External.WTS_INFO_CLASS.WTSDomainName, out buffer, out strLen) && strLen > 1))
                return null;

            //Prepend domain name.
            username = Marshal.PtrToStringAnsi(buffer) + "\\" + username;
            External.WTSFreeMemory(buffer);

            return username;
        }

#if DEBUG
        private static void Testing()
        {
#if true
            while (!Debugger.IsAttached)
                Thread.Sleep(100);
            Debugger.Break();
#endif

            do
            {
            }
            while (false);
        }
#endif
    }
}
