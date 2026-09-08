using System;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;

using TangerineRigControl.Automation;
using TangerineRigControl.Services;
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
                var original = new RigSettings();
                original.LConnect.TurnOn.Steps.Add(new MacroStep
                {
                    AutomationId = "test-button",
                    RelativeX = 0.25,
                    RelativeY = 0.75
                });

                var serializer = new XmlSerializer(typeof(RigSettings));
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
                using (var handler = SignalRgbClient.CreateLocalHandler())
                    if (handler.UseProxy || handler.AllowAutoRedirect) return 41;
                var transport = new StubHandler(HttpStatusCode.NoContent);
                using (var client = new SignalRgbClient(transport))
                {
                    client.SetEnabledAsync(true).GetAwaiter().GetResult();
                    if (!transport.ValidRequest || transport.Body != "{\"enabled\":true}") return 42;
                    client.SetEnabledAsync(false).GetAwaiter().GetResult();
                    if (transport.Body != "{\"enabled\":false}") return 43;
                }
                foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.Redirect, HttpStatusCode.InternalServerError })
                {
                    using (var client = new SignalRgbClient(new StubHandler(status)))
                        if (!Rejects(delegate { client.SetEnabledAsync(true).GetAwaiter().GetResult(); })) return 44;
                }
                using (var client = new SignalRgbClient(new StubHandler(new HttpRequestException())))
                    if (!Rejects(delegate { client.SetEnabledAsync(true).GetAwaiter().GetResult(); })) return 45;
                using (var client = new SignalRgbClient(new StubHandler(new TaskCanceledException())))
                    if (!Rejects(delegate { client.SetEnabledAsync(true).GetAwaiter().GetResult(); })) return 46;
                if (!TestLocalRestoreProtocol()) return 61;
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Self-test failed: " + ex.GetType().Name);
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

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly Exception _error;
            internal bool ValidRequest;
            internal string Body;
            internal StubHandler(HttpStatusCode status) { _status = status; }
            internal StubHandler(Exception error) { _error = error; }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            {
                if (_error != null) throw _error;
                ValidRequest = request.Method.Method == "PATCH" &&
                    request.RequestUri.AbsoluteUri == "http://127.0.0.1:16038/api/v1/lighting/enabled";
                Body = await request.Content.ReadAsStringAsync();
                return new HttpResponseMessage(_status);
            }
        }
    }
}
