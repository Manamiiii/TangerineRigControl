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
        internal static readonly string DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name);

        // Shared with Setup/uninstall, including other sessions of the same Windows user.
        internal static FileStream AcquireLifecycleLock()
        {
            return AcquireLifecycleLock(DataDirectory);
        }

        internal static FileStream AcquireLifecycleLock(string directory)
        {
            Directory.CreateDirectory(directory);
            return new FileStream(Path.Combine(directory, ".lifecycle.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
    }
}
