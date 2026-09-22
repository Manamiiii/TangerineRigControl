using System;
using System.Threading;


using TangerineRigControl.Infrastructure;
using TangerineRigControl.Models;

namespace TangerineRigControl.Automation
{
    internal sealed class MacroRunner
    {
        private readonly ExternalAppController _apps = new ExternalAppController();

        public void Run(ApplicationTarget target, MacroDefinition macro, bool minimizeAfterAction, CancellationToken cancellation)
        {
            SettingsStore.ValidateMacro(macro);
            if (macro.Steps.Count == 0) throw new InvalidOperationException(target.DisplayName + "尚未录制该操作。");
            cancellation.ThrowIfCancellationRequested();
            var handle = _apps.OpenAndFindWindow(target, 10000, cancellation);
            uint processId;
            NativeMethods.GetWindowThreadProcessId(handle, out processId);
            _apps.Show(handle, cancellation);
            Wait(target.InitialDelayMilliseconds, cancellation);
            for (var index = 0; index < macro.Steps.Count; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                EnsureWindow(handle, processId);
                try
                {
                    var step = macro.Steps[index];
                    ClickRelative(handle, step.RelativeX, step.RelativeY);
                    Wait(step.DelayAfterMilliseconds, cancellation);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("第 " + (index + 1) + " 步停止：" + ex.Message, ex);
                }
            }
            cancellation.ThrowIfCancellationRequested();
            // No further click is sent: the last action is allowed to hide/close its own window.
            if (minimizeAfterAction && NativeMethods.IsWindow(handle) && NativeMethods.IsWindowVisible(handle))
            {
                uint actualProcess;
                NativeMethods.GetWindowThreadProcessId(handle, out actualProcess);
                if (actualProcess == processId) _apps.Minimize(handle, cancellation);
            }
        }

        internal static void EnsureWindow(IntPtr handle, uint expectedProcess)
        {
            uint actualProcess;
            NativeMethods.GetWindowThreadProcessId(handle, out actualProcess);
            if (!NativeMethods.IsWindow(handle) || actualProcess != expectedProcess || !NativeMethods.IsWindowVisible(handle) || NativeMethods.GetForegroundWindow() != handle)
                throw new InvalidOperationException("原厂软件窗口已变化，请重新打开后重试。");
            ProcessIntegrity.EnsureCanAutomate(handle, "原厂软件");
        }

        private static void Wait(int milliseconds, CancellationToken cancellation)
        {
            if (cancellation.WaitHandle.WaitOne(milliseconds)) cancellation.ThrowIfCancellationRequested();
        }

        private static void ClickRelative(IntPtr handle, double relativeX, double relativeY)
        {
            var point = GetRecordedPoint(handle, relativeX, relativeY);
            ClickScreen(handle, point.X, point.Y);
        }

        private static NativeMethods.Point GetRecordedPoint(IntPtr handle, double relativeX, double relativeY)
        {
            NativeMethods.Rect rect;
            if (!NativeMethods.GetClientRect(handle, out rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                throw new InvalidOperationException("无法读取程序窗口尺寸。");
            var point = new NativeMethods.Point
            {
                X = Math.Min(rect.Right - rect.Left - 1, (int)((rect.Right - rect.Left) * relativeX)),
                Y = Math.Min(rect.Bottom - rect.Top - 1, (int)((rect.Bottom - rect.Top) * relativeY))
            };
            if (!NativeMethods.ClientToScreen(handle, ref point)) throw new InvalidOperationException("无法转换点击位置。");
            return point;
        }

        private static void ClickScreen(IntPtr handle, int x, int y)
        {
            var point = new NativeMethods.Point { X = x, Y = y };
            if (NativeMethods.GetForegroundWindow() != handle ||
                NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(point), 2) != handle)
                throw new InvalidOperationException("目标窗口失去焦点或被遮挡，已停止点击。");
            if (!NativeMethods.SetCursorPos(x, y)) throw new InvalidOperationException("无法移动鼠标，已停止点击。");
            NativeMethods.mouse_event(NativeMethods.MouseeventfLeftdown, 0, 0, 0, UIntPtr.Zero);
            NativeMethods.mouse_event(NativeMethods.MouseeventfLeftup, 0, 0, 0, UIntPtr.Zero);
        }
    }
}
