using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CreateProcessAsUser.Shared;
using CSharpTools.Pipes;

namespace CreateProcessAsUser.Client
{
    public static class Helper
    {
        public static async Task<SResult> CreateProcessAsUser(SParameters parameters, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            if (timeout == null)
                timeout = TimeSpan.FromMilliseconds(-1);

            SMessage message = new() { parameters = parameters };
            SResult result = SResult.Default();

            ManualResetEventSlim continuationEvent = new(false);
            PipeClient pipeClient = new(Properties.PIPE_NAME, Properties.BUFFER_SIZE);
            
            pipeClient.OnConnect += () => continuationEvent.Set();
            await Task.Run(() => continuationEvent.Wait(timeout.Value, cancellationToken));
            continuationEvent.Reset();
            if (!pipeClient.IsConnected)
            {
                result.result = EResult.TIMED_OUT;
                goto cleanup;
            }

            pipeClient.OnMessage += (data) =>
            {
                result = MessageHelpers.Deserialize(data).result;
                continuationEvent.Set();
            };
            pipeClient.SendMessage(MessageHelpers.Serialize(message));
            await Task.Run(() => continuationEvent.Wait(timeout.Value, cancellationToken));
            if (!continuationEvent.IsSet)
            {
                //If the continuation event is not set then the server has not responded in time.
                result.result = EResult.TIMED_OUT;
                goto cleanup;
            }

        cleanup:
            pipeClient.Dispose();
            continuationEvent.Dispose();
            return result;
        }
    }
}
