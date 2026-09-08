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

        internal static string LoadWarning { get; private set; }

        internal static RigSettings Clone(RigSettings settings)
        {
            var serializer = new XmlSerializer(typeof(RigSettings));
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, settings);
                stream.Position = 0;
                return (RigSettings)serializer.Deserialize(stream);
            }
        }

        internal static void ValidateMacro(MacroDefinition macro)
        {
            if (macro == null || macro.Steps == null || macro.Steps.Count > 200)
                throw new InvalidOperationException("操作序列无效或超过 200 步，请重新录制。");
            foreach (var step in macro.Steps)
            {
                if (step == null || !ValidCoordinate(step.RelativeX) || !ValidCoordinate(step.RelativeY) ||
                    step.DelayAfterMilliseconds < 0 || step.DelayAfterMilliseconds > 30000)
                    throw new InvalidOperationException("操作序列包含无效坐标或等待时间，请重新录制。");
            }
        }

        private static bool ValidCoordinate(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value < 1;
        }

        internal static void Validate(RigSettings settings)
        {
            if (settings == null || settings.LConnect == null || settings.Kanali == null)
                throw new InvalidOperationException("设备配置不完整。");
            foreach (var target in new[] { settings.LConnect, settings.Kanali })
            {
                if (target.InitialDelayMilliseconds < 0 || target.InitialDelayMilliseconds > 30000)
                    throw new InvalidOperationException("启动等待时间必须在 0 到 30000 毫秒之间。");
                ValidateMacro(target.TurnOn);
                ValidateMacro(target.TurnOff);
            }
        }

        public static RigSettings Load()
        {
            LoadWarning = null;
            RigSettings settings = null;
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var serializer = new XmlSerializer(typeof(RigSettings));
                    using (var stream = File.OpenRead(SettingsPath))
                    {
                        settings = serializer.Deserialize(stream) as RigSettings;
                        if (settings == null) throw new InvalidOperationException();
                        FillMissingDefaults(settings);
                        Validate(settings);
                    }
                }
            }
            catch
            {
                settings = null;
                LoadWarning = "本地配置无法读取或内容无效，已使用默认配置。原文件仍保留；保存时会创建本地备份。";
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
            Validate(settings);
            Directory.CreateDirectory(SettingsDirectory);
            var serializer = new XmlSerializer(typeof(RigSettings));
            var temporaryPath = SettingsPath + ".tmp";
            using (var stream = File.Create(temporaryPath))
            {
                serializer.Serialize(stream, settings);
            }

            if (File.Exists(SettingsPath))
            {
                File.Replace(temporaryPath, SettingsPath, SettingsPath + ".bak");
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
