// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Manamiiii
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace TangerineRigControl.Infrastructure
{
    // Call only while holding both the legacy and current lifecycle locks.
    // Originals are never moved, deleted or rewritten. The receipt detects old-version edits.
    internal sealed class DataLayout
    {
        private readonly string _root;
        private readonly string _legacy;
        private string Receipt { get { return Path.Combine(_root, "Runtime", "legacy-source.sha256"); } }
        private string Pending { get { return Path.Combine(_root, "Runtime", "migration-pending"); } }
        internal string SettingsPath { get { return Path.Combine(_root, "Config", "settings.xml"); } }
        internal string BackupPath { get { return Path.Combine(_root, "Backups", "settings.xml.bak"); } }
        internal string TempDirectory { get { return Path.Combine(_root, "Temp"); } }

        internal DataLayout(string root, string legacy) { _root = root; _legacy = legacy; }

        internal void Prepare(Action afterFirstCopy = null)
        {
            CheckPaths();
            if (Directory.Exists(Pending) || File.Exists(Pending)) Fail("上次目录迁移未完成");
            if (File.Exists(Receipt)) { VerifySource(); return; }
            var files = LegacyFiles();
            if (files.Length == 0) return;
            if (Directory.Exists(Path.Combine(_root, "Config")) || Directory.Exists(Path.Combine(_root, "Backups")))
                Fail("新旧位置同时存在配置且没有迁移记录");
            // Do not use recovery defaults to authorize a migration of damaged/newer files.
            try
            {
                foreach (var file in files.Where(p => p.EndsWith("settings.xml", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)))
                    SettingsFile.Read(File.ReadAllBytes(file));
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is System.Xml.XmlException)) throw;
                Fail("旧配置或备份损坏、不可读或来自更新版本");
            }
            var before = Fingerprint();
            Directory.CreateDirectory(Pending);
            var config = Path.Combine(Pending, "Config");
            var backups = Path.Combine(Pending, "Backups");
            Directory.CreateDirectory(config);
            Directory.CreateDirectory(backups);
            foreach (var file in files)
            {
                var target = Path.Combine(Path.GetFileName(file) == "settings.xml" ? config : backups, Path.GetFileName(file));
                File.Copy(file, target, false);
                if (Hash(file) != Hash(target)) Fail("迁移副本校验失败");
                if (afterFirstCopy != null) { afterFirstCopy(); afterFirstCopy = null; }
            }
            if (before != Fingerprint()) Fail("迁移期间旧配置发生变化");
            // A failed/interrupted commit keeps the pending directory, so a later run fails closed.
            Directory.Move(config, Path.GetDirectoryName(SettingsPath));
            Directory.Move(backups, Path.GetDirectoryName(BackupPath));
            using (var output = new FileStream(Receipt, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(output)) { writer.Write(before); writer.Flush(); output.Flush(true); }
            Directory.Delete(Pending);
        }

        internal void VerifySource()
        {
            CheckPaths();
            if (Directory.Exists(Pending) || File.Exists(Pending)) Fail("上次目录迁移未完成");
            if (File.Exists(Receipt))
            {
                if (File.ReadAllText(Receipt) != Fingerprint()) Fail("迁移后旧配置被修改、删除或新增");
                if (!Directory.Exists(Path.GetDirectoryName(SettingsPath)) || !Directory.Exists(Path.GetDirectoryName(BackupPath)))
                    Fail("迁移后的配置目录缺失");
                if (!File.Exists(SettingsPath) && !File.Exists(BackupPath)) Fail("迁移后的配置与备份均缺失");
            }
            else if (LegacyFiles().Length != 0) Fail("发现未核对的旧配置");
        }

        private string[] LegacyFiles()
        {
            if (!Directory.Exists(_legacy)) return new string[0];
            var files = Directory.GetFiles(_legacy).Where(p => Path.GetFileName(p) != ".lifecycle.lock").OrderBy(p => p, StringComparer.Ordinal).ToArray();
            if (Directory.GetDirectories(_legacy).Length != 0 || files.Any(p => Path.GetFileName(p) != "settings.xml" && Path.GetFileName(p) != "settings.xml.bak" &&
                !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p), "^settings[.]xml[.]recovery-[a-f0-9]{32}$")))
                Fail("旧配置目录含有未识别文件或未完成的保存");
            foreach (var file in files) EnsureOrdinaryPath(file);
            if (files.Length != 0 && !files.Any(p => Path.GetFileName(p) == "settings.xml" || Path.GetFileName(p) == "settings.xml.bak"))
                Fail("旧配置仅剩恢复副本");
            return files;
        }

        private string Fingerprint()
        {
            return string.Join("\n", LegacyFiles().Select(p => Path.GetFileName(p) + " " + Hash(p)));
        }

        private static string Hash(string path)
        {
            using (var hash = SHA256.Create())
            using (var input = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "");
        }

        private void CheckPaths()
        {
            foreach (var path in new[] { _legacy, SettingsPath, BackupPath, Receipt, Pending, TempDirectory }) EnsureOrdinaryPath(path);
        }

        internal static void EnsureOrdinaryPath(string path)
        {
            for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) Fail("数据路径含目录联接或符号链接");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }

        private static void Fail(string reason)
        {
            throw new InvalidOperationException(reason + "。已停止读取和保存，原文件保留。请退出所有版本，备份新旧目录，按 docs/DATA.md 核对恢复；不要删除迁移记录后重试。");
        }
    }
}
