using System;
using System.Runtime.InteropServices;

namespace CreateProcessAsUser.Shared
{
    [Serializable]
    public struct SParameters
    {
        /// <summary>
        /// The authentication mode to use.
        /// </summary>
        public EAuthenticationMode authenticationMode;

        /// <summary>
        /// Whether or not to run the new process with elevated privileges.
        /// </summary>
        public bool elevated;

        /// <summary>
        /// The string credentials to use for authentication.
        /// <para>This is only used if <see cref="authenticationMode"/> is set to <see cref="EAuthenticationMode.CREDENTIALS"/>.</para>
        /// </summary>
        public SCredentials credentials;
    }
}
