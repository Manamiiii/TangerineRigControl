// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Manamiiii
using System;
using System.IO;

namespace TangerineRigControl.Infrastructure
{
    internal static class AppIdentity
    {
#if INSTALLER_TEST
        internal const string Name = "TangerineRigControl.InstallerTest";
        internal const string ModelId = "Manamiiii.TangerineRigControl.InstallerTest";
#else
        internal const string Name = "TangerineRigControl";
        internal const string ModelId = "Manamiiii.TangerineRigControl";
#endif
        internal const string MutexName = "Local\\" + Name + ".SingleInstance";
        internal static readonly string LegacyDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name);
        internal static readonly string DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tangerine", "Data", Name);
        internal static readonly string RuntimeDirectory = Path.Combine(DataDirectory, "Runtime");

        // Shared with Setup/uninstall, including other sessions of the same Windows user.
        internal static IDisposable AcquireLifecycleLock()
        {
            var legacy = AcquireLifecycleLock(LegacyDataDirectory);
            try { return new LockPair(legacy, AcquireLifecycleLock(RuntimeDirectory)); }
            catch { legacy.Dispose(); throw; }
        }

        internal static FileStream AcquireLifecycleLock(string directory)
        {
            DataLayout.EnsureOrdinaryPath(Path.Combine(directory, ".lifecycle.lock"));
            Directory.CreateDirectory(directory);
            return new FileStream(Path.Combine(directory, ".lifecycle.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        private sealed class LockPair : IDisposable
        {
            private readonly FileStream _legacy, _current;
            internal LockPair(FileStream legacy, FileStream current) { _legacy = legacy; _current = current; }
            public void Dispose() { _current.Dispose(); _legacy.Dispose(); }
        }
    }
}
