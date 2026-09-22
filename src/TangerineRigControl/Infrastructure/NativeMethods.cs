using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TangerineRigControl.Infrastructure
{
    internal static class NativeMethods
    {
        internal const int SwRestore = 9;
        internal const int SwMinimize = 6;
        internal const uint MouseeventfLeftdown = 0x0002;
        internal const uint MouseeventfLeftup = 0x0004;
        internal const int WmHotkey = 0x0312;
        internal const uint ModAlt = 0x0001;
        internal const uint ModControl = 0x0002;
        internal const uint ModNoRepeat = 0x4000;
        internal const int VkF8 = 0x77;
        internal const int VkF10 = 0x79;
        internal const int VkF11 = 0x7A;
        internal const int VkF12 = 0x7B;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WindowPlacement
        {
            internal int Length;
            internal int Flags;
            internal int ShowCommand;
            internal Point MinPosition;
            internal Point MaxPosition;
            internal Rect NormalPosition;
        }

        [DllImport("user32.dll")]
        internal static extern bool IsIconic(IntPtr handle);

        [DllImport("user32.dll")]
        internal static extern bool GetWindowPlacement(IntPtr handle, ref WindowPlacement placement);

        [StructLayout(LayoutKind.Sequential)]
        internal struct SidAndAttributes
        {
            public IntPtr Sid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct TokenMandatoryLabel
        {
            public SidAndAttributes Label;
        }

        [DllImport("user32.dll")]
        internal static extern bool IsWindow(IntPtr handle);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr handle, uint flags);

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindowAsync(IntPtr hWnd, int command);

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

        [DllImport("user32.dll")]
        internal static extern bool ClientToScreen(IntPtr hWnd, ref Point point);

        [DllImport("user32.dll")]
        internal static extern bool ScreenToClient(IntPtr hWnd, ref Point point);

        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        internal static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        internal static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

        [DllImport("user32.dll")]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, int virtualKey);

        [DllImport("user32.dll")]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool GetTokenInformation(
            IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation,
            int tokenInformationLength, out int returnLength);

        [DllImport("advapi32.dll")]
        internal static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

        [DllImport("advapi32.dll")]
        internal static extern IntPtr GetSidSubAuthority(IntPtr sid, int subAuthority);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
    }
}
