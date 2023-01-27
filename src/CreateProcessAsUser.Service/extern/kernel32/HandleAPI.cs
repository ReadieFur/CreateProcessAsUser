using System;
using System.Runtime.InteropServices;

namespace kernel32
{
    public static class HandleAPI
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hSnapshot);
    }
}
