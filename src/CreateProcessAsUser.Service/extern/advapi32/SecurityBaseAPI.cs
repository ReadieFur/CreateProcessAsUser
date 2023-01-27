using System;
using System.Runtime.InteropServices;

namespace advapi32
{
    public static class SecurityBaseAPI
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public extern static bool DuplicateToken(
            IntPtr ExistingTokenHandle,
            uint SECURITY_IMPERSONATION_LEVEL,
            ref IntPtr DuplicateTokenHandle);

        [DllImport("advapi32.dll", EntryPoint = "DuplicateTokenEx")]
        public static extern bool DuplicateTokenEx(
            IntPtr ExistingTokenHandle,
            uint dwDesiredAccess,
            IntPtr lpThreadAttributes,
            int TokenType,
            int ImpersonationLevel,
            ref IntPtr DuplicateTokenHandle);

        public const uint STANDARD_RIGHTS_READ = 0x00020000;
        public const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
        public const uint TOKEN_DUPLICATE = 0x0002;
        public const uint TOKEN_QUERY = 0x0008;
        public const uint TOKEN_IMPERSONATE = 0x0004;
        public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;

        public const int SECURITY_IMPERSONATION_LEVEL = 2;
        
        public const int TOKEN_PRIMARY = 1;
    }
}
