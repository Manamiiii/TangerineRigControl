using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace TangerineRigControl.Models
{
    [Serializable]
    public sealed class RigSettings
    {
        public bool StartMinimized { get; set; }
        public bool StartWithWindows { get; set; }
        public bool MinimizeVendorAppsAfterAction { get; set; }
        public bool SignalRgbEnabled { get; set; }
        public ApplicationTarget LConnect { get; set; }
        public ApplicationTarget Kanali { get; set; }

        public RigSettings()
        {
            MinimizeVendorAppsAfterAction = true;
            SignalRgbEnabled = true;
            LConnect = new ApplicationTarget
            {
                DisplayName = "联力副屏",
                UninstallDisplayName = "L-Connect 3",
                ProcessName = "L-Connect 3",
                WindowTitleContains = "L-Connect"
            };
            Kanali = new ApplicationTarget
            {
                DisplayName = "TRYX 曲面屏",
                UninstallDisplayName = "KANALI",
                ProcessName = "KANALI",
                WindowTitleContains = "KANALI"
            };
        }
    }

    [Serializable]
    public sealed class ApplicationTarget
    {
        public string DisplayName { get; set; }
        public string UninstallDisplayName { get; set; }
        public string ExecutablePath { get; set; }
        public string ProcessName { get; set; }
        public string WindowTitleContains { get; set; }
        public int InitialDelayMilliseconds { get; set; }
        public MacroDefinition TurnOn { get; set; }
        public MacroDefinition TurnOff { get; set; }

        public ApplicationTarget()
        {
            InitialDelayMilliseconds = 1200;
            TurnOn = new MacroDefinition { Name = "开启" };
            TurnOff = new MacroDefinition { Name = "关闭" };
        }
    }

    [Serializable]
    public sealed class MacroDefinition
    {
        public string Name { get; set; }

        [XmlArrayItem("Step")]
        public List<MacroStep> Steps { get; set; }

        public MacroDefinition()
        {
            Steps = new List<MacroStep>();
        }
    }

    [Serializable]
    public sealed class MacroStep
    {
        public string AutomationId { get; set; }
        public string ElementName { get; set; }
        public double RelativeX { get; set; }
        public double RelativeY { get; set; }
        public int DelayAfterMilliseconds { get; set; }

        public MacroStep()
        {
            DelayAfterMilliseconds = 900;
        }
    }
}
