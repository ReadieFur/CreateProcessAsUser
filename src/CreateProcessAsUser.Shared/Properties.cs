using CSharpTools.Pipes;

namespace CreateProcessAsUser.Shared
{
    public static class Properties
    {
        public const string PIPE_NAME = "create_process_as_user";
        public static readonly int BUFFER_SIZE = Helpers.ComputeBufferSizeOf<SMessage>();
    }
}
