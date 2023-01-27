using System;

namespace CreateProcessAsUser.Shared
{
    [Serializable]
    public enum EAuthenticationMode : Int16
    {
        /// <summary>
        /// Inherit the credentials of the caller.
        /// </summary>
        INHERIT,

        /// <summary>
        /// Use the provided <see cref="SCredentials"/>.
        /// </summary>
        CREDENTIALS,

        /// <summary>
        /// Use the provided logon token.
        /// </summary>
        //TOKEN
    }
}
