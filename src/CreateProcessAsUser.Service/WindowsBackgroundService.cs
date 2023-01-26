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
            SMessage formattedData = MessageHelpers.Deserialize(data);
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

                        if (!kernel32.WinBase.GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint clientPID))
                        {
                            formattedData.result.result = EResult.FAILED_TO_GET_CALLER_PID;
                            goto sendResponse;
                        }

                        //Get the process information.
                        Process callingProcess = Process.GetProcessById((int)clientPID);
                        break;
                    }
                case EAuthenticationMode.CREDENTIALS:
                    {
                        break;
                    }
                case EAuthenticationMode.TOKEN:
                    {
                        break;
                    }
            }

        sendResponse:
            try { pipeServerManager?.SendMessage(id, MessageHelpers.Serialize(formattedData)); }
            catch {}
        }
    }
}
