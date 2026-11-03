// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Manamiiii
#if INSTALLER_TEST
using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using TangerineRigControl.Models;
using TangerineRigControl.UI;

namespace TangerineRigControl.Infrastructure
{
    // Compiled only into the separately named fixture. Never reads production settings.
    internal static class InstallerSmokeTest
    {
        internal static int Run(string[] args)
        {
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
                    typeof(MainForm).GetField("_allowClose", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, true);
                    form.Close();
                };
                timer.Start();
                Application.Run(form);
            }
            return 0;
        }
    }
}
#endif
