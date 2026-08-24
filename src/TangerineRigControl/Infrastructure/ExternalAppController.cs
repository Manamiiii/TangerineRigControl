using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using TangerineRigControl.Models;

namespace TangerineRigControl.Infrastructure
{
    internal sealed class ExternalAppController
    {
        public IntPtr OpenAndFindWindow(ApplicationTarget target, int timeoutMilliseconds)
        {
            if (target == null) throw new ArgumentNullException("target");
            var handle = FindWindow(target);
            if (handle == IntPtr.Zero)
            {
                if (string.IsNullOrWhiteSpace(target.ExecutablePath) || !File.Exists(target.ExecutablePath))
                {
                    throw new InvalidOperationException(target.DisplayName + " 的程序路径尚未配置。");
                }

                Process.Start(new ProcessStartInfo(target.ExecutablePath) { UseShellExecute = true });
            }

            var started = Environment.TickCount;
            do
            {
                handle = FindWindow(target);
                if (handle != IntPtr.Zero) return handle;
                Thread.Sleep(150);
            } while (unchecked(Environment.TickCount - started) < timeoutMilliseconds);

            throw new InvalidOperationException("没有找到 " + target.DisplayName + " 的主窗口。");
        }

        public IntPtr FindWindow(ApplicationTarget target)
        {
            IntPtr result = IntPtr.Zero;
            NativeMethods.EnumWindows(delegate(IntPtr handle, IntPtr ignored)
            {
                uint processId;
                NativeMethods.GetWindowThreadProcessId(handle, out processId);
                try
                {
                    using (var process = Process.GetProcessById((int)processId))
                    {
                        if (!string.Equals(process.ProcessName, target.ProcessName, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
                catch
                {
                    return true;
                }

                var title = new StringBuilder(512);
                NativeMethods.GetWindowText(handle, title, title.Capacity);
                if (!string.IsNullOrWhiteSpace(target.WindowTitleContains) &&
                    title.ToString().IndexOf(target.WindowTitleContains, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return true;
                }

                result = handle;
                return false;
            }, IntPtr.Zero);
            return result;
        }

        public void Show(IntPtr handle)
        {
            NativeMethods.ShowWindow(handle, NativeMethods.SwRestore);
            NativeMethods.SetForegroundWindow(handle);
        }

        public void Minimize(IntPtr handle)
        {
            NativeMethods.ShowWindow(handle, NativeMethods.SwMinimize);
        }
    }
}
