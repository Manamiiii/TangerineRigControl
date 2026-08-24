using System;
using System.IO;
using System.Xml.Serialization;
using TangerineRigControl.Models;

namespace TangerineRigControl.Infrastructure
{
    internal static class SettingsStore
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TangerineRigControl");

        internal static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.xml");

        public static RigSettings Load()
        {
            RigSettings settings = null;
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var serializer = new XmlSerializer(typeof(RigSettings));
                    using (var stream = File.OpenRead(SettingsPath))
                    {
                        settings = serializer.Deserialize(stream) as RigSettings;
                    }
                }
            }
            catch
            {
                // Privacy by default: no crash dumps or persistent error logs.
            }

            if (settings == null)
            {
                settings = new RigSettings();
            }

            FillMissingDefaults(settings);
            DiscoverPath(settings.LConnect);
            DiscoverPath(settings.Kanali);
            return settings;
        }

        public static void Save(RigSettings settings)
        {
            Directory.CreateDirectory(SettingsDirectory);
            var serializer = new XmlSerializer(typeof(RigSettings));
            var temporaryPath = SettingsPath + ".tmp";
            using (var stream = File.Create(temporaryPath))
            {
                serializer.Serialize(stream, settings);
            }

            if (File.Exists(SettingsPath))
            {
                File.Replace(temporaryPath, SettingsPath, null);
            }
            else
            {
                File.Move(temporaryPath, SettingsPath);
            }
        }

        private static void DiscoverPath(ApplicationTarget target)
        {
            if (target == null || (!string.IsNullOrWhiteSpace(target.ExecutablePath) && File.Exists(target.ExecutablePath)))
            {
                return;
            }

            target.ExecutablePath = InstalledAppLocator.FindExecutable(target.UninstallDisplayName) ?? string.Empty;
        }

        private static void FillMissingDefaults(RigSettings settings)
        {
            var defaults = new RigSettings();
            if (settings.LConnect == null) settings.LConnect = defaults.LConnect;
            if (settings.Kanali == null) settings.Kanali = defaults.Kanali;
            if (settings.LConnect.TurnOn == null) settings.LConnect.TurnOn = new MacroDefinition { Name = "开启" };
            if (settings.LConnect.TurnOff == null) settings.LConnect.TurnOff = new MacroDefinition { Name = "关闭" };
            if (settings.Kanali.TurnOn == null) settings.Kanali.TurnOn = new MacroDefinition { Name = "开启" };
            if (settings.Kanali.TurnOff == null) settings.Kanali.TurnOff = new MacroDefinition { Name = "关闭" };
            if (settings.LConnect.InitialDelayMilliseconds <= 0) settings.LConnect.InitialDelayMilliseconds = defaults.LConnect.InitialDelayMilliseconds;
            if (settings.Kanali.InitialDelayMilliseconds <= 0) settings.Kanali.InitialDelayMilliseconds = defaults.Kanali.InitialDelayMilliseconds;
        }
    }
}
