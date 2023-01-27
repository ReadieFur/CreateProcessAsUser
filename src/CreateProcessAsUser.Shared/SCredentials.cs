using System;
using System.Runtime.Serialization;

namespace CreateProcessAsUser.Shared
{
    [Serializable]
    public struct SCredentials : ISerializable
    {
        /// <summary>
        /// The domain that the account exists under.
        /// <para>The size of the array should be no larger than 255 characters.</para>
        /// </summary>
        [SerializedArraySize(255)]
        public Char[] domain = new Char[255];
        private UInt16 domain_size = 0;

        /// <summary>
        /// The username of the account to use.
        /// <para>The size of the array should be no larger than 20 characters.</para>
        /// </summary>
        //https://serverfault.com/questions/105142/windows-server-2008-r2-change-the-maximum-username-length
        [SerializedArraySize(20)]
        public Char[] username = new Char[20];
        private UInt16 username_size = 0;

        /// <summary>
        /// The password of the account to use.
        /// <para>The size of the array should be no larger than 256 characters.</para>
        /// </summary>
        //https://learn.microsoft.com/en-us/answers/questions/39987/what-is-max-password-length-allowed-in-windows-201
        [SerializedArraySize(256)]
        public Char[] password = new Char[256];
        private UInt16 password_size = 0;

        public SCredentials() {}

        public SCredentials(SerializationInfo info, StreamingContext context) =>
            this.FromCustomSerializedData(ref info);

        public void GetObjectData(SerializationInfo info, StreamingContext context) =>
            this.ToCustomSerializedData(ref info);
    }
}
