using System;
using System.Drawing;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;
using TangerineRigControl.Automation;
using TangerineRigControl.Infrastructure;
using TangerineRigControl.Models;

namespace TangerineRigControl.UI
{
    internal sealed class SettingsForm : Form
    {
        private readonly RigSettings _settings;
        internal RigSettings SavedSettings { get { return _settings; } }
        private readonly Dictionary<ApplicationTarget, TextBox> _paths = new Dictionary<ApplicationTarget, TextBox>();
        private readonly CheckBox _minimizeApps;
        private readonly CheckBox _useAuraSdk;
        private readonly CheckBox _startWithWindows;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private bool _opening;

        public SettingsForm(RigSettings settings)
        {
            settings = SettingsStore.Clone(settings);
            _settings = settings;
            Text = "TangerineRigControl 设置";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(720, 570);
            MinimumSize = new Size(720, 570);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(30, 33, 38);
            ForeColor = Color.White;

            var title = new Label
            {
                Text = "本地设备配置",
                Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold),
                Location = new Point(26, 22),
                AutoSize = true
            };
            var privacy = new Label
            {
                Text = "所有路径和录制步骤仅保存在本机 LocalAppData，不会写入仓库或发送到网络。",
                ForeColor = Color.Silver,
                Location = new Point(28, 58),
                AutoSize = true
            };

            _useAuraSdk = new CheckBox { Text = "华硕主板＋内存使用 Aura SDK（取消则使用录制）", Checked = settings.UseAuraSdk, Location = new Point(30, 92), Size = new Size(590, 26) };
            Controls.Add(_useAuraSdk);
            _minimizeApps = new CheckBox
            {
                Text = "操作完成后最小化原厂软件",
                Checked = settings.MinimizeVendorAppsAfterAction,
                Location = new Point(30, 120),
                Size = new Size(330, 26)
            };
            _startWithWindows = new CheckBox
            {
                Text = "Windows 登录时启动并缩到托盘",
                Checked = StartupManager.IsEnabled(),
                Location = new Point(370, 120),
                Size = new Size(280, 26)
            };

            Controls.Add(new Label { Text = "SDK 关闭需保持控制台运行；开启/退出会释放控制权，恢复默认灯效。\r\n登录启动不自动提权；原厂软件以管理员运行时，请手动以管理员启动控制台。", Location = new Point(30, 152), Size = new Size(650, 44) });
            var tabs = new TabControl { Location = new Point(30, 202), Size = new Size(650, 230), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            foreach (var target in settings.GetTargets())
            {
                var page = new TabPage(target.DisplayName) { BackColor = BackColor, ForeColor = ForeColor, Padding = new Padding(8, 12, 8, 0) };
                TextBox pathBox;
                var group = CreateAppGroup(target, 0, 12, out pathBox);
                group.Dock = DockStyle.Top;
                _paths.Add(target, pathBox);
                page.Controls.Add(group);
                page.Controls.Add(new Label { Text = "只配置需要的设备。等待时间仅修改草稿；点击下方“保存”生效。", Location = new Point(16, 176), AutoSize = true });
                tabs.TabPages.Add(page);
            }
            var save = CreateButton("保存", 500, 483, 72, true);
            save.Enabled = SettingsStore.CanSave;
            save.Click += delegate
            {

                settings.UseAuraSdk = _useAuraSdk.Checked;
                settings.MinimizeVendorAppsAfterAction = _minimizeApps.Checked;
                settings.StartWithWindows = _startWithWindows.Checked;
                try
                {
                    if (_opening) return;
                    foreach (var target in settings.GetTargets()) ApplyPath(target);
                    SettingsStore.Validate(settings);
                    var previousStartup = StartupManager.ReadValue();
                    StartupManager.SetEnabled(_startWithWindows.Checked);
                    try { SettingsStore.Save(settings); }
                    catch
                    {
                        try { StartupManager.RestoreValue(previousStartup); }
                        catch { throw new InvalidOperationException("配置保存失败，且启动项恢复失败。请重新检查 Windows 登录启动选项。"); }
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "保存设置失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            var cancel = CreateButton("取消", 580, 483, 72, false);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            var diagnostics = CreateButton("复制脱敏诊断", 30, 483, 130, false);
            diagnostics.Click += delegate
            {
                try { Clipboard.SetText(BuildSanitizedDiagnostics()); }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    MessageBox.Show(this, "剪贴板暂时被占用，请稍后重试。", "未复制");
                    return;
                }
                MessageBox.Show(this, "诊断信息已复制。内容不包含程序路径、用户名、设备标识或录制坐标。",
                    "已复制", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            Controls.Add(title);
            Controls.Add(privacy);

            Controls.Add(_minimizeApps);
            Controls.Add(_startWithWindows);
            Controls.Add(tabs);
            Controls.Add(new Label
            {
                Text = SettingsStore.CanSave ? "录制和清除仅修改草稿；点击“保存”生效，点击“取消”可撤销。" : "配置恢复保护中：请先恢复有效配置后重启，当前禁止保存。",
                Location = new Point(30, 450),
                AutoSize = true,
                ForeColor = Color.Silver
            });
            Controls.Add(save);
            Controls.Add(cancel);
            Controls.Add(diagnostics);
        }

        private GroupBox CreateAppGroup(ApplicationTarget target, int x, int y, out TextBox pathBox)
        {
            var group = new GroupBox
            {
                Text = target.DisplayName,
                Location = new Point(x, y),
                Size = new Size(622, 154),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.White
            };
            pathBox = new TextBox
            {
                Text = target.ExecutablePath ?? string.Empty,
                Location = new Point(16, 27),
                Size = new Size(496, 25),
                ReadOnly = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(45, 48, 54),
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.FixedSingle
            };
            var browse = CreateButton("浏览…", 520, 25, 82, false);
            browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var capturedPathBox = pathBox;
            browse.Click += delegate
            {
                using (var dialog = new OpenFileDialog { Filter = "Windows 程序 (*.exe)|*.exe", CheckFileExists = true })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        if (!string.Equals(capturedPathBox.Text, dialog.FileName, StringComparison.OrdinalIgnoreCase) &&
                            (target.TurnOn.Steps.Count > 0 || target.TurnOff.Steps.Count > 0))
                        {
                            if (MessageBox.Show(this, "已有坐标将保留。新程序的页面布局可能不同，必须复核序列后再执行。是否更换路径？", "更换原厂程序", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                        }
                        capturedPathBox.Text = dialog.FileName;
                    }
                }
            };

            var on = CreateButton("录制开启（" + target.TurnOn.Steps.Count + " 步）", 16, 65, 130, false);
            var clearOn = CreateButton("清除", 150, 65, 52, false);
            var timeOn = CreateButton("等待…", 206, 65, 82, false);
            var off = CreateButton("录制关闭（" + target.TurnOff.Steps.Count + " 步）", 316, 65, 130, false);
            var clearOff = CreateButton("清除", 450, 65, 52, false);
            var timeOff = CreateButton("等待…", 506, 65, 82, false);
            timeOn.AccessibleName = target.DisplayName + "开启序列等待时间";
            timeOff.AccessibleName = target.DisplayName + "关闭序列等待时间";
            timeOn.Click += delegate { EditTiming(target.TurnOn); };
            timeOff.Click += delegate { EditTiming(target.TurnOff); };
            var open = CreateButton("打开原厂软件", 16, 109, 146, false);
            group.Controls.Add(new Label { Text = "首次点击前等待（毫秒）", Location = new Point(182, 118), AutoSize = true });
            var initialDelay = new NumericUpDown
            {
                Minimum = 0, Maximum = 30000, Increment = 100, Value = target.InitialDelayMilliseconds,
                Location = new Point(356, 114), Size = new Size(100, 26),
                AccessibleName = target.DisplayName + "首次点击前等待毫秒"
            };
            initialDelay.ValueChanged += delegate { target.InitialDelayMilliseconds = (int)initialDelay.Value; };
            group.Controls.Add(initialDelay);
            on.Click += delegate { Record(target, target.TurnOn, on, "录制开启"); };
            off.Click += delegate { Record(target, target.TurnOff, off, "录制关闭"); };
            clearOn.AccessibleName = target.DisplayName + "清除开启序列";
            clearOff.AccessibleName = target.DisplayName + "清除关闭序列";
            clearOn.Click += delegate
            {
                target.TurnOn.Steps.Clear();
                on.Text = "录制开启（0 步）";
            };
            clearOff.Click += delegate
            {
                target.TurnOff.Steps.Clear();
                off.Text = "录制关闭（0 步）";
            };
            open.Click += async delegate
            {
                if (_opening) return;
                _opening = true;
                open.Enabled = false;
                var token = _lifetime.Token;
                try
                {
                    ApplyPath(target);
                    var apps = new ExternalAppController();
                    var launchTarget = new ApplicationTarget { DisplayName = target.DisplayName, ExecutablePath = target.ExecutablePath,
                        ProcessName = target.ProcessName, WindowTitleContains = target.WindowTitleContains };
                    await Task.Run(delegate { apps.Show(apps.OpenAndFindWindow(launchTarget, 10000, token), token); });
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "无法打开", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                finally { _opening = false; if (!IsDisposed) open.Enabled = true; else _lifetime.Dispose(); }
            };

            group.Controls.Add(pathBox);
            group.Controls.Add(browse);
            group.Controls.Add(on);
            group.Controls.Add(clearOn);
            group.Controls.Add(off);
            group.Controls.Add(clearOff);
            group.Controls.Add(timeOn);
            group.Controls.Add(timeOff);
            group.Controls.Add(open);
            return group;
        }

        private void ApplyPath(ApplicationTarget target)
        {
            target.ExecutablePath = _paths[target].Text.Trim();
            if (target == _settings.MotherboardLighting || target == _settings.GpuLighting)
                target.ProcessName = InstalledAppLocator.WindowProcessName(target.ExecutablePath);
        }

        private void EditTiming(MacroDefinition macro)
        {
            if (_opening) return;
            if (macro.Steps.Count == 0)
            {
                MessageBox.Show(this, "请先录制对应序列，再调整等待时间。", "尚未录制", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var form = new MacroTimingForm(macro)) form.ShowDialog(this);
        }

        private void Record(ApplicationTarget target, MacroDefinition macro, Button button, string prefix)
        {
            if (_opening) return;
            ApplyPath(target);
            using (var recorder = new MacroRecorderForm(target, macro))
            {
                if (recorder.ShowDialog(this) == DialogResult.OK)
                {
                    button.Text = prefix + "（" + macro.Steps.Count + " 步）";
                }
            }
        }

        private static Button CreateButton(string text, int x, int y, int width, bool accent)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = accent ? Color.FromArgb(213, 255, 70) : Color.FromArgb(54, 58, 65),
                ForeColor = accent ? Color.Black : Color.White,
                FlatAppearance = { BorderSize = 0 }
            };
        }

        private string BuildSanitizedDiagnostics()
        {
            var builder = new StringBuilder();
            builder.AppendLine("TangerineRigControl " + System.Diagnostics.FileVersionInfo.GetVersionInfo(Application.ExecutablePath).ProductVersion);
            builder.AppendLine("OS: " + Environment.OSVersion.VersionString);
            builder.AppendLine("配置来源：" + SettingsStore.LoadSource + "；当前数字来自设置草稿，尚未保存。");
            builder.AppendLine("Aura SDK selected: " + _useAuraSdk.Checked);
            var names = new[] { "LConnect", "Kanali", "Motherboard", "Gpu" };
            var index = 0;
            foreach (var target in _settings.GetTargets())
            {
                var name = names[index++];
                builder.AppendLine(name + " executable exists: " + (File.Exists(_paths[target].Text) ? "yes" : "no"));
                builder.AppendLine(name + " macros: on=" + target.TurnOn.Steps.Count + ", off=" + target.TurnOff.Steps.Count);
            }
            builder.AppendLine("Start with Windows: " + (_startWithWindows.Checked ? "yes" : "no"));
            return builder.ToString();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _lifetime.Cancel();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (!_opening) _lifetime.Dispose();
            base.OnFormClosed(e);
        }
    }
}
