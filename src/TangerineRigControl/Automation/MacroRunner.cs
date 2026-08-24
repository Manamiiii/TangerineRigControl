using System;
using System.Threading;
using System.Windows.Automation;
using TangerineRigControl.Infrastructure;
using TangerineRigControl.Models;

namespace TangerineRigControl.Automation
{
    internal sealed class MacroRunner
    {
        private readonly ExternalAppController _apps = new ExternalAppController();

        public void Run(ApplicationTarget target, MacroDefinition macro, bool minimizeAfterAction)
        {
            if (macro == null || macro.Steps == null || macro.Steps.Count == 0)
            {
                throw new InvalidOperationException(target.DisplayName + "的“" + (macro == null ? "操作" : macro.Name) + "”尚未录制。");
            }

            var handle = _apps.OpenAndFindWindow(target, 10000);
            _apps.Show(handle);
            Thread.Sleep(500);

            foreach (var step in macro.Steps)
            {
                if (!TryInvokeAccessibleElement(handle, step))
                {
                    ClickRelative(handle, step.RelativeX, step.RelativeY);
                }
                Thread.Sleep(Math.Max(100, step.DelayAfterMilliseconds));
            }

            if (minimizeAfterAction) _apps.Minimize(handle);
        }

        private static bool TryInvokeAccessibleElement(IntPtr handle, MacroStep step)
        {
            try
            {
                Condition condition = null;
                if (!string.IsNullOrWhiteSpace(step.AutomationId))
                {
                    condition = new PropertyCondition(AutomationElement.AutomationIdProperty, step.AutomationId);
                }
                else if (!string.IsNullOrWhiteSpace(step.ElementName))
                {
                    condition = new PropertyCondition(AutomationElement.NameProperty, step.ElementName);
                }

                if (condition == null) return false;
                var root = AutomationElement.FromHandle(handle);
                var element = root.FindFirst(TreeScope.Descendants, condition);
                if (element == null || !element.Current.IsEnabled) return false;

                object pattern;
                if (element.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
                {
                    ((InvokePattern)pattern).Invoke();
                    return true;
                }
                if (element.TryGetCurrentPattern(TogglePattern.Pattern, out pattern))
                {
                    ((TogglePattern)pattern).Toggle();
                    return true;
                }

                var bounds = element.Current.BoundingRectangle;
                if (bounds.IsEmpty) return false;
                ClickScreen((int)bounds.Left + (int)bounds.Width / 2, (int)bounds.Top + (int)bounds.Height / 2);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ClickRelative(IntPtr handle, double relativeX, double relativeY)
        {
            NativeMethods.Rect rect;
            if (!NativeMethods.GetClientRect(handle, out rect)) throw new InvalidOperationException("无法读取程序窗口尺寸。");
            var point = new NativeMethods.Point
            {
                X = (int)Math.Round((rect.Right - rect.Left) * relativeX),
                Y = (int)Math.Round((rect.Bottom - rect.Top) * relativeY)
            };
            NativeMethods.ClientToScreen(handle, ref point);
            ClickScreen(point.X, point.Y);
        }

        private static void ClickScreen(int x, int y)
        {
            NativeMethods.SetCursorPos(x, y);
            NativeMethods.mouse_event(NativeMethods.MouseeventfLeftdown, 0, 0, 0, UIntPtr.Zero);
            NativeMethods.mouse_event(NativeMethods.MouseeventfLeftup, 0, 0, 0, UIntPtr.Zero);
        }
    }
}
