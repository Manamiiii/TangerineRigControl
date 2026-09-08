using System;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using TangerineRigControl.Models;

namespace TangerineRigControl.Infrastructure
{
    internal static class LConnectWindowRestorer
    {
        public static void Restore(ApplicationTarget target, IntPtr handle)
        {
            if (!string.Equals(target.ProcessName, "L-Connect 3", StringComparison.OrdinalIgnoreCase)) return;
            uint processId;
            NativeMethods.GetWindowThreadProcessId(handle, out processId);
            try
            {
                // Ordinary minimization retains the managed content. Restore the native window
                // before asking UI Automation whether that content is on screen.
                if (NativeMethods.IsIconic(handle))
                {
                    NativeMethods.ShowWindowAsync(handle, NativeMethods.SwRestore);
                    if (WaitForContent(handle)) return;
                }
                if (ExternalAppController.HasVisibleContent(handle)) return;
                // Ask the existing UI instance to restore its managed visibility, just as its own reopen path does.
                // No vendor assemblies are loaded, no service commands are sent, and the pipe is local only.
                NotifyInstance(@"LConnect\SingleInstance", processId);
                if (WaitForContent(handle)) return;
                throw new InvalidOperationException("联力窗口尚未恢复可操作内容。");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("无法通过联力软件的本机恢复入口打开界面。请从托盘打开 L-Connect 后重试；已停止录制或回放。", ex);
            }
        }

        private static bool WaitForContent(IntPtr handle)
        {
            var started = Environment.TickCount;
            do
            {
                if (ExternalAppController.HasVisibleContent(handle)) return true;
                Thread.Sleep(100);
            } while (unchecked(Environment.TickCount - started) < 3000);
            return false;
        }

        internal static void NotifyInstance(string pipeName, uint expectedProcessId)
        {
            using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Anonymous))
            {
                pipe.Connect(1500);
                uint serverProcessId;
                if (expectedProcessId == 0 || !NativeMethods.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out serverProcessId) ||
                    serverProcessId != expectedProcessId)
                    throw new InvalidOperationException("联力恢复入口与目标窗口进程不一致。");

                // Two-byte UI activation notification; never reuse this channel for device control.
                var bytes = new byte[] { 0x09, 0x36 };
                using (var deadline = new CancellationTokenSource(1500))
                {
                    pipe.WriteAsync(bytes, 0, bytes.Length, deadline.Token).GetAwaiter().GetResult();
                }
            }
        }

    }
}
