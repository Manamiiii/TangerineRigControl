using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using TangerineRigControl.Models;
using TangerineRigControl.Services;

namespace TangerineRigControl.Infrastructure
{
    internal static class RegressionTests
    {
        internal static void Run()
        {
            Case("settings: round trip, zero delay, draft isolation", RoundTrip);
            Case("settings: legacy backend migration", Legacy);
            Case("settings: recovery preserves good backup and damaged bytes", Recovery);
            Case("settings: unreadable files cannot be overwritten", Blocked);
            Case("settings: external edits and newer schemas are protected", Conflicts);
            Case("settings: failed write leaves primary intact", FailedWrite);
            Case("startup: exact executable command matching", Startup);
            Case("windows: pre-cancel never starts a process", CancelWindow);
            Case("aura: release does not create a session; one thread owns backend", AuraOwnership);
            Case("aura: timeout quarantines session and close is bounded", AuraTimeout);
            Case("aura: cancellation and release failure are reported", AuraFailures);
        }

        private static void Case(string name, Action test)
        {
            try { test(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { throw new InvalidOperationException("FAIL " + name + " (" + ex.GetType().Name + ")", ex); }
        }
        private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed"); }
        private static void Reject(Action action)
        {
            try { action(); } catch (InvalidOperationException) { return; }
            throw new InvalidOperationException("Expected rejection");
        }
        private static void WithFile(Action<string> test)
        {
            var directory = Path.Combine(Path.GetTempPath(), "TangerineRigControl-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try { test(Path.Combine(directory, "settings.xml")); }
            finally { Directory.Delete(directory, true); }
        }
        private static RigSettings Saved(SettingsFile store)
        {
            var value = store.Load();
            value.LConnect.TurnOn.Steps.Add(new MacroStep { RelativeX = 0.25, RelativeY = 0.75, DelayAfterMilliseconds = 0 });
            value.Kanali.TurnOff.Steps.Add(new MacroStep { RelativeX = 0.4, RelativeY = 0.6 });
            value.Kanali.InitialDelayMilliseconds = 0;
            store.Save(value);
            return value;
        }
        private static void RoundTrip()
        {
            WithFile(path =>
            {
                var store = new SettingsFile(path);
                var value = Saved(store);
                var loaded = new SettingsFile(path).Load();
                Check(loaded.Kanali.InitialDelayMilliseconds == 0 && loaded.LConnect.TurnOn.Steps[0].DelayAfterMilliseconds == 0);
                Check(loaded.Kanali.TurnOff.Steps.Count == 1 && loaded.LConnect.TurnOn.Steps[0].RelativeX == 0.25);
                var draft = SettingsStore.Clone(loaded);
                draft.Kanali.TurnOff.Steps.Clear();
                Check(new SettingsFile(path).Load().Kanali.TurnOff.Steps.Count == 1);
                store.Save(value);
                Check(File.Exists(path + ".bak"));
            });
        }
        private static void Legacy()
        {
            WithFile(path =>
            {
                File.WriteAllText(path, "<RigSettings><LConnect><TurnOn><Steps><Step><RelativeX>0.2</RelativeX><RelativeY>0.3</RelativeY></Step></Steps></TurnOn></LConnect></RigSettings>");
                var value = new SettingsFile(path).Load();
                Check(!value.UseAuraSdk && value.LConnect.TurnOn.Steps.Count == 1);
                Check(new SettingsFile(path + ".new").Load().UseAuraSdk);
            });
        }
        private static void Recovery()
        {
            WithFile(path =>
            {
                var store = new SettingsFile(path);
                var value = Saved(store);
                store.Save(value);
                var goodBackup = File.ReadAllText(path + ".bak");
                File.WriteAllText(path, "damaged");
                var recovery = new SettingsFile(path);
                value = recovery.Load();
                Check(value.LConnect.TurnOn.Steps.Count == 1 && recovery.Warning != null);
                recovery.Save(value);
                Check(File.ReadAllText(path + ".bak") == goodBackup);
                var copies = Directory.GetFiles(Path.GetDirectoryName(path), "settings.xml.recovery-*");
                Check(copies.Length == 1 && File.ReadAllText(copies[0]) == "damaged");
                recovery.Save(value);
                Check(new SettingsFile(path).Load().Kanali.TurnOff.Steps.Count == 1);
            });
        }
        private static void Blocked()
        {
            WithFile(path =>
            {
                File.WriteAllText(path, "bad");
                File.WriteAllText(path + ".bak", "bad backup");
                var store = new SettingsFile(path);
                var value = store.Load();
                Reject(() => store.Save(value));
                Check(File.ReadAllText(path) == "bad" && File.ReadAllText(path + ".bak") == "bad backup");
            });
        }
        private static void Conflicts()
        {
            WithFile(path =>
            {
                var store = new SettingsFile(path);
                var value = Saved(store);
                File.AppendAllText(path, " ");
                Reject(() => store.Save(value));
                File.Copy(path, path + ".bak");
                File.WriteAllText(path, File.ReadAllText(path).Replace("<SchemaVersion>1</SchemaVersion>", "<SchemaVersion>999</SchemaVersion>"));
                var newer = new SettingsFile(path);
                value = newer.Load();
                Check(newer.Source == "新版本配置保护");
                Reject(() => newer.Save(value));
            });
        }
        private static void FailedWrite()
        {
            WithFile(path =>
            {
                var store = new SettingsFile(path);
                var value = Saved(store);
                var before = File.ReadAllText(path);
                using (var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var failed = false;
                    try { store.Save(value); } catch (IOException) { failed = true; }
                    Check(failed);
                }
                Check(before == File.ReadAllText(path));
                Check(Directory.GetFiles(Path.GetDirectoryName(path), "*.tmp").Length == 0);
            });
        }
        private static void Startup()
        {
            Check(StartupManager.IsCurrentCommand("\"app.exe\" --startup", "app.exe"));
            Check(!StartupManager.IsCurrentCommand("\"other.exe\" --startup", "app.exe"));
            Check(!StartupManager.IsCurrentCommand(null, "app.exe"));
            Check(InstalledAppLocator.WindowProcessName("iGCBootstrapper.exe") == "iGC.Lite");
            Check(InstalledAppLocator.WindowProcessName("KANALI.exe") == "KANALI");
        }
        private static void CancelWindow()
        {
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel();
                var stopped = false;
                try { new ExternalAppController().OpenAndFindWindow(new ApplicationTarget(), 1, cancel.Token); }
                catch (OperationCanceledException) { stopped = true; }
                Check(stopped);
            }
        }
        private sealed class FakeAura : IAuraBackend
        {
            internal int CreatedOn;
            internal int CalledOn;
            internal int Released;
            internal bool Disposed;
            internal ManualResetEvent Gate;
            internal bool FailDispose;
            public string SetEnabled(bool enabled, CancellationToken token)
            {
                CalledOn = Thread.CurrentThread.ManagedThreadId;
                if (Gate != null) Gate.WaitOne();
                return "fake";
            }
            public void ReleaseExisting() { Released++; }
            public void Dispose() { Disposed = true; if (FailDispose) throw new InvalidOperationException("fake release failure"); }
        }
        private static void AuraOwnership()
        {
            var fake = new FakeAura();
            var made = 0;
            using (var session = new AuraSession(() => { made++; fake.CreatedOn = Thread.CurrentThread.ManagedThreadId; return fake; }, 2000))
            {
                session.ReleaseExisting();
                Check(made == 0);
                Check(session.SetEnabled(false, CancellationToken.None) == "fake");
                session.ReleaseExisting();
                Check(fake.CalledOn == fake.CreatedOn && fake.CalledOn != Thread.CurrentThread.ManagedThreadId && fake.Released == 1);
                Check(session.CloseAsync(2000).GetAwaiter().GetResult() && fake.Disposed);
            }
        }
        private static void AuraTimeout()
        {
            using (var gate = new ManualResetEvent(false))
            {
                var fake = new FakeAura { Gate = gate };
                using (var session = new AuraSession(() => fake, 100))
                {
                    try
                    {
                        var watch = Stopwatch.StartNew();
                        Reject(() => session.SetEnabled(false, CancellationToken.None));
                        Check(watch.ElapsedMilliseconds < 2000);
                        Reject(() => session.SetEnabled(true, CancellationToken.None));
                        Check(!session.CloseAsync(50).GetAwaiter().GetResult());
                    }
                    finally { gate.Set(); }
                    Check(session.CloseAsync(2000).GetAwaiter().GetResult());
                }
            }
        }

        private static void AuraFailures()
        {
            var made = 0;
            using (var token = new CancellationTokenSource())
            using (var session = new AuraSession(() => { made++; return new FakeAura(); }, 100))
            {
                token.Cancel();
                var canceled = false;
                try { session.SetEnabled(false, token.Token); } catch (OperationCanceledException) { canceled = true; }
                Check(canceled && made == 0);
                Check(session.CloseAsync(2000).GetAwaiter().GetResult());
            }
            using (var session = new AuraSession(() => new FakeAura { FailDispose = true }, 2000))
            {
                session.SetEnabled(false, CancellationToken.None);
                Check(!session.CloseAsync(2000).GetAwaiter().GetResult());
            }
        }
    }
}
