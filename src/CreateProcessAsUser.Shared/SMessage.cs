using System;

namespace CreateProcessAsUser.Shared
{
    [Serializable]
    public struct SMessage
    {
        /// <summary>
        /// The parameters to use when creating the process.
        /// </summary>
        public SParameters parameters;

        /// <summary>
        /// The result of the operation.
        /// </summary>
        public SResult result;
    }
}
