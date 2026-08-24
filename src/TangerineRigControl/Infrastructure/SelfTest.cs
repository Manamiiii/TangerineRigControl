using System.IO;
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
                return 0;
            }
            catch
            {
                return 10;
            }
        }
    }
}
