using System;
using System.Runtime.InteropServices;

namespace CreateProcessAsUser.Shared
{
    [Serializable]
    public struct SProcessInformation
    {
        /// <summary>
        /// The path to the executable.
        /// </summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public String executablePath;

        /// <summary>
        /// The arguments to pass to the executable.
        /// </summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public String arguments;

        /// <summary>
        /// The working directory to use.
        /// </summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public String workingDirectory;

        /// <summary>
        /// The environment variables to use.
        /// </summary>
        //public Dictionary<string, string> environmentVariables;
    }
}
