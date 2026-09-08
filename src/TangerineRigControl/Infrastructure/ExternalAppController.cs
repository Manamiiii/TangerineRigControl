using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using TangerineRigControl.Models;

namespace TangerineRigControl.Infrastructure
{
    internal sealed class ExternalAppController
    {
        public IntPtr OpenAndFindWindow(ApplicationTarget target, int timeoutMilliseconds)
        {
            if (target == null) throw new ArgumentNullException("target");
            var handle = FindWindow(target);
            if (handle != IntPtr.Zero) ProcessIntegrity.EnsureCanAutomate(handle, target.DisplayName);
            var usesNativeReopen = RequiresNativeReopen(target.ProcessName);
            var needsNativeReopen = usesNativeReopen && (handle == IntPtr.Zero || !HasVisibleContent(handle));
            if (handle == IntPtr.Zero || needsNativeReopen)
            {
                if (string.IsNullOrWhiteSpace(target.ExecutablePath) || !File.Exists(target.ExecutablePath))
                {
                    throw new InvalidOperationException(target.DisplayName + " 的程序路径尚未配置。");
                }

                using (var launched = Process.Start(new ProcessStartInfo(target.ExecutablePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(target.ExecutablePath))
                })) { }
            }

            var started = Environment.TickCount;
            do
            {
                handle = FindWindow(target);
                if (handle != IntPtr.Zero)
                {
                    ProcessIntegrity.EnsureCanAutomate(handle, target.DisplayName);
                    LConnectWindowRestorer.Restore(target, handle);
                    // Electron's own second-instance handler must restore its renderer before native foregrounding.
                    if (!usesNativeReopen || HasVisibleContent(handle)) return handle;
                }
                Thread.Sleep(150);
            } while (unchecked(Environment.TickCount - started) < timeoutMilliseconds);

            throw new InvalidOperationException(usesNativeReopen
                ? "KANALI 的原生重开请求未能恢复界面，请从托盘打开后重试。"
                : "没有找到 " + target.DisplayName + " 的主窗口。");
        }

        internal static bool RequiresNativeReopen(string processName)
        {
            return string.Equals(processName, "KANALI", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool HasVisibleContent(IntPtr handle)
        {
            if (!NativeMethods.IsWindow(handle) || !NativeMethods.IsWindowVisible(handle) || NativeMethods.IsIconic(handle)) return false;
            try
            {
                var root = AutomationElement.FromHandle(handle);
                return root.FindFirst(TreeScope.Children,
                    new PropertyCondition(AutomationElement.IsOffscreenProperty, false)) != null;
            }
            catch (ElementNotAvailableException) { return false; }
        }

        public IntPtr FindWindow(ApplicationTarget target)
        {
            IntPtr result = IntPtr.Zero;
            long bestScore = -1;
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

                NativeMethods.Rect client;
                if (!NativeMethods.GetClientRect(handle, out client)) return true;
                if (NativeMethods.IsIconic(handle))
                {
                    // A minimized window can report an empty client area; use its restored bounds for discovery.
                    var placement = new NativeMethods.WindowPlacement
                    {
                        Length = Marshal.SizeOf(typeof(NativeMethods.WindowPlacement))
                    };
                    if (NativeMethods.GetWindowPlacement(handle, ref placement)) client = placement.NormalPosition;
                }
                var visible = NativeMethods.IsWindowVisible(handle);
                // Tray applications retain a hidden main window. It must be discoverable before Show restores it.
                // Only accept hidden windows when an explicit title filter identified the target.
                if (!visible && string.IsNullOrWhiteSpace(target.WindowTitleContains)) return true;
                var score = WindowCandidateScore(visible, client.Right - client.Left, client.Bottom - client.Top);
                if (score > bestScore)
                {
                    bestScore = score;
                    result = handle;
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        internal static long WindowCandidateScore(bool visible, int width, int height)
        {
            // Exclude zero-size and 1x1 infrastructure windows (for example GDI+ helper windows).
            if (width <= 1 || height <= 1) return -1;
            return (visible ? 1L << 62 : 0) + (long)width * height;
        }

        public void Show(IntPtr handle)
        {
            NativeMethods.ShowWindow(handle, NativeMethods.SwRestore);
            NativeMethods.SetForegroundWindow(handle);
            var started = Environment.TickCount;
            while (NativeMethods.GetForegroundWindow() != handle)
            {
                if (unchecked(Environment.TickCount - started) >= 1500)
                    throw new InvalidOperationException("无法激活原厂软件窗口，请手动打开后重试。");
                Thread.Sleep(50);
            }
        }

        public void Minimize(IntPtr handle)
        {
            ProcessIntegrity.EnsureCanAutomate(handle, "原厂软件");
            // Let the application's UI thread handle the normal system minimize command first.
            NativeMethods.PostMessage(handle, 0x0112, new IntPtr(0xF020), IntPtr.Zero);
            if (WaitForMinimized(handle)) return;
            NativeMethods.ShowWindowAsync(handle, NativeMethods.SwMinimize);
            if (!WaitForMinimized(handle))
                throw new InvalidOperationException("操作序列已执行，但原厂软件未能自动最小化，请手动收起窗口。");
        }

        private static bool WaitForMinimized(IntPtr handle)
        {
            var started = Environment.TickCount;
            do
            {
                if (!NativeMethods.IsWindow(handle) || !NativeMethods.IsWindowVisible(handle) || NativeMethods.IsIconic(handle)) return true;
                Thread.Sleep(50);
            } while (unchecked(Environment.TickCount - started) < 1500);
            return false;
        }
    }
}
