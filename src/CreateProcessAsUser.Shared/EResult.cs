using System;

namespace CreateProcessAsUser.Shared
{
    [Serializable]
    public enum EResult : Int16
    {
        //Possibly change a lot of these errors to <INTERNAL_ERROR>?
        UNKNOWN = -1,
        OK,
        TIMED_OUT,
        FAILED_TO_GET_TOKEN,
        FAILED_TO_GET_ENVIRONMENT,
        FAILED_TO_CREATE_PROCESS,
        INVALID_CREDENTIALS,
        INSUFFICIENT_PERMISSIONS,
        FAILED_TO_GET_CALLER_PID
    }
}
