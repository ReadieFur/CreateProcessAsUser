using System;

namespace CreateProcessAsUser.Shared
{
    [Serializable]
    public struct SMessage
    {
        /// <summary>
        /// The parameters to use when creating the process.
        /// </summary>
        public SParameters parameters = new();

        /// <summary>
        /// The result of the operation.
        /// </summary>
        public SResult result = new();

        public SMessage() {}
    }
}
