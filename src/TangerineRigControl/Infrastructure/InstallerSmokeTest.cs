// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Manamiiii
#if INSTALLER_TEST
using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;
using TangerineRigControl.Models;
using TangerineRigControl.UI;

namespace TangerineRigControl.Infrastructure
{
    // Compiled only into the separately named fixture. Never reads production settings.
    internal static class InstallerSmokeTest
    {
        internal static int Run(string[] args)
        {
            if (args.Length == 1 && args[0] == "--fixture-startup-test") return CheckStartupOwnership();
            if (args.Length != 1 || (args[0] != "--fixture-write" && args[0] != "--fixture-read")) return 90;
            var settings = SettingsStore.Load();
            if (args[0] == "--fixture-write")
            {
                settings.UseAuraSdk = false;
                settings.LConnect.InitialDelayMilliseconds = 777;
                settings.LConnect.TurnOn.Steps.Add(new MacroStep { RelativeX = 0.25, RelativeY = 0.5 });
                SettingsStore.Save(settings);
                SettingsStore.Save(SettingsStore.Clone(settings));
            }
            if (settings.UseAuraSdk || settings.LConnect.InitialDelayMilliseconds != 777 ||
                settings.LConnect.TurnOn.Steps.Count != 1) return 91;
            using (var form = new MainForm(settings))
            using (var timer = new Timer { Interval = 100 })
            {
                form.Shown += delegate { File.WriteAllText(Path.Combine(AppIdentity.DataDirectory, "ready"), "ready"); };
                timer.Tick += delegate
                {
                    if (!File.Exists(Path.Combine(AppIdentity.DataDirectory, "stop"))) return;
                    timer.Stop();
                    // Exercise close-to-tray and the actual tray exit handler, without hardware actions.
                    form.Close();
                    if (form.IsDisposed || form.Visible) throw new InvalidOperationException("Window close did not retain the tray application.");
                    File.WriteAllText(Path.Combine(AppIdentity.DataDirectory, "tray-close-passed"), "passed");
                    var tray = (NotifyIcon)typeof(MainForm).GetField("_trayIcon", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    foreach (ToolStripItem item in tray.ContextMenuStrip.Items)
                        if (item.Text == "退出") { item.PerformClick(); return; }
                    throw new InvalidOperationException("Tray exit action missing.");
                };
                timer.Start();
                Application.Run(form);
            }
            return 0;
        }

        private static int CheckStartupOwnership()
        {
            var original = StartupManager.ReadValue();
            try
            {
                StartupManager.RestoreValue(Tuple.Create<object, RegistryValueKind>("fixture-other-portable-command", RegistryValueKind.String));
                StartupManager.SetEnabled(false);
                if (!object.Equals(StartupManager.ReadValue().Item1, "fixture-other-portable-command")) return 92;
                StartupManager.SetEnabled(true);
                if (!StartupManager.IsEnabled()) return 93;
                StartupManager.SetEnabled(false);
                if (StartupManager.ReadValue().Item1 != null) return 94;
                StartupManager.RestoreValue(Tuple.Create<object, RegistryValueKind>("\"" + Application.ExecutablePath + "\"", RegistryValueKind.String));
                StartupManager.SetEnabled(false);
                return StartupManager.ReadValue().Item1 == null ? 0 : 95;
            }
            finally { StartupManager.RestoreValue(original); }
        }
    }
}
#endif
