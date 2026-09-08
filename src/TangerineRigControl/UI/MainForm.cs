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
        private readonly SignalRgbClient _signalRgb = new SignalRgbClient();
        private readonly MacroRunner _macroRunner = new MacroRunner();
        private readonly ExternalAppController _apps = new ExternalAppController();
        private readonly Label _status;
        private readonly NotifyIcon _trayIcon;
        private bool _allowClose;
        private bool _busy;
        private bool _editing;
        private CancellationTokenSource _operation;
        private string _hotkeyWarning = string.Empty;
        private readonly Button _stop = CreateButton("停止操作", 38, 452, 122, false);

        public MainForm(RigSettings settings)
        {
            _settings = settings;
            Text = "TangerineRigControl";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(760, 530);
            MinimumSize = new Size(760, 530);
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
                Text = "本地运行 · 无遥测 · Ctrl+Alt+F10 开启 · F11 关闭 · F12 显示",
                ForeColor = Color.FromArgb(166, 171, 179),
                Location = new Point(39, 72),
                AutoSize = true
            };

            var allOn = CreateButton("全部开启", 472, 34, 112, true);
            var allOff = CreateButton("全部关闭", 594, 34, 112, false);
            allOn.Click += async delegate { await RunAllAsync(true); };
            allOff.Click += async delegate { await RunAllAsync(false); };

            Controls.Add(CreateDeviceRow("RGB 灯光", "SignalRGB 本地接口", 116,
                async enabled => await SetLightsAsync(enabled), delegate { OpenSignalRgb(); }));
            Controls.Add(CreateDeviceRow("联力副屏", "L-Connect 3", 208,
                async enabled => await RunMacroAsync(_settings.LConnect, enabled), delegate { OpenVendor(_settings.LConnect); }));
            Controls.Add(CreateDeviceRow("TRYX 曲面屏", "KANALI", 300,
                async enabled => await RunMacroAsync(_settings.Kanali, enabled), delegate { OpenVendor(_settings.Kanali); }));

            var settingsButton = CreateButton("设置与录制", 38, 410, 122, false);
            settingsButton.Click += delegate
            {
                if (_busy || _editing) return;
                _editing = true;
                try
                {
                    using (var form = new SettingsForm(_settings))
                    {
                        if (form.ShowDialog(this) == DialogResult.OK) _settings = form.SavedSettings;
                    }
                    SetStatus(InitialStatus());
                }
                finally { _editing = false; }
            };

            _status = new Label
            {
                Text = InitialStatus(),
                Location = new Point(180, 417),
                Size = new Size(526, 70),
                ForeColor = Color.FromArgb(166, 171, 179),
                TextAlign = ContentAlignment.TopRight
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
                Icon = SystemIcons.Application,
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
                _signalRgb.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            var modifiers = NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat;
            var failed = new List<string>();
            if (!NativeMethods.RegisterHotKey(Handle, HotkeyAllOn, modifiers, NativeMethods.VkF10)) failed.Add("F10");
            if (!NativeMethods.RegisterHotKey(Handle, HotkeyAllOff, modifiers, NativeMethods.VkF11)) failed.Add("F11");
            if (!NativeMethods.RegisterHotKey(Handle, HotkeyShow, modifiers, NativeMethods.VkF12)) failed.Add("F12");
            _hotkeyWarning = failed.Count == 0 ? string.Empty : "快捷键注册失败：Ctrl+Alt+" + string.Join(" / ", failed);
            if (_status != null && failed.Count > 0) SetStatus(_hotkeyWarning);
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
            var on = CreateButton("开启", 432, 20, 66, true);
            var off = CreateButton("关闭", 506, 20, 66, false);
            var detail = CreateButton("设置", 580, 20, 66, false);
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
            if (_busy || _editing) return;
            _busy = true;
            _operation = new CancellationTokenSource();
            _stop.Enabled = true;
            SetStatus(enabled ? "正在开启全部设备…" : "正在关闭全部设备…");
            var errors = new List<string>();
            try
            {
                if (_settings.SignalRgbEnabled)
                {
                    try { await _signalRgb.SetEnabledAsync(enabled); }
                    catch (Exception ex) { errors.Add("灯光：" + ex.Message); }
                }
                _operation.Token.ThrowIfCancellationRequested();
                await TryRunMacroAsync(_settings.LConnect, enabled, errors);
                _operation.Token.ThrowIfCancellationRequested();
                await TryRunMacroAsync(_settings.Kanali, enabled, errors);
                _operation.Token.ThrowIfCancellationRequested();
                SetStatus(errors.Count == 0 ? "全部操作已执行；屏幕实际状态待确认" : string.Join("  |  ", errors));
            }
            catch (OperationCanceledException) { SetStatus("操作已停止，部分步骤可能已生效，请确认设备状态。"); }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { EndOperation(); }
        }

        private async Task TryRunMacroAsync(ApplicationTarget target, bool enabled, IList<string> errors)
        {
            var macro = enabled ? target.TurnOn : target.TurnOff;
            if (macro == null || macro.Steps == null || macro.Steps.Count == 0)
            {
                errors.Add(target.DisplayName + "：尚未录制");
                return;
            }

            try
            {
                await Task.Run(delegate { _macroRunner.Run(target, macro, _settings.MinimizeVendorAppsAfterAction, _operation.Token); });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                errors.Add(target.DisplayName + "：" + ex.Message);
            }
        }

        private async Task SetLightsAsync(bool enabled)
        {
            if (!_settings.SignalRgbEnabled)
            {
                SetStatus("SignalRGB 本地接口已在设置中关闭。");
                return;
            }
            await RunBusyAsync(enabled ? "正在开启灯光…" : "正在关闭灯光…",
                async delegate { await _signalRgb.SetEnabledAsync(enabled); }, enabled ? "灯光已开启" : "灯光已关闭");
        }

        private async Task RunMacroAsync(ApplicationTarget target, bool enabled)
        {
            var macro = enabled ? target.TurnOn : target.TurnOff;
            await RunBusyAsync("正在操作" + target.DisplayName + "…",
                async delegate { await Task.Run(delegate { _macroRunner.Run(target, macro, _settings.MinimizeVendorAppsAfterAction, _operation.Token); }); },
                target.DisplayName + (enabled ? "开启" : "关闭") + "操作已执行；实际状态待确认");
        }

        private async Task RunBusyAsync(string progress, Func<Task> action, string success)
        {
            if (_busy || _editing) return;
            _busy = true;
            _operation = new CancellationTokenSource();
            _stop.Enabled = true;
            SetStatus(progress);
            try
            {
                await action();
                _operation.Token.ThrowIfCancellationRequested();
                SetStatus(success);
            }
            catch (OperationCanceledException) { SetStatus("操作已停止，部分步骤可能已生效，请确认设备状态。"); }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
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
        }

        private void OpenVendor(ApplicationTarget target)
        {
            if (_busy || _editing) return;
            try { _apps.Show(_apps.OpenAndFindWindow(target, 10000)); }
            catch (Exception ex) { SetStatus(ex.Message); }
        }

        private void OpenSignalRgb()
        {
            if (_busy || _editing) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("signalrgb://") { UseShellExecute = true });
            }
            catch
            {
                SetStatus("没有找到 SignalRGB，请先安装或从开始菜单打开。");
            }
        }

        private string InitialStatus()
        {
            if (!string.IsNullOrEmpty(_hotkeyWarning)) return _hotkeyWarning;
            var lian = _settings.LConnect.TurnOn.Steps.Count > 0 && _settings.LConnect.TurnOff.Steps.Count > 0;
            var kanali = _settings.Kanali.TurnOn.Steps.Count > 0 && _settings.Kanali.TurnOff.Steps.Count > 0;
            return lian && kanali ? "控制序列已就绪" : "请先进入“设置与录制”校准屏幕开关";
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

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_allowClose || e.CloseReason == CloseReason.WindowsShutDown)
            {
                if (_operation != null) _operation.Cancel();
                return;
            }
            e.Cancel = true;
            Hide();
            _trayIcon.ShowBalloonTip(1200, "TangerineRigControl", "控制台仍在托盘中运行。", ToolTipIcon.Info);
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
