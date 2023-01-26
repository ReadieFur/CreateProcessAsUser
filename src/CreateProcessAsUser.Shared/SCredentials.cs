using System;
using System.Runtime.InteropServices;

namespace CreateProcessAsUser.Shared
{
    [Serializable]
    public struct SCredentials
    {
        /// <summary>
        /// The domain that the account exists under.
        /// </summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 255)]
        public string userDomain;

        /// <summary>
        /// The username of the account to use.
        /// </summary>
        //https://serverfault.com/questions/105142/windows-server-2008-r2-change-the-maximum-username-length
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
        public string username;

        /// <summary>
        /// The password of the account to use.
        /// </summary>
        //https://learn.microsoft.com/en-us/answers/questions/39987/what-is-max-password-length-allowed-in-windows-201
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string password;
    }
}
