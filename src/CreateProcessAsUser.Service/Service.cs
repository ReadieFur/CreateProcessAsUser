//#define WAIT_FOR_DEBUGGER

using CreateProcessAsUser.Shared;
using CSharpTools.Pipes;
using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

#nullable enable
namespace CreateProcessAsUser.Service
{
    public partial class Service : ServiceBase
    {
        private PipeServerManager? pipeServerManager;

        public Service()
        {
            InitializeComponent();
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

        private void PipeServerManager_OnMessage(Guid id, ReadOnlyMemory<byte> data)
        {
            SMessage formattedData;
            try { formattedData = Helpers.Deserialize<SMessage>(data.ToArray()); }
            catch
            {
                formattedData = new() { result = SResult.Default() };
                formattedData.result.result = EResult.UNKNOWN;
                goto sendResponse;
            }
            formattedData.result = SResult.Default();

        sendResponse:
            try { pipeServerManager?.SendMessage(id, Helpers.Serialize(formattedData)); }
            catch {}
        }
    }
}
