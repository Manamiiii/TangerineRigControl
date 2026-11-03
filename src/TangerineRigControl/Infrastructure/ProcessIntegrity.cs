// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Manamiiii

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TangerineRigControl.Infrastructure
{
    internal static class ProcessIntegrity
    {
        private const int TokenIntegrityLevel = 25;
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint TokenQuery = 0x0008;

        public static void EnsureCanAutomate(IntPtr windowHandle, string displayName)
        {
            uint targetProcessId;
            NativeMethods.GetWindowThreadProcessId(windowHandle, out targetProcessId);
            if (targetProcessId == 0) throw new InvalidOperationException("目标窗口已失效，请重新打开原厂软件。");

            int currentLevel;
            int targetLevel;
            if (!TryGetCurrentIntegrityLevel(out currentLevel) ||
                !TryGetProcessIntegrityLevel(targetProcessId, out targetLevel))
            {
                throw new InvalidOperationException("无法确认原厂软件的操作权限，已停止自动化。请确认程序仍在运行且权限一致。");
            }

            if (!CanAutomate(currentLevel, targetLevel))
            {
                throw new InvalidOperationException(
                    displayName + " 正以管理员权限运行，当前控制台无法向它发送点击。" +
                    "请退出 TangerineRigControl 后右键选择“以管理员身份运行”，再录制或操作。");
            }
        }

        internal static bool CanAutomate(int currentIntegrityLevel, int targetIntegrityLevel)
        {
            return currentIntegrityLevel >= targetIntegrityLevel;
        }

        private static bool TryGetCurrentIntegrityLevel(out int integrityLevel)
        {
            integrityLevel = 0;
            IntPtr token;
            using (var process = Process.GetCurrentProcess())
            {
                if (!NativeMethods.OpenProcessToken(process.Handle, TokenQuery, out token)) return false;
            }

            try
            {
                return TryReadIntegrityLevel(token, out integrityLevel);
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }

        private static bool TryGetProcessIntegrityLevel(uint processId, out int integrityLevel)
        {
            integrityLevel = 0;
            var processHandle = NativeMethods.OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (processHandle == IntPtr.Zero) return false;
            try
            {
                IntPtr token;
                if (!NativeMethods.OpenProcessToken(processHandle, TokenQuery, out token)) return false;
                try
                {
                    return TryReadIntegrityLevel(token, out integrityLevel);
                }
                finally
                {
                    NativeMethods.CloseHandle(token);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(processHandle);
            }
        }

        private static bool TryReadIntegrityLevel(IntPtr token, out int integrityLevel)
        {
            integrityLevel = 0;
            int size;
            NativeMethods.GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out size);
            if (size <= 0) return false;

            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!NativeMethods.GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out size)) return false;
                var label = (NativeMethods.TokenMandatoryLabel)Marshal.PtrToStructure(
                    buffer, typeof(NativeMethods.TokenMandatoryLabel));
                var subAuthorityCount = Marshal.ReadByte(NativeMethods.GetSidSubAuthorityCount(label.Label.Sid));
                if (subAuthorityCount == 0) return false;
                var lastSubAuthority = NativeMethods.GetSidSubAuthority(label.Label.Sid, subAuthorityCount - 1);
                integrityLevel = Marshal.ReadInt32(lastSubAuthority);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
