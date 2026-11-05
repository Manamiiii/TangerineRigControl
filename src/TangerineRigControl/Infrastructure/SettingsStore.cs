// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Manamiiii

using System;
using System.IO;
using System.Xml.Serialization;
using TangerineRigControl.Models;

namespace TangerineRigControl.Infrastructure
{
    internal static class SettingsStore
    {
        private static readonly DataLayout Layout = new DataLayout(AppIdentity.DataDirectory, AppIdentity.LegacyDataDirectory);
        internal static string SettingsPath { get { return Layout.SettingsPath; } }

        internal static string LoadWarning { get; private set; }
        private static SettingsFile _fileStore;
        private static SettingsFile FileStore
        {
            get
            {
                if (_fileStore == null)
                {
                    Layout.Prepare();
                    _fileStore = new SettingsFile(SettingsPath, Layout.BackupPath, Layout.TempDirectory, Layout.VerifySource);
                }
                return _fileStore;
            }
        }
        internal static string LoadSource { get { return _fileStore == null ? null : _fileStore.Source; } }
        internal static bool CanSave { get { return _fileStore != null && _fileStore.CanSave; } }

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
            if (settings == null || settings.LConnect == null || settings.Kanali == null || settings.MotherboardLighting == null || settings.GpuLighting == null)
                throw new InvalidOperationException("设备配置不完整。");
            foreach (var target in settings.GetTargets())
            {
                if (target.InitialDelayMilliseconds < 0 || target.InitialDelayMilliseconds > 30000)
                    throw new InvalidOperationException("启动等待时间必须在 0 到 30000 毫秒之间。");
                ValidateMacro(target.TurnOn);
                ValidateMacro(target.TurnOff);
            }
        }

        public static RigSettings Load()
        {
            var settings = FileStore.Load();
            LoadWarning = FileStore.Warning;
#if !INSTALLER_TEST
            foreach (var target in settings.GetTargets()) DiscoverPath(target);
#endif
            return settings;
        }

        public static void Save(RigSettings settings)
        {
            FileStore.Save(settings);
        }

        private static void DiscoverPath(ApplicationTarget target)
        {
            if (target == null || !string.IsNullOrWhiteSpace(target.ExecutablePath))
            {
                return;
            }

            try { target.ExecutablePath = InstalledAppLocator.FindExecutable(target.UninstallDisplayName) ?? string.Empty; }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            catch (ArgumentException) { }
        }

        internal static void FillMissingDefaults(RigSettings settings)
        {
            var defaults = new RigSettings();
            if (settings.LConnect == null) settings.LConnect = defaults.LConnect;
            if (settings.Kanali == null) settings.Kanali = defaults.Kanali;
            if (settings.MotherboardLighting == null) settings.MotherboardLighting = defaults.MotherboardLighting;
            if (settings.GpuLighting == null) settings.GpuLighting = defaults.GpuLighting;
            settings.MotherboardLighting.DisplayName = defaults.MotherboardLighting.DisplayName;
            settings.GpuLighting.DisplayName = defaults.GpuLighting.DisplayName;
            foreach (var target in settings.GetTargets())
            {
                if (target.TurnOn == null) target.TurnOn = new MacroDefinition { Name = "开启" };
                if (target.TurnOff == null) target.TurnOff = new MacroDefinition { Name = "关闭" };
            }
            foreach (var target in new[] { settings.MotherboardLighting, settings.GpuLighting })
                if (!string.IsNullOrWhiteSpace(target.ExecutablePath))
                    target.ProcessName = InstalledAppLocator.WindowProcessName(target.ExecutablePath);
        }
    }

    // Owns a load/save session. Tests use temporary directories, never the user's settings.
    internal sealed class SettingsFile
    {
        private readonly string _path;
        private readonly string _backup, _temp;
        private readonly Action _guard;
        private byte[] _snapshot;
        private bool _loaded;
        private bool _blocked;
        private bool _recovered;
        internal string Warning { get; private set; }
        internal string Source { get; private set; }
        internal bool CanSave { get { return _loaded && !_blocked; } }

        internal SettingsFile(string path, string backup = null, string temp = null, Action guard = null)
        { _path = path; _backup = backup ?? path + ".bak"; _temp = temp ?? Path.GetDirectoryName(path); _guard = guard; }

        internal RigSettings Load()
        {
            if (_guard != null) _guard();
            Warning = null;
            _blocked = false;
            _recovered = false;
            _loaded = true;
            _snapshot = null;
            try
            {
                _snapshot = File.Exists(_path) ? File.ReadAllBytes(_path) : null;
                if (_snapshot != null) { var value = Read(_snapshot); Source = "主配置"; return value; }
                if (!File.Exists(_backup)) { Source = "首次配置"; return new RigSettings(); }
            }
            catch (Exception ex)
            {
                if (ex is NewerSettingsException)
                {
                    _blocked = true;
                    Source = "新版本配置保护";
                    Warning = "配置来自更新版本，不能用旧程序覆盖；请启动对应版本。";
                    return new RigSettings { UseAuraSdk = false };
                }
                if (!IsReadFailure(ex)) throw;
            }
            try
            {
                var value = Read(File.ReadAllBytes(_backup));
                _recovered = true;
                Source = "备份恢复（未保存）";
                Warning = "主配置无法使用，已读取有效备份。保存时会另存故障原文件，不覆盖现有备份。请核对各组步数。";
                return value;
            }
            catch (Exception ex)
            {
                if (!IsReadFailure(ex)) throw;
                _blocked = true;
                Source = "恢复保护";
                Warning = "配置及备份无法安全读取，已进入恢复保护：默认界面不代表录制已删除，保存已禁用。请保留原文件并恢复有效配置后重启；不要重新录制覆盖。";
                return new RigSettings { UseAuraSdk = false };
            }
        }

        private static bool IsReadFailure(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException ||
                ex is System.Xml.XmlException || ex is System.Security.SecurityException || ex is ArgumentException;
        }

        internal static RigSettings Read(byte[] data)
        {
            var document = new System.Xml.XmlDocument { XmlResolver = null };
            using (var stream = new MemoryStream(data))
            using (var reader = System.Xml.XmlReader.Create(stream, new System.Xml.XmlReaderSettings
                { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null })) document.Load(reader);
            RigSettings settings;
            using (var reader = new System.Xml.XmlNodeReader(document))
                settings = (RigSettings)new XmlSerializer(typeof(RigSettings)).Deserialize(reader);
            // Missing flags in pre-SDK files must not change an existing recording backend.
            if (document.DocumentElement.SelectSingleNode("UseAuraSdk") == null) settings.UseAuraSdk = false;
            if (settings.SchemaVersion > RigSettings.CurrentSchemaVersion)
                throw new NewerSettingsException();
            SettingsStore.FillMissingDefaults(settings);
            SettingsStore.Validate(settings);
            settings.SchemaVersion = RigSettings.CurrentSchemaVersion;
            return settings;
        }

        internal void Save(RigSettings settings)
        {
            if (_guard != null) _guard();
            if (!_loaded || _blocked) throw new InvalidOperationException("配置处于恢复保护，不能覆盖保存；请恢复有效配置后重启。");
            SettingsStore.Validate(settings);
            var current = File.Exists(_path) ? File.ReadAllBytes(_path) : null;
            if (!Equal(current, _snapshot)) throw new InvalidOperationException("配置文件已被其他程序修改，请重启后重新读取，当前草稿未覆盖磁盘。");
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            Directory.CreateDirectory(Path.GetDirectoryName(_backup));
            Directory.CreateDirectory(_temp);
            var temporaryPath = Path.Combine(_temp, "settings.xml." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new XmlSerializer(typeof(RigSettings)).Serialize(stream, settings);
                    stream.Flush(true);
                }
                var written = File.ReadAllBytes(temporaryPath);
                if (current != null)
                {
                    if (_recovered)
                    {
                        File.Copy(_path, Path.Combine(Path.GetDirectoryName(_backup), "settings.xml.recovery-" + Guid.NewGuid().ToString("N")), false);
                        File.Replace(temporaryPath, _path, null);
                    }
                    else File.Replace(temporaryPath, _path, _backup);
                }
                else File.Move(temporaryPath, _path);
                _snapshot = written;
                _recovered = false;
                Source = "主配置";
                Warning = null;
            }
            finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        }

        private static bool Equal(byte[] left, byte[] right)
        {
            if (left == null || right == null) return left == right;
            if (left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }

        private sealed class NewerSettingsException : InvalidOperationException { }
    }
}
