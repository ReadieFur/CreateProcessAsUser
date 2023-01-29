#define WAIT_FOR_DEBUGGER

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
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
                //Deny network users access to the pipe.
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                    PipeAccessRights.FullControl, AccessControlType.Deny));

                pipeServerManager = new(Properties.PIPE_NAME, Properties.BUFFER_SIZE, pipeSecurity: pipeSecurity);
                pipeServerManager.OnMessage += PipeServerManager_OnMessage;

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

            //TODO: Move these options to the message parameters.
            External.STARTUPINFO startInfo = new();
            startInfo.cb = Marshal.SizeOf(typeof(External.STARTUPINFO));
            startInfo.lpDesktop = "winsta0\\default";
            startInfo.wShowWindow = 1;

            SMessage formattedData;
            try { formattedData = CSharpTools.Pipes.Helpers.Deserialize<SMessage>(data.ToArray()); }
            catch
            {
                formattedData = new();
                goto sendResponse;
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
                goto sendResponse;
            }

            string applicationName = formattedData.parameters.processInformation.executablePath.FromCharArray();
            string? commandLine = formattedData.parameters.processInformation.arguments.FromCharArray();
            string workingDirectory = formattedData.parameters.processInformation.workingDirectory.FromCharArray();
            if (!File.Exists(applicationName))
            {
                formattedData.result.result = EResult.INVALID_PROCESS_INFORMATION;
                goto cleanup;
            }
            if (string.IsNullOrWhiteSpace(commandLine))
                commandLine = null;
            if (string.IsNullOrWhiteSpace(workingDirectory))
                workingDirectory = Path.GetDirectoryName(applicationName);

            switch (formattedData.parameters.authenticationMode)
            {
                case EAuthenticationMode.INHERIT:
                    //Get the calling process's PID.
                    PipeStream pipe = (PipeStream)typeof(PipeServer)
                        .GetProperty("_pipe", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .GetValue(clientPipe)!;

                    if (!External.GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint clientProcessID))
                    {
                        Debug.WriteLine(Marshal.GetLastWin32Error());
                        formattedData.result.result = EResult.FAILED_TO_GET_CALLER_PID;
                        goto sendResponse;
                    }

                    //Get the process information.
                    Process clientProcess;
                    try { clientProcess = Process.GetProcessById((int)clientProcessID); }
                    catch (Exception) { return; }
                    if (clientProcess.HasExited)
                        return;

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
                        goto sendResponse;
                    }

                    //Duplicate the token to be used for the new process.
                    if (!External.DuplicateTokenEx(
                        clientProcessToken,
                        (int)(External.TOKEN_ACCESS.TOKEN_QUERY | External.TOKEN_ACCESS.TOKEN_DUPLICATE | External.TOKEN_ACCESS.TOKEN_ASSIGN_PRIMARY),
                        IntPtr.Zero,
                        (int)External.SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                        (int)External.TOKEN_TYPE.TokenPrimary,
                        out token))
                    {
                        Debug.WriteLine(Marshal.GetLastWin32Error());
                        formattedData.result.result = EResult.FAILED_TO_GET_TOKEN;
                        goto sendResponse;
                    }
                    break;
                case EAuthenticationMode.CREDENTIALS:
                    if (!External.LogonUser(
                        formattedData.parameters.credentials.username.FromCharArray(),
                        formattedData.parameters.credentials.domain.FromCharArray(),
                        formattedData.parameters.credentials.password.FromCharArray(),
                        (int)External.LOGON_TYPE.LOGON32_LOGON_INTERACTIVE,
                        (int)External.LOGON_PROVIDER.LOGON32_PROVIDER_DEFAULT,
                        out token))
                    {
                        Debug.WriteLine(Marshal.GetLastWin32Error());
                        formattedData.result.result = EResult.INVALID_CREDENTIALS;
                        goto sendResponse;
                    }
                    break;
                default:
                    formattedData.result.result = EResult.UNKNOWN;
                    goto sendResponse;
            }

            //Load the users enviroment variables.
            if (!External.CreateEnvironmentBlock(ref enviroment, token, false))
            {
                Debug.WriteLine(Marshal.GetLastWin32Error());
                formattedData.result.result = EResult.FAILED_TO_GET_ENVIRONMENT;
                goto sendResponse;
            }

            if (pipeServerManager == null || pipeServerManager.IsDisposed || !clientPipe.IsConnected || clientPipe.IsDisposed)
            {
                //Return as the server has ended between receiving the message and now.
                goto cleanup;
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
                goto sendResponse;
            }

            formattedData.result.processID = processInformation.dwProcessId;
            formattedData.result.result = EResult.CREATED_PROCESS;

        sendResponse:
            try { pipeServerManager?.SendMessage(id, CSharpTools.Pipes.Helpers.Serialize(formattedData)); }
            catch (Exception) {}

        cleanup:

            if (token != IntPtr.Zero)
                External.CloseHandle(token);
            if (enviroment != IntPtr.Zero)
                External.DestroyEnvironmentBlock(enviroment);
            if (processInformation.hThread != IntPtr.Zero)
                External.CloseHandle(processInformation.hThread);
            if (processInformation.hProcess != IntPtr.Zero)
                External.CloseHandle(processInformation.hProcess);
            foreach (IntPtr handle in otherOpenHandles)
                if (handle != IntPtr.Zero)
                    External.CloseHandle(handle);
        }
    }
}
