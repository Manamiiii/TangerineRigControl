// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Manamiiii

using System;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Threading.Tasks;



using TangerineRigControl.Automation;

using System.Xml.Serialization;
using TangerineRigControl.Models;

namespace TangerineRigControl.Infrastructure
{
    internal static class SelfTest
    {
        public static int Run()
        {
            try
            {
                RegressionTests.Run();
                var original = new RigSettings();
                var partial = new RigSettings();
                partial.Kanali.TurnOn.Steps.Add(new MacroStep { RelativeX = 0.2, RelativeY = 0.3 });
                partial.LConnect.TurnOff.Steps.Add(new MacroStep { RelativeX = 0.4, RelativeY = 0.5 });
                partial.MotherboardLighting = null;
                partial.GpuLighting = null;
                SettingsStore.FillMissingDefaults(partial);
                if (partial.Kanali.TurnOn.Steps.Count != 1 || partial.LConnect.TurnOff.Steps.Count != 1 ||
                    partial.MotherboardLighting == null || partial.GpuLighting == null) return 69;
                var execution = original.GetExecutionTargets();
                if (execution[2] != original.GpuLighting || execution[3] != original.MotherboardLighting) return 65;
                if (!TangerineRigControl.Services.AuraLightingClient.IsSupportedType(0x10000) ||
                    !TangerineRigControl.Services.AuraLightingClient.IsSupportedType(0x70000) ||
                    !TangerineRigControl.Services.AuraLightingClient.IsSupportedType(0x11000) ||
                    TangerineRigControl.Services.AuraLightingClient.IsSupportedType(0x20000) ||
                    TangerineRigControl.Services.AuraLightingClient.IsSupportedType(0x30000)) return 66;
                var sdkSettings = SettingsStore.Clone(original);
                if (!sdkSettings.UseAuraSdk) return 67;
                sdkSettings.UseAuraSdk = false;
                if (SettingsStore.Clone(sdkSettings).UseAuraSdk) return 68;
                original.LConnect.TurnOn.Steps.Add(new MacroStep
                {
                    AutomationId = "test-button",
                    RelativeX = 0.25,
                    RelativeY = 0.75
                });

                var serializer = new XmlSerializer(typeof(RigSettings));
                // Old two-screen settings must retain their steps when the unused API flag is ignored.
                using (var legacy = new StringReader("<RigSettings><SignalRgbEnabled>true</SignalRgbEnabled><LConnect><TurnOn><Steps><Step><RelativeX>0.25</RelativeX><RelativeY>0.75</RelativeY></Step></Steps></TurnOn></LConnect></RigSettings>"))
                {
                    var migrated = (RigSettings)serializer.Deserialize(legacy);
                    SettingsStore.Validate(migrated);
                    if (migrated.GetTargets().Length != 4 || migrated.LConnect.TurnOn.Steps.Count != 1 ||
                        migrated.LConnect.TurnOn.Steps[0].RelativeX != 0.25 ||
                        migrated.MotherboardLighting.TurnOn.Steps.Count != 0 || migrated.GpuLighting.TurnOff.Steps.Count != 0) return 62;
                    var edited = SettingsStore.Clone(migrated);
                    edited.MotherboardLighting.TurnOn.Steps.Add(new MacroStep());
                    if (migrated.MotherboardLighting.TurnOn.Steps.Count != 0 || edited.LConnect.TurnOn.Steps.Count != 1) return 63;
                    edited.GpuLighting.TurnOff.Steps.Add(new MacroStep { RelativeY = double.NaN });
                    if (!Rejects(delegate { SettingsStore.Validate(edited); })) return 64;
                }
                using (var stream = new MemoryStream())
                {
                    serializer.Serialize(stream, original);
                    stream.Position = 0;
                    var copy = serializer.Deserialize(stream) as RigSettings;
                    if (copy == null) return 11;
                    if (copy.LConnect == null || copy.Kanali == null) return 12;
                    if (copy.LConnect.TurnOn.Steps.Count != 1) return 13;
                    if (copy.LConnect.TurnOn.Steps[0].AutomationId != "test-button") return 14;
                    if (copy.LConnect.TurnOff == null || copy.Kanali.TurnOff == null) return 15;
                }
                if (!ProcessIntegrity.CanAutomate(0x3000, 0x2000)) return 16;
                if (ProcessIntegrity.CanAutomate(0x2000, 0x3000)) return 17;
                var draft = SettingsStore.Clone(original);
                draft.LConnect.TurnOn.Steps[0].RelativeX = 0.5;
                draft.Kanali.TurnOff.Steps.Add(new MacroStep());
                if (original.LConnect.TurnOn.Steps[0].RelativeX != 0.25 || original.Kanali.TurnOff.Steps.Count != 0) return 21;
                draft.LConnect.TurnOn.Steps[0].DesiredToggleState = true;
                if (SettingsStore.Clone(draft).LConnect.TurnOn.Steps[0].DesiredToggleState != true) return 22;
                if (SettingsStore.Clone(original).LConnect.TurnOn.Steps[0].DesiredToggleState.HasValue) return 23;
                SettingsStore.Validate(original);
                foreach (var value in new[] { double.NaN, double.PositiveInfinity, -0.1, 1.0 })
                {
                    draft = SettingsStore.Clone(original);
                    draft.LConnect.TurnOn.Steps[0].RelativeX = value;
                    if (!Rejects(delegate { SettingsStore.Validate(draft); })) return 24;
                }
                draft = SettingsStore.Clone(original);
                draft.LConnect.TurnOn.Steps.Add(null);
                if (!Rejects(delegate { SettingsStore.Validate(draft); })) return 25;
                draft = SettingsStore.Clone(original);
                draft.Kanali.InitialDelayMilliseconds = int.MaxValue;
                if (!Rejects(delegate { SettingsStore.Validate(draft); })) return 26;
                if (!Rejects(delegate { ProcessIntegrity.EnsureCanAutomate(IntPtr.Zero, "test"); })) return 27;
                if (!Rejects(delegate { MacroRunner.EnsureWindow(IntPtr.Zero, 0); })) return 28;
                if (ExternalAppController.WindowCandidateScore(false, 960, 540) < 0) return 51;
                if (ExternalAppController.WindowCandidateScore(false, 1, 1) >= 0 ||
                    ExternalAppController.WindowCandidateScore(true, 0, 540) >= 0) return 52;
                if (ExternalAppController.WindowCandidateScore(true, 640, 480) <=
                    ExternalAppController.WindowCandidateScore(false, 960, 540)) return 53;
                if (ExternalAppController.WindowCandidateScore(false, 960, 540) <=
                    ExternalAppController.WindowCandidateScore(false, 320, 200)) return 54;
                if (!ExternalAppController.RequiresNativeReopen("kanali") ||
                    ExternalAppController.RequiresNativeReopen("L-Connect 3") ||
                    ExternalAppController.RequiresNativeReopen(null)) return 55;
                if (ExternalAppController.HasVisibleContent(IntPtr.Zero)) return 56;
                if (!TestLocalRestoreProtocol()) return 61;
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Self-test failed: " + ex.Message);
                return 10;
            }
        }

        private static bool Rejects(Action action)
        {
            try { action(); return false; }
            catch (InvalidOperationException) { return true; }
        }

        private static bool TestLocalRestoreProtocol()
        {
            // Isolated synthetic pipe: this test never contacts the installed vendor application.
            var name = "TangerineRigControl-test-" + Guid.NewGuid().ToString("N");
            using (var process = Process.GetCurrentProcess())
            using (var server = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            {
                var connection = server.BeginWaitForConnection(null, null);
                var received = Task.Factory.StartNew(delegate
                {
                    server.EndWaitForConnection(connection);
                    var data = new byte[2];
                    var count = 0;
                    while (count < data.Length)
                    {
                        var read = server.Read(data, count, data.Length - count);
                        if (read == 0) return false;
                        count += read;
                    }
                    return data[0] == 0x09 && data[1] == 0x36;
                }, TaskCreationOptions.LongRunning);
                LConnectWindowRestorer.NotifyInstance(name, (uint)process.Id);
                if (!received.Wait(1500) || !received.Result) return false;
            }
            using (var server = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            {
                var connection = server.BeginWaitForConnection(null, null);
                var received = Task.Factory.StartNew(delegate
                {
                    server.EndWaitForConnection(connection);
                    return server.Read(new byte[2], 0, 2);
                }, TaskCreationOptions.LongRunning);
                if (!Rejects(delegate { LConnectWindowRestorer.NotifyInstance(name, uint.MaxValue); })) return false;
                if (!received.Wait(1500) || received.Result != 0) return false;
            }
            return true;
        }

    }
}
