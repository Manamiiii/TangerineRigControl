using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TangerineRigControl.Automation;
using TangerineRigControl.Infrastructure;
using TangerineRigControl.Models;

namespace TangerineRigControl.UI
{
    internal sealed class SettingsForm : Form
    {
        private readonly RigSettings _settings;
        private readonly TextBox _lianPath;
        private readonly TextBox _kanaliPath;
        private readonly CheckBox _signalRgb;
        private readonly CheckBox _minimizeApps;

        public SettingsForm(RigSettings settings)
        {
            _settings = settings;
            Text = "TangerineRigControl 设置";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(690, 510);
            MinimumSize = new Size(690, 510);
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

            _signalRgb = new CheckBox
            {
                Text = "启用 SignalRGB 本地接口（仅访问 127.0.0.1）",
                Checked = settings.SignalRgbEnabled,
                Location = new Point(30, 92),
                Size = new Size(430, 26)
            };
            _minimizeApps = new CheckBox
            {
                Text = "操作完成后最小化原厂软件",
                Checked = settings.MinimizeVendorAppsAfterAction,
                Location = new Point(30, 120),
                Size = new Size(330, 26)
            };

            var lianGroup = CreateAppGroup(settings.LConnect, 30, 158, out _lianPath);
            var kanaliGroup = CreateAppGroup(settings.Kanali, 30, 283, out _kanaliPath);

            var save = CreateButton("保存", 500, 423, 72, true);
            save.Click += delegate
            {
                settings.SignalRgbEnabled = _signalRgb.Checked;
                settings.MinimizeVendorAppsAfterAction = _minimizeApps.Checked;
                settings.LConnect.ExecutablePath = _lianPath.Text.Trim();
                settings.Kanali.ExecutablePath = _kanaliPath.Text.Trim();
                SettingsStore.Save(settings);
                DialogResult = DialogResult.OK;
                Close();
            };
            var cancel = CreateButton("取消", 580, 423, 72, false);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(title);
            Controls.Add(privacy);
            Controls.Add(_signalRgb);
            Controls.Add(_minimizeApps);
            Controls.Add(lianGroup);
            Controls.Add(kanaliGroup);
            Controls.Add(save);
            Controls.Add(cancel);
        }

        private GroupBox CreateAppGroup(ApplicationTarget target, int x, int y, out TextBox pathBox)
        {
            var group = new GroupBox
            {
                Text = target.DisplayName,
                Location = new Point(x, y),
                Size = new Size(622, 112),
                ForeColor = Color.White
            };
            pathBox = new TextBox
            {
                Text = target.ExecutablePath ?? string.Empty,
                Location = new Point(16, 27),
                Size = new Size(496, 25),
                ReadOnly = true,
                BackColor = Color.FromArgb(45, 48, 54),
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.FixedSingle
            };
            var browse = CreateButton("浏览…", 520, 25, 82, false);
            var capturedPathBox = pathBox;
            browse.Click += delegate
            {
                using (var dialog = new OpenFileDialog { Filter = "Windows 程序 (*.exe)|*.exe", CheckFileExists = true })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK) capturedPathBox.Text = dialog.FileName;
                }
            };

            var on = CreateButton("录制开启（" + target.TurnOn.Steps.Count + " 步）", 16, 65, 170, false);
            var off = CreateButton("录制关闭（" + target.TurnOff.Steps.Count + " 步）", 196, 65, 170, false);
            var open = CreateButton("打开原厂软件", 432, 65, 170, false);
            on.Click += delegate { Record(target, target.TurnOn, on, "录制开启"); };
            off.Click += delegate { Record(target, target.TurnOff, off, "录制关闭"); };
            open.Click += delegate
            {
                try
                {
                    target.ExecutablePath = capturedPathBox.Text.Trim();
                    new ExternalAppController().Show(new ExternalAppController().OpenAndFindWindow(target, 10000));
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };

            group.Controls.Add(pathBox);
            group.Controls.Add(browse);
            group.Controls.Add(on);
            group.Controls.Add(off);
            group.Controls.Add(open);
            return group;
        }

        private void Record(ApplicationTarget target, MacroDefinition macro, Button button, string prefix)
        {
            target.ExecutablePath = target == _settings.LConnect ? _lianPath.Text.Trim() : _kanaliPath.Text.Trim();
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
    }
}
