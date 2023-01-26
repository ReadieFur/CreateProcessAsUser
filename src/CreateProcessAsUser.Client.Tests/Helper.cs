using System;
using System.Threading;
using System.Threading.Tasks;
using CreateProcessAsUser.Shared;
using CSharpTools.Pipes;

namespace CreateProcessAsUser.Client
{
    public static class Helper
    {
        public static async Task<SResult> CreateProcessAsUser(SParameters parameters, TimeSpan timeout = default, CancellationToken cancellationToken = default)
        {
            if (timeout == default)
                timeout = TimeSpan.FromMilliseconds(-1);

            SMessage message = new()
            {
                parameters = parameters
            };
            SResult result = SResult.Default();

            ManualResetEventSlim continuationEvent = new(false);
            PipeClient pipeClient = new(Properties.PIPE_NAME, Properties.BUFFER_SIZE);

            pipeClient.onConnect += () => continuationEvent.Set();
            await Task.Run(() => continuationEvent.Wait(timeout, cancellationToken));
            if (!pipeClient.isConnected)
                goto cleanup;
            continuationEvent.Reset();

            pipeClient.onMessage += (data) =>
            {
                result = Helpers.Deserialize<SMessage>(data.ToArray()).result;
                continuationEvent.Set();
            };
            pipeClient.SendMessage(Helpers.Serialize(parameters));
            await Task.Run(() => continuationEvent.Wait(timeout, cancellationToken));

        cleanup:
            pipeClient.Dispose();
            return result;
        }
    }
}
