using System;

namespace CreateProcessAsUser.Shared
{
    [Serializable]
    public struct SResult
    {
        /// <summary>
        /// The ID of the process that was created, or -1 if the operation failed.
        /// </summary>
        public Int32 processId = -1;

        //TODO: Use an enum.
        /// <summary>
        /// The result of the operation.
        /// </summary>
        //Possibly replace this enum with an int holding the value of GetLastError().
        public EResult result = EResult.UNKNOWN;

        public SResult() {}
    }
}
