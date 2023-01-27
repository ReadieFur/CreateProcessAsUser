using CSharpTools.Pipes;

namespace CreateProcessAsUser.Shared
{
    public static class Properties
    {
        public const string PIPE_NAME = "create_process_as_user";
        //Originally I was using a shared project for these files.
        //However I had one odd issue where the computed buffer size of the message was different between the client and server.
        //I suspect this was due to the name of the appdomain that the shared files were being compiled into, though it was only 1 byte difference.
        public static readonly int BUFFER_SIZE = Helpers.ComputeBufferSizeOf<SMessage>();
    }
}
