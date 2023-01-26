using System;

namespace CreateProcessAsUser.Shared
{
    [Serializable]
    public struct SResult
    {
        /// <summary>
        /// The ID of the process that was created, or -1 if the operation failed.
        /// </summary>
        public int processId;

        //TODO: Use an enum.
        /// <summary>
        /// The result of the operation.
        /// </summary>
        public EResult result;

        internal static SResult Default() => new()
        {
            processId = -1,
            result = EResult.UNKNOWN
        };
    }
}
