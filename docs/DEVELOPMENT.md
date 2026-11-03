# 构建、自检与打包

普通用户请先阅读 [下载与运行](../README.md#下载与运行)。以下是开发和本地打包步骤，不会上传文件或创建 Release。

## 构建环境

- Windows 10/11。
- Visual Studio 2022 Build Tools 的“.NET 桌面生成工具”。
- .NET Framework 4.8 Developer Pack（运行环境不能替代编译用的引用程序集）。

项目使用 C# 5、WinForms、Win32 和 Aura COM，没有需要随 EXE 分发的第三方运行时库，不捆绑厂商 DLL。图标由 `tools/Generate-AppIcon.ps1` 生成。

## 构建并自检

在 Developer PowerShell 的仓库根目录执行：

```powershell
msbuild TangerineRigControl.sln /p:Configuration=Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$p = Start-Process src\TangerineRigControl\bin\Release\TangerineRigControl.exe -ArgumentList '--self-test' -PassThru
if (-not $p.WaitForExit(60000)) { $p.Kill(); throw 'Self-test timed out' }
$p.WaitForExit()
if ($p.ExitCode -ne 0) { throw "Self-test failed: $($p.ExitCode)" }
```

输出位于 `src\TangerineRigControl\bin\Release`。退出码 0 表示自检通过。自检只使用临时配置、合成控件、本机测试管道及 SDK 替身，不启动原厂软件、不发送设备点击、不修改登录启动项。

自检覆盖配置恢复、旧配置迁移、等待编辑、窗口保护、界面布局和 SDK 超时等边界，不能证明真实灯效或全部软件版本兼容。设备验证见 [验收清单](NIGHTLY-DEVICE-TEST.md)。

## 打包

在构建和自检成功后执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Package-Portable.ps1
if ($LASTEXITCODE -ne 0) { throw 'Packaging failed' }
```

执行策略参数只对该 PowerShell 进程生效，不修改系统策略；执行前可检查仓库脚本。

输出在 `artifacts\packages`，包含版本化 ZIP 及 `.sha256`。同名包存在时脚本会拒绝覆盖；如需重新打包，使用 `-OutputDirectory artifacts/packages-check` 指定新的目录。`-ExecutablePath` 可指定其他构建输出，应确保它来自当前源码且已经通过自检。

打包使用明确的文件清单，包含 EXE、GPL v3 许可证、用户文档及主界面图。包内 `source/` 同时包含当前工作树的项目源码、编译资源、解决方案、构建和打包脚本及文档；源码清单从项目文件中的编译项、资源和附属文件生成，不会递归复制整个工作目录。没有 `LICENSE` 或所需源码时会停止打包，不会复制真实配置、日志、PDB 或厂商 DLL。

`BUILD-INFO.txt` 记录打包时的源码提交和工作树状态，不是二进制来源签名；本地未提交构建会明确标注。必须先从同一工作树构建并自检，再打包，期间不要修改源码。重新构建包中程序时，在 `source/` 目录按本文步骤执行，构建环境和原厂依赖仍需另行准备。

当前许可为 GPL-3.0-only。分发受 GPL 覆盖的修改版时，应保留许可证和版权声明、注明修改，并提供该二进制对应的源码和构建材料；不能只提供可能已发生变化的 main 分支链接。历史 MIT 版本的许可边界见 [README](../README.md#license)。

## 修改与发布边界

- `UI`：主界面、设置草稿与等待编辑；`Automation`：F8 录制及坐标回放。
- `Infrastructure`：窗口、权限、配置与自检；`Services`：Aura COM 及后台会话。
- 修改后运行自检；涉及录制或 SDK 时，保留旧配置兼容性并说明设备验证范围。
- 功能图请使用合成配置，说明截图状态；不要提交本机路径、坐标、日志或真实用户截图。

CI 在推送或 PR 时构建、自检并上传便携包，不创建 Release。正式分发前核对版本、LICENSE、包内说明和校验值，并记录设备/原厂软件版本的实测结果。不要把历史验证或 SDK 调用成功当作当前版本硬件效果已确认。
