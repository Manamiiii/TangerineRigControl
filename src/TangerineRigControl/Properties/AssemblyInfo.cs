// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Manamiiii

using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("TangerineRigControl")]
[assembly: AssemblyDescription("A local Windows control panel for PC lighting and displays.")]
[assembly: AssemblyCompany("Tangerine")]
#if INSTALLER_TEST
[assembly: AssemblyProduct("TangerineRigControl.InstallerTest")]
#else
[assembly: AssemblyProduct("TangerineRigControl")]
#endif
[assembly: AssemblyCopyright("Copyright © 2026 Manamiiii; GPL-3.0-only")]
[assembly: ComVisible(false)]
[assembly: Guid("9A14E2C5-0E9D-4019-812A-0B248F0519B2")]
[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyFileVersion("0.4.1.0")]
[assembly: AssemblyInformationalVersion("0.4.1-preview")]
