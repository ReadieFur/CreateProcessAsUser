using System;

namespace CreateProcessAsUser.Shared
{
    [Serializable]
    public enum EResult : Int16
    {
        UNKNOWN = -1,
        OK,
        FAILED_TO_GET_CALLER_PID,
        TIMED_OUT
    }
}
