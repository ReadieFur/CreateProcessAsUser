//#define USER_DEBUG

using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using CreateProcessAsUser.Shared;
using CSharpTools.Pipes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using advapi32;
using kernel32;
using userenv;
using System.Runtime.InteropServices;

namespace CreateProcessAsUser.Service
{
    public sealed class WindowsBackgroundService : BackgroundService
    {
        private readonly ILogger<WindowsBackgroundService> logger;

        private PipeServerManager? pipeServerManager;

        /// <summary>
        /// Called once at service startup, not to be confused with <see cref="StartAsync(CancellationToken)"/>.
        /// </summary>
        /// <param name="logger">The logger that will store messages in the event viewer.</param>
        public WindowsBackgroundService(ILogger<WindowsBackgroundService> logger)
        {
#if DEBUG && USER_DEBUG
            while (!Debugger.IsAttached)
                System.Threading.Thread.Sleep(100);
            Debugger.Break();
#endif

            Mutex mutex = new(true, $"startup_mutex_{Properties.PIPE_NAME}", out bool createdNew);
            if (!createdNew)
            {
                mutex.Close();
                logger.LogError("Service already running.");
                Environment.Exit(1);
            }

            this.logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
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
            pipeServerManager.OnMessage += PipeServerManager_onMessage;

            return Task.CompletedTask;
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            pipeServerManager?.Dispose();
            pipeServerManager = null;

            return Task.CompletedTask;
        }

        private void PipeServerManager_onMessage(Guid id, ReadOnlyMemory<byte> data)
        {
            IntPtr token = IntPtr.Zero;
            IntPtr enviroment = IntPtr.Zero;
            ProcessThreadsAPI.PROCESS_INFORMATION processInformation = new ProcessThreadsAPI.PROCESS_INFORMATION();
            //TODO: Move these options to the message parameters.
            ProcessThreadsAPI.STARTUPINFO startInfo = new();
            startInfo.cb = Marshal.SizeOf(typeof(ProcessThreadsAPI.STARTUPINFO));
            startInfo.lpDesktop = "winsta0\\default";
            startInfo.wShowWindow = 1;

            SMessage formattedData;
            try { formattedData = Helpers.Deserialize<SMessage>(data.ToArray()); }
            catch
            {
                formattedData = new() { result = SResult.Default() };
                formattedData.result.result = EResult.UNKNOWN;
                goto sendResponse;
            }
            formattedData.result = SResult.Default();

            if (pipeServerManager == null || pipeServerManager.IsDisposed)
            {
                //Return as the server has ended between receiving the message and now.
                return;
            }

            switch (formattedData.parameters.authenticationMode)
            {
                case EAuthenticationMode.INHERIT:
                    {
                        //Get the calling process's PID.
                        if (!pipeServerManager.PipeServers.TryGetValue(id, out PipeServer? clientPipe) || clientPipe == null)
                        {
                            formattedData.result.result = EResult.FAILED_TO_GET_CALLER_PID;
                            goto sendResponse;
                        }

                        PipeStream pipe = (PipeStream)typeof(PipeServer)
                            .GetProperty("_pipe", BindingFlags.NonPublic | BindingFlags.Instance)!
                            .GetValue(clientPipe)!;

                        if (!WinBase.GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint clientProcessID))
                        {
                            formattedData.result.result = EResult.FAILED_TO_GET_CALLER_PID;
                            goto sendResponse;
                        }

                        //Get the process information.
                        Process clientProcess = Process.GetProcessById((int)clientProcessID);
                        if (clientProcess.HasExited)
                            return;

                        //Get the token.
                        if (!ProcessThreadsAPI.OpenProcessToken(
                            clientProcess.Handle,
                            SecurityBaseAPI.STANDARD_RIGHTS_READ | SecurityBaseAPI.TOKEN_QUERY | SecurityBaseAPI.TOKEN_DUPLICATE
                            | SecurityBaseAPI.TOKEN_ASSIGN_PRIMARY | SecurityBaseAPI.TOKEN_IMPERSONATE,
                            out IntPtr clientProcessToken))
                        {
                            formattedData.result.result = EResult.FAILED_TO_GET_TOKEN;
                            goto sendResponse;
                        }

                        //Duplicate the token to be used for the new process.
                        //if (!SecurityBaseAPI.DuplicateToken(clientProcessToken, SecurityBaseAPI.TOKEN_DUPLICATE, ref token))
                        if (!SecurityBaseAPI.DuplicateTokenEx(
                            clientProcessToken,
                            SecurityBaseAPI.TOKEN_DUPLICATE | SecurityBaseAPI.TOKEN_IMPERSONATE,
                            IntPtr.Zero,
                            SecurityBaseAPI.SECURITY_IMPERSONATION_LEVEL,
                            SecurityBaseAPI.TOKEN_PRIMARY,
                            ref token))
                        {
                            formattedData.result.result = EResult.FAILED_TO_GET_TOKEN;
                            goto sendResponse;
                        }

                        break;
                    }
                case EAuthenticationMode.CREDENTIALS:
                    {
                        if (!WinBase.LogonUser("kofre", "READIEFURPC", "Greattey03", 2, 0, out token))
                            goto sendResponse;

                        break;
                    }
                default:
                    {
                        formattedData.result.result = EResult.UNKNOWN;
                        goto sendResponse;
                    }
            }

            /*if (!UserEnv.CreateEnvironmentBlock(ref enviroment, token, false))
                goto sendResponse;*/

            //https://github.com/murrayju/CreateProcessAsUser/blob/master/ProcessExtensions/ProcessExtensions.cs
            /*if (!ProcessThreadsAPI.CreateProcessAsUser(
                token,
                //formattedData.parameters.processInformation.executablePath,
                "C:\\Windows\\System32\\notepad.exe",
                string.Empty,
                IntPtr.Zero, //TODO?: https://learn.microsoft.com/en-us/previous-versions/windows/desktop/legacy/aa379560(v=vs.85)
                IntPtr.Zero,
                false, //Don't inherit this processes handles.
                ProcessThreadsAPI.NORMAL_PRIORITY_CLASS | WinBase.CREATE_NEW_CONSOLE | WinBase.CREATE_NEW_PROCESS_GROUP,
                IntPtr.Zero,
                //formattedData.parameters.processInformation.workingDirectory,
                "C:\\Windows\\System32",
                ref startInfo,
                out processInformation))
            {
                formattedData.result.result = EResult.FAILED_TO_CREATE_PROCESS;
                goto sendResponse;
            }*/

            // Create process
            WinBase.SECURITY_ATTRIBUTES sa = new();
            sa.nLength = Marshal.SizeOf(sa);
            ProcessThreadsAPI.STARTUPINFO si = new();
            si.cb = Marshal.SizeOf(si);
            si.lpDesktop = "winsta0\\default";
            ProcessThreadsAPI.PROCESS_INFORMATION pi = new();

            /*if (!ProcessThreadsAPI.CreateProcessAsUser(token, "C:\\Windows\\System32\\notepad.exe", string.Empty,
                IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, null, ref si, out pi))
            {
                formattedData.result.result = EResult.FAILED_TO_CREATE_PROCESS;
                goto sendResponse;
            }*/

            if (!ProcessThreadsAPI.CreateProcessWithLogonW("kofre", "READIEFURPC", "Greattey03", 0x00000001,
                "C:\\Windows\\System32\\notepad.exe", "", 0, IntPtr.Zero, null, ref si, out pi))
            {
                formattedData.result.result = EResult.FAILED_TO_CREATE_PROCESS;
                goto sendResponse;
            }

        sendResponse:
            if (token != IntPtr.Zero)
                HandleAPI.CloseHandle(token);
            if (enviroment != IntPtr.Zero)
                UserEnv.DestroyEnvironmentBlock(enviroment);
            if (processInformation.hThread != IntPtr.Zero)
                HandleAPI.CloseHandle(processInformation.hThread);
            if (processInformation.hProcess != IntPtr.Zero)
                HandleAPI.CloseHandle(processInformation.hProcess);

            try { pipeServerManager?.SendMessage(id, Helpers.Serialize(formattedData)); }
            catch {}
        }
    }
}
