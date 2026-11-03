# Windows 安装版

本项目是 WinForms / .NET Framework 桌面工具，没有网页版。当前只提供 Windows 10/11 x64；打包文件集中在 `packaging/windows/`，便于未来单独添加其他平台的实现，但 macOS、Linux 和 Windows ARM64 尚未支持或验证。

## 用户安装与更新

- `TangerineRigControl-<版本>-windows-x64-Setup.exe`：当前用户安装，不请求管理员权限；固定安装到 `%LOCALAPPDATA%\Programs\TangerineRigControl`，添加当前用户开始菜单快捷方式和 Windows 卸载入口。
- `TangerineRigControl-<版本>-windows-portable.zip`：保留免安装使用方式。
- 两者使用相同的应用身份和 `%LOCALAPPDATA%\TangerineRigControl` 配置，不要同时运行。源码不预置个人安装路径或录制坐标。
- 需要 .NET Framework 4.8/4.8.1 和对应原厂软件。安装器不联网下载组件，不安装服务，不设置自动启动，也不启动程序；安装结束后从开始菜单打开。
- 更新：从托盘退出程序，备份配置，下载并核对新版 Setup 的 SHA-256，然后手动运行。已有版本直接覆盖程序文件，保留配置；安装器拒绝降级。无后台自动更新。
- 卸载：从托盘退出，在 Windows“已安装的应用”中卸载。保留配置、备份及额外放入安装目录的文件；只删除指向当前安装 EXE 的本应用登录启动项。重装后读取原配置。
- 从便携版切换：先取消便携版的登录启动并保存、退出；安装后按需要重新设置登录启动。不要同时删除数据目录。
- 运行中安装/卸载会拒绝继续，不强杀进程；关窗口只是隐藏到托盘。磁盘不足或权限问题时会提示失败，配置不会被删除；修复环境后重新运行安装器。
- 首版未签名。核对仓库来源和 SHA-256，不关闭系统安全保护。尚未进行干净 Windows 普通账户验收，也不代表真实设备已验收。

## 构建

先按 [开发说明](DEVELOPMENT.md) 构建 Release x64 并运行 `--self-test`。安装器复用便携包的源码及文档白名单，不捆绑用户配置、PDB 或厂商 DLL。

使用固定的便携 NSIS `nsis-3.0.4.1`（NSIS 自报 3.04），不需要安装全局工具或管理员权限：

- 来源：https://github.com/electron-userland/electron-builder-binaries/releases/download/nsis-3.0.4.1/nsis-3.0.4.1.7z
- 压缩包 SHA-256：`9877df902530f96357d13a7a31ae2b9df67f48b11ffc9a1700a7c961574ec5fa`
- 根目录 `makensis.exe` SHA-256：`f2b2b7726ac0d4e720dff52bfca11a5518d550fc75ed34a48dc47921527293f0`

已有 electron-builder 缓存会自动发现；也可显式传 `-MakensisPath`。新电脑需先获取并解压上述构建工具；普通用户运行 Setup 不需要它。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Package-Windows.ps1
```

输出在 `artifacts/windows`：Setup、Portable 和各自 `.sha256`，同名产物不会被覆盖。`-ExecutablePath`、`-OutputDirectory` 可指定其他位置。Setup 包含相应 GPL 源码及构建材料；NSIS 第三方声明见 [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md)。

正式 ID：`Manamiiii.TangerineRigControl`；卸载 GUID：`{D9BC8BD8-3A19-4798-A233-18C4B5B62BE4}`。请勿随版本改变。应用与安装器共同使用旧单实例互斥量和本用户数据目录的独占文件锁，避免安装期间新开程序；不修改 XML schema。

## 自动验证范围

`INSTALLER_TEST` 条件编译生成独立的 `TangerineRigControl.InstallerTest`：独立数据目录、启动项、互斥量、AppUserModelID 和卸载 GUID。它禁用全局快捷键与原厂路径发现，只用虚构 XML，显示实际主窗口并按测试信号正常退出。正式二进制不包含测试启动模式。

CI 编译独立 fixture，分别打包低版本号与当前版本号的 Setup，运行 `tools/Test-Installer.ps1 -OldSetup <旧fixture> -NewSetup <新fixture>`。检查安装、窗口启动、保存重开、运行中拒绝安装/卸载、升级保留 XML、拒绝降级、卸载保留数据、重装恢复、启动项和额外文件保护。已有 fixture 身份时拒绝测试，不覆盖前一次遗留数据。

低版本 fixture 仅验证安装器的升级流程，不代表真实历史业务迁移。正式包会运行已有自检并检查隐私和打包清单；正式身份的安装生命周期、交互向导及干净 Windows 普通账户验收须单独记录。JSON、浏览器存储、OCR 与 Web 安全边界不适用于此原生工具。

CI 不创建 Release，也不上传公开安装包；只保留检查日志。公开发布仍需人工决定。
