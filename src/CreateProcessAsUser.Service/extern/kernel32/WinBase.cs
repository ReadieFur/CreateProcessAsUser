using System;
using System.Runtime.InteropServices;

namespace kernel32
{
    public static class WinBase
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetNamedPipeClientProcessId(IntPtr hNamedPipeHandle, out uint lpServerProcessId);
    }
}
