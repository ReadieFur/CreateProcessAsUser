﻿using System;

namespace CreateProcessAsUser.Shared
{
    [Serializable]
    public struct SParameters
    {
        /// <summary>
        /// The authentication mode to use.
        /// </summary>
        public EAuthenticationMode authenticationMode = EAuthenticationMode.INHERIT;

        /// <summary>
        /// Whether or not to run the new process with elevated privileges.
        /// </summary>
        public Boolean elevated = false;

        /// <summary>
        /// The string credentials to use for authentication.
        /// <para>This is only used if <see cref="authenticationMode"/> is set to <see cref="EAuthenticationMode.CREDENTIALS"/>.</para>
        /// </summary>
        public SCredentials credentials = new();

        /// <summary>
        /// The process information.
        /// </summary>
        public SProcessInformation processInformation = new();

        public SParameters() {}
    }
}
