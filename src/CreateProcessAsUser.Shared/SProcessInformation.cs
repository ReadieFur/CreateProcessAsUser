using System;
using System.Runtime.Serialization;

namespace CreateProcessAsUser.Shared
{
    [Serializable]
    public struct SProcessInformation : ISerializable
    {
        /// <summary>
        /// The path to the executable.
        /// <para>The size of the array should be no larger than 260 characters.</para>
        /// </summary>
        [SerializedArraySize(260)]
        public Char[] executablePath = new Char[260];
        private UInt16 executablePath_size = 0;

        /// <summary>
        /// The arguments to pass to the executable.
        /// <para>The size of the array should be no larger than 260 characters.</para>
        /// </summary>
        [SerializedArraySize(260)]
        public Char[] arguments = new Char[260];
        private UInt16 arguments_size = 0;

        /// <summary>
        /// The working directory to use.
        /// <para>The size of the array should be no larger than 260 characters.</para>
        /// </summary>
        [SerializedArraySize(260)]
        public Char[] workingDirectory = new Char[260];
        private UInt16 workingDirectory_size = 0;

        /// <summary>
        /// The environment variables to use.
        /// </summary>
        //public Dictionary<string, string> environmentVariables;

        public SProcessInformation() {}

        public SProcessInformation(SerializationInfo info, StreamingContext context) =>
            this.FromCustomSerializedData(ref info);

        public void GetObjectData(SerializationInfo info, StreamingContext context) =>
            this.ToCustomSerializedData(ref info);
    }
}
