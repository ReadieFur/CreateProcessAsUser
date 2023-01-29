using System;

namespace CreateProcessAsUser.Shared
{
    [Serializable]
    public enum EResult : Int16
    {
        //Possibly change a lot of these errors to <INTERNAL_ERROR>?
        UNKNOWN = -1,
        CREATED_PROCESS,
        TIMED_OUT,
        FAILED_TO_GET_TOKEN,
        FAILED_TO_GET_ENVIRONMENT,
        FAILED_TO_CREATE_PROCESS,
        INVALID_CREDENTIALS,
        INVALID_PROCESS_INFORMATION,
        INSUFFICIENT_PERMISSIONS,
        FAILED_TO_GET_CALLER_PID,
        FAILED_TO_GET_DESKTOP_SESSIONS
    }
}
