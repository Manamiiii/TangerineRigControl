using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Automation;
using System.Windows.Forms;
using TangerineRigControl.Infrastructure;
using TangerineRigControl.Models;

namespace TangerineRigControl.Automation
{
    internal sealed class MacroRecorderForm : Form
    {
        private const int HotkeyId = 0x5447;
        private readonly ApplicationTarget _target;
        private readonly MacroDefinition _macro;
        private readonly ExternalAppController _apps = new ExternalAppController();
        private readonly List<MacroStep> _steps = new List<MacroStep>();
        private readonly ListBox _stepList = new ListBox();
        private IntPtr _targetWindow;

        public MacroRecorderForm(ApplicationTarget target, MacroDefinition macro)
        {
            _target = target;
            _macro = macro;
            Text = "录制“" + target.DisplayName + " - " + macro.Name + "”";
            StartPosition = FormStartPosition.Manual;
            Location = new Point(Screen.PrimaryScreen.WorkingArea.Right - 390, Screen.PrimaryScreen.WorkingArea.Top + 40);
            Size = new Size(360, 390);
            TopMost = true;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(30, 33, 38);
            ForeColor = Color.White;

            var instructions = new Label
            {
                AutoSize = false,
                Location = new Point(18, 16),
                Size = new Size(315, 92),
                Text = "1. 在原厂软件中回到稳定的起始页面。\r\n" +
                       "2. 鼠标移到要点击的位置，按 F8 记录。\r\n" +
                       "3. 手动点击该位置进入下一页，再重复记录。\r\n" +
                       "4. 完成后点“保存序列”。",
                ForeColor = Color.Gainsboro
            };

            _stepList.Location = new Point(18, 116);
            _stepList.Size = new Size(315, 166);
            _stepList.BackColor = Color.FromArgb(42, 45, 51);
            _stepList.ForeColor = Color.White;
            _stepList.BorderStyle = BorderStyle.FixedSingle;

            var removeButton = CreateButton("删除最后一步", 18, 294, 112);
            removeButton.Click += delegate
            {
                if (_steps.Count == 0) return;
                _steps.RemoveAt(_steps.Count - 1);
                RefreshStepList();
            };

            var saveButton = CreateButton("保存序列", 138, 294, 94);
            saveButton.BackColor = Color.FromArgb(213, 255, 70);
            saveButton.ForeColor = Color.Black;
            saveButton.Click += delegate
            {
                if (_steps.Count == 0)
                {
                    MessageBox.Show(this, "请至少记录一个操作步骤。", "尚未录制",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                _macro.Steps = new List<MacroStep>(_steps);
                DialogResult = DialogResult.OK;
                Close();
            };

            var cancelButton = CreateButton("取消", 240, 294, 93);
            cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(instructions);
            Controls.Add(_stepList);
            Controls.Add(removeButton);
            Controls.Add(saveButton);
            Controls.Add(cancelButton);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                _targetWindow = _apps.OpenAndFindWindow(_target, 10000);
                _apps.Show(_targetWindow);
                NativeMethods.RegisterHotKey(Handle, HotkeyId, NativeMethods.ModNoRepeat, NativeMethods.VkF8);
                Activate();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "无法开始录制", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WmHotkey && message.WParam.ToInt32() == HotkeyId)
            {
                CaptureStep();
                return;
            }
            base.WndProc(ref message);
        }

        private void CaptureStep()
        {
            var screenPoint = new NativeMethods.Point();
            if (!NativeMethods.GetCursorPos(out screenPoint)) return;
            var clientPoint = screenPoint;
            if (!NativeMethods.ScreenToClient(_targetWindow, ref clientPoint)) return;

            NativeMethods.Rect rect;
            if (!NativeMethods.GetClientRect(_targetWindow, out rect)) return;
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0 || clientPoint.X < 0 || clientPoint.Y < 0 || clientPoint.X > width || clientPoint.Y > height)
            {
                MessageBox.Show(this, "鼠标需要位于原厂软件窗口内部。", "未记录",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var step = new MacroStep
            {
                RelativeX = (double)clientPoint.X / width,
                RelativeY = (double)clientPoint.Y / height
            };

            try
            {
                var element = AutomationElement.FromPoint(new System.Windows.Point(screenPoint.X, screenPoint.Y));
                if (element != null)
                {
                    step.AutomationId = SafeCurrentProperty(element, AutomationElement.AutomationIdProperty);
                    step.ElementName = SafeCurrentProperty(element, AutomationElement.NameProperty);
                }
            }
            catch
            {
                // Custom-drawn applications are expected to fall back to relative coordinates.
            }

            _steps.Add(step);
            RefreshStepList();
        }

        private static string SafeCurrentProperty(AutomationElement element, AutomationProperty property)
        {
            try
            {
                var value = element.GetCurrentPropertyValue(property, true);
                return value == AutomationElement.NotSupported ? string.Empty : Convert.ToString(value);
            }
            catch
            {
                return string.Empty;
            }
        }

        private void RefreshStepList()
        {
            _stepList.Items.Clear();
            for (var index = 0; index < _steps.Count; index++)
            {
                var step = _steps[index];
                var hint = !string.IsNullOrWhiteSpace(step.ElementName) ? step.ElementName : "相对坐标";
                _stepList.Items.Add(string.Format("{0}. {1}  ({2:P0}, {3:P0})", index + 1, hint, step.RelativeX, step.RelativeY));
            }
        }

        private static Button CreateButton(string text, int x, int y, int width)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(54, 58, 65),
                ForeColor = Color.White,
                FlatAppearance = { BorderSize = 0 }
            };
        }
    }
}
