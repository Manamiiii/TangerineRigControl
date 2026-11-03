// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Manamiiii

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using TangerineRigControl.Automation;
using TangerineRigControl.Infrastructure;
using TangerineRigControl.Models;
using TangerineRigControl.Services;


namespace TangerineRigControl.UI
{
    internal sealed class MainForm : Form
    {
        private const int HotkeyAllOn = 0x5450;
        private const int HotkeyAllOff = 0x5451;
        private const int HotkeyShow = 0x5452;
        private RigSettings _settings;
        private readonly AuraSession _aura = new AuraSession();

        private readonly MacroRunner _macroRunner = new MacroRunner();
        private readonly ExternalAppController _apps = new ExternalAppController();
        private readonly TextBox _status;
        private bool _shutdownStarted;
        private bool _shutdownReady;
        private readonly NotifyIcon _trayIcon;
        private bool _allowClose;
        private bool _busy;
        private bool _editing;
        private CancellationTokenSource _operation;
        private string _hotkeyWarning = string.Empty;
        private readonly List<string> _completedResults = new List<string>();
        private readonly Button _stop = CreateButton("停止操作", 38, 544, 122, false);

        public MainForm(RigSettings settings)
        {
            _settings = settings;
            Text = "TangerineRigControl";
            using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("TangerineRigControl.AppIcon"))
            using (var source = new Icon(stream))
                Icon = (Icon)source.Clone();
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(760, 700);
            MinimumSize = new Size(760, 700);
            BackColor = Color.FromArgb(25, 28, 33);
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F);

            var accent = new Panel
            {
                Dock = DockStyle.Left,
                Width = 8,
                BackColor = Color.FromArgb(213, 255, 70)
            };
            var title = new Label
            {
                Text = "主机灯光与屏幕控制",
                Font = new Font("Microsoft YaHei UI", 20, FontStyle.Bold),
                Location = new Point(36, 28),
                AutoSize = true
            };
            var subtitle = new Label
            {
                Text = "本地运行 · 无遥测 · 快捷键均需 Ctrl+Alt：F10 开启 / F11 关闭 / F12 显示",
                ForeColor = Color.FromArgb(166, 171, 179),
                Location = new Point(39, 72),
                AutoSize = true
            };

            var allOn = CreateButton("全部开启", 472, 34, 112, true);
            var allOff = CreateButton("全部关闭", 594, 34, 112, false);
            allOn.Click += async delegate { await RunAllAsync(true); };
            allOff.Click += async delegate { await RunAllAsync(false); };

            Controls.Add(CreateDeviceRow("联力副屏", "L-Connect 3 · 坐标录制", 116,
                async enabled => await RunDeviceAsync(_settings.LConnect, enabled), delegate { OpenVendor(_settings.LConnect); }));
            Controls.Add(CreateDeviceRow("TRYX 曲面屏", "KANALI · 坐标录制", 208,
                async enabled => await RunDeviceAsync(_settings.Kanali, enabled), delegate { OpenVendor(_settings.Kanali); }));
            Controls.Add(CreateDeviceRow("主板＋内存灯光", "Aura SDK / 录制备用", 300,
                async enabled => await RunDeviceAsync(_settings.MotherboardLighting, enabled), delegate { OpenVendor(_settings.MotherboardLighting); }));
            Controls.Add(CreateDeviceRow("显卡灯光", "iGC.Lite · 坐标录制", 392,
                async enabled => await RunDeviceAsync(_settings.GpuLighting, enabled), delegate { OpenVendor(_settings.GpuLighting); }));
            var settingsButton = CreateButton("设置与录制", 38, 502, 122, false);
            settingsButton.Click += async delegate
            {
                if (_busy || _editing) return;
                _editing = true;
                try
                {
                    using (var form = new SettingsForm(_settings))
                    {
                        if (form.ShowDialog(this) == DialogResult.OK)
                        {
                            var releaseAura = _settings.UseAuraSdk && !form.SavedSettings.UseAuraSdk;
                            _settings = form.SavedSettings;
                            if (releaseAura) await Task.Run(delegate { _aura.ReleaseExisting(); });
                        }
                    }
                    SetStatus(InitialStatus());
                }
                catch (Exception ex) { SetStatus(ex.Message); }
                finally { _editing = false; }
            };

            _status = new TextBox
            {
                Text = InitialStatus(),
                Location = new Point(180, 509),
                Size = new Size(526, 140),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = BackColor,
                BorderStyle = BorderStyle.None,
                ForeColor = Color.FromArgb(166, 171, 179),
                TabStop = true
            };

            Controls.Add(accent);
            Controls.Add(title);
            Controls.Add(subtitle);
            Controls.Add(allOn);
            Controls.Add(allOff);
            Controls.Add(settingsButton);
            Controls.Add(_status);
            _stop.Enabled = false;
            _stop.Click += delegate { if (_operation != null) _operation.Cancel(); };
            Controls.Add(_stop);

            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("显示控制台", null, delegate { RestoreFromTray(); });
            trayMenu.Items.Add("全部开启", null, async delegate { await RunAllAsync(true); });
            trayMenu.Items.Add("全部关闭", null, async delegate { await RunAllAsync(false); });
            trayMenu.Items.Add("停止操作", null, delegate { if (_operation != null) _operation.Cancel(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("退出", null, delegate { _allowClose = true; Close(); });
            _trayIcon = new NotifyIcon
            {
                Text = "TangerineRigControl",
                Icon = Icon,
                Visible = true,
                ContextMenuStrip = trayMenu
            };
            _trayIcon.DoubleClick += delegate { RestoreFromTray(); };

            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized) Hide();
            };
            FormClosing += OnFormClosing;
            Shown += delegate
            {
                if (_settings.StartMinimized)
                {
                    WindowState = FormWindowState.Minimized;
                    Hide();
                }
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_operation != null) _operation.Cancel();
                if (_trayIcon != null) _trayIcon.Dispose();
                _aura.Dispose();

            }
            base.Dispose(disposing);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
#if !INSTALLER_TEST
            var modifiers = NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat;
            var failed = new List<string>();
            if (!NativeMethods.RegisterHotKey(Handle, HotkeyAllOn, modifiers, NativeMethods.VkF10)) failed.Add("F10");
            if (!NativeMethods.RegisterHotKey(Handle, HotkeyAllOff, modifiers, NativeMethods.VkF11)) failed.Add("F11");
            if (!NativeMethods.RegisterHotKey(Handle, HotkeyShow, modifiers, NativeMethods.VkF12)) failed.Add("F12");
            _hotkeyWarning = failed.Count == 0 ? string.Empty : "快捷键注册失败：Ctrl+Alt+" + string.Join(" / ", failed);
            if (_status != null && failed.Count > 0) SetStatus(_hotkeyWarning);
#endif
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyAllOn);
            NativeMethods.UnregisterHotKey(Handle, HotkeyAllOff);
            NativeMethods.UnregisterHotKey(Handle, HotkeyShow);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WmHotkey)
            {
                var id = message.WParam.ToInt32();
                if (id == HotkeyAllOn)
                {
                    RunAllFromHotkey(true);
                    return;
                }
                if (id == HotkeyAllOff)
                {
                    RunAllFromHotkey(false);
                    return;
                }
                if (id == HotkeyShow)
                {
                    RestoreFromTray();
                    return;
                }
            }
            base.WndProc(ref message);
        }

        private async void RunAllFromHotkey(bool enabled)
        {
            await RunAllAsync(enabled);
        }

        private Control CreateDeviceRow(string name, string description, int y, Func<bool, Task> toggle, Action open)
        {
            var panel = new Panel
            {
                Location = new Point(38, y),
                Size = new Size(668, 76),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(35, 39, 45)
            };
            panel.Controls.Add(new Label
            {
                Text = name,
                Font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold),
                Location = new Point(18, 14),
                AutoSize = true
            });
            panel.Controls.Add(new Label
            {
                Text = description,
                ForeColor = Color.FromArgb(153, 159, 168),
                Location = new Point(20, 43),
                AutoSize = true
            });
            var on = CreateButton("开启", 392, 20, 66, true);
            var off = CreateButton("关闭", 466, 20, 66, false);
            var detail = CreateButton("原厂软件", 540, 20, 106, false);
            on.Anchor = off.Anchor = detail.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            on.Click += async delegate { if (!_busy) await toggle(true); };
            off.Click += async delegate { if (!_busy) await toggle(false); };
            detail.Click += delegate { open(); };
            panel.Controls.Add(on);
            panel.Controls.Add(off);
            panel.Controls.Add(detail);
            return panel;
        }

        private async Task RunAllAsync(bool enabled)
        {
            await RunBusyAsync(enabled ? "正在开启全部设备…" : "正在关闭全部设备…", async delegate
            {
                foreach (var target in _settings.GetExecutionTargets())
                {
                    _operation.Token.ThrowIfCancellationRequested();
                    SetStatus(WithCompletedResults("正在操作" + target.DisplayName + "…"));
                    await TryRunDeviceAsync(target, enabled, _completedResults);
                }
                return string.Join(Environment.NewLine, _completedResults);
            });
        }

        private async Task TryRunDeviceAsync(ApplicationTarget target, bool enabled, IList<string> errors)
        {
            var macro = enabled ? target.TurnOn : target.TurnOff;
            if (!(_settings.UseAuraSdk && target == _settings.MotherboardLighting) && (macro == null || macro.Steps == null || macro.Steps.Count == 0))
            {
                errors.Add(target.DisplayName + "：已跳过，请在“设置与录制”中录制" + (enabled ? "开启" : "关闭") + "序列。");
                return;
            }

            try
            {
                var result = await Task.Run(delegate { return RunTarget(target, macro, enabled); });
                errors.Add(target.DisplayName + "：" + result);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                if (_operation.IsCancellationRequested) throw;
                errors.Add(target.DisplayName + "：" + ex.Message);
            }
        }

        private string RunTarget(ApplicationTarget target, MacroDefinition macro, bool enabled)
        {
            if (_settings.UseAuraSdk && target == _settings.MotherboardLighting)
                return _aura.SetEnabled(enabled, _operation.Token);
            else
            {
                _macroRunner.Run(target, macro, _settings.MinimizeVendorAppsAfterAction, _operation.Token);
                return "序列已执行；实际状态待确认";
            }
        }

        private async Task RunDeviceAsync(ApplicationTarget target, bool enabled)
        {
            var macro = enabled ? target.TurnOn : target.TurnOff;
            await RunBusyAsync("正在操作" + target.DisplayName + "…",
                async delegate { return await Task.Run(delegate { return RunTarget(target, macro, enabled); }); });
        }

        private async Task RunBusyAsync(string progress, Func<Task<string>> action)
        {
            if (_busy || _editing) return;
            _busy = true;
            _completedResults.Clear();
            _operation = new CancellationTokenSource();
            _stop.Enabled = true;
            SetActionButtonsEnabled(false);
            SetStatus(progress);
            try
            {
                var success = await action();
                _operation.Token.ThrowIfCancellationRequested();
                SetStatus(success);
            }
            catch (OperationCanceledException) { SetStatus(WithCompletedResults("操作已停止，部分步骤可能已生效，请确认设备状态。")); }
            catch (Exception ex)
            {
                SetStatus(WithCompletedResults(ex.Message));
            }
            finally
            {
                EndOperation();
            }
        }

        private void EndOperation()
        {
            _busy = false;
            if (_operation != null) { _operation.Dispose(); _operation = null; }
            if (!IsDisposed) _stop.Enabled = false;
            if (!IsDisposed) SetActionButtonsEnabled(true);
        }

        private async void OpenVendor(ApplicationTarget target)
        {
            await RunBusyAsync("正在打开" + target.DisplayName + "…", async delegate
            {
                await Task.Run(delegate { _apps.Show(_apps.OpenAndFindWindow(target, 10000, _operation.Token), _operation.Token); });
                return "原厂软件已打开。";
            });
        }

        private string InitialStatus()
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(_hotkeyWarning)) lines.Add(_hotkeyWarning);
            lines.Add("先在“设置与录制”配置需要的设备，再单项验证开关。");
            foreach (var target in _settings.GetExecutionTargets())
                lines.Add(target.DisplayName + "：" + ConfigurationStatus(_settings, target));
            return string.Join(Environment.NewLine, lines);
        }

        internal static string ConfigurationStatus(RigSettings settings, ApplicationTarget target)
        {
            if (settings.UseAuraSdk && target == settings.MotherboardLighting)
                return "已选择 Aura SDK，尚未验证连接或灯效。";
            if (target.TurnOn.Steps.Count == 0 && target.TurnOff.Steps.Count == 0)
                return "尚未录制；无需使用的设备可留空。";
            if (string.IsNullOrWhiteSpace(target.ExecutablePath) || !System.IO.File.Exists(target.ExecutablePath))
                return "已有录制，原厂程序路径待配置或不可用。";
            return "开启 " + target.TurnOn.Steps.Count + " 步 / 关闭 " + target.TurnOff.Steps.Count + " 步；实际效果待确认。";
        }

        private string WithCompletedResults(string message)
        {
            return _completedResults.Count == 0 ? message : string.Join(Environment.NewLine, _completedResults) + Environment.NewLine + message;
        }

        private void SetStatus(string text)
        {
            if (IsDisposed || Disposing) return;
            _status.Text = text;
            if (_trayIcon != null) _trayIcon.Text = text.Length > 63 ? text.Substring(0, 60) + "…" : text;
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private async void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_allowClose || e.CloseReason == CloseReason.WindowsShutDown)
            {
                if (_shutdownReady) return;
                e.Cancel = true;
                if (_shutdownStarted) return;
                _shutdownStarted = true;
                _editing = true;
                SetActionButtonsEnabled(false);
                if (_operation != null) _operation.Cancel();
                SetStatus("正在释放 SDK 控制权…");
                await Task.Yield();
                var released = await _aura.CloseAsync(3000);
                if (!released)
                {
                    SetStatus("SDK 未确认释放，底层调用可能仍在执行；退出后请检查原厂灯效。");
                    await Task.Delay(1500);
                }
                _shutdownReady = true;
                _allowClose = true;
                Close();
                return;
            }
            e.Cancel = true;
            Hide();
            _trayIcon.ShowBalloonTip(1200, "TangerineRigControl", "控制台仍在托盘中运行。", ToolTipIcon.Info);
        }

        private void SetActionButtonsEnabled(bool enabled)
        {
            enabled = enabled && !_shutdownStarted;
            foreach (Control control in Controls)
            {
                if (control is Button && control != _stop) control.Enabled = enabled;
                foreach (Control child in control.Controls) if (child is Button) child.Enabled = enabled;
            }
        }

        private static Button CreateButton(string text, int x, int y, int width, bool accent)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = accent ? Color.FromArgb(213, 255, 70) : Color.FromArgb(54, 58, 65),
                ForeColor = accent ? Color.Black : Color.White,
                FlatAppearance = { BorderSize = 0 },
                Cursor = Cursors.Hand
            };
        }
    }
}
