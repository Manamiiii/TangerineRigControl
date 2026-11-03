using System;
using System.Drawing;
using System.Windows.Forms;
using TangerineRigControl.Infrastructure;
using TangerineRigControl.Models;

namespace TangerineRigControl.UI
{
    // Edits only timings. Coordinates and compatibility fields remain untouched.
    internal sealed class MacroTimingForm : Form
    {
        private readonly MacroDefinition _macro;
        private readonly int[] _delays;
        private readonly ListBox _steps = new ListBox { Name = "steps" };
        private readonly NumericUpDown _delay = new NumericUpDown { Name = "delay", Minimum = 0, Maximum = 30000, Increment = 100 };
        private bool _selecting;

        internal MacroTimingForm(MacroDefinition macro)
        {
            SettingsStore.ValidateMacro(macro);
            _macro = macro;
            _delays = new int[macro.Steps.Count];
            for (var index = 0; index < _delays.Length; index++) _delays[index] = macro.Steps[index].DelayAfterMilliseconds;

            Text = macro.Name + "序列 · 等待时间";
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(440, 350);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(30, 33, 38);
            ForeColor = Color.White;
            Controls.Add(new Label { Text = "选择步骤，调整本次点击后的等待（毫秒）。\r\n慢页面请增加前一步的等待；不会执行任何点击。", Location = new Point(18, 16), Size = new Size(404, 44) });
            _steps.Location = new Point(18, 68);
            _steps.Size = new Size(404, 172);
            for (var index = 0; index < _delays.Length; index++) _steps.Items.Add(StepText(index));
            _steps.SelectedIndexChanged += delegate
            {
                if (_selecting || _steps.SelectedIndex < 0) return;
                _selecting = true;
                try { _delay.Value = _delays[_steps.SelectedIndex]; }
                finally { _selecting = false; }
            };
            Controls.Add(_steps);
            Controls.Add(new Label { Text = "点击后等待", Location = new Point(18, 252), AutoSize = true });
            _delay.Location = new Point(110, 248);
            _delay.Size = new Size(112, 26);
            _delay.Enabled = _delays.Length > 0;
            _delay.ValueChanged += delegate
            {
                if (_selecting || _steps.SelectedIndex < 0) return;
                var index = _steps.SelectedIndex;
                _delays[index] = (int)_delay.Value;
                _selecting = true;
                try { _steps.Items[index] = StepText(index); }
                finally { _selecting = false; }
            };
            Controls.Add(_delay);
            Controls.Add(new Label { Text = "毫秒（0～30000）", Location = new Point(234, 252), AutoSize = true });
            var save = new Button { Text = "应用到草稿", Name = "apply", Location = new Point(198, 300), Size = new Size(112, 32), Enabled = _delays.Length > 0 };
            save.Click += delegate { ApplyChanges(); DialogResult = DialogResult.OK; Close(); };
            var cancel = new Button { Text = "取消", Location = new Point(320, 300), Size = new Size(102, 32), DialogResult = DialogResult.Cancel };
            foreach (var button in new[] { save, cancel })
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 0;
                button.BackColor = Color.FromArgb(54, 58, 65);
                button.ForeColor = Color.White;
            }
            Controls.Add(save);
            Controls.Add(cancel);
            CancelButton = cancel;
            if (_delays.Length > 0) _steps.SelectedIndex = 0;
        }

        internal void ApplyChanges()
        {
            for (var index = 0; index < _delays.Length; index++) _macro.Steps[index].DelayAfterMilliseconds = _delays[index];
        }

        private string StepText(int index)
        {
            return "第 " + (index + 1) + " 步：点击后等待 " + _delays[index] + " 毫秒";
        }
    }
}
