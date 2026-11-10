# 构建、自检与打包

普通用户请先阅读 [下载与运行](../README.md#下载与运行)。以下是开发和本地打包步骤，不会上传文件或创建 Release。

## 构建环境

- Windows 10/11 x64。
- Visual Studio 2022 Build Tools 的“.NET 桌面生成工具”。
- .NET Framework 4.8 Developer Pack（运行环境不能替代编译用的引用程序集）。

项目使用 C# 5、WinForms、Win32 和 Aura COM，没有需要随 EXE 分发的第三方运行时库，不捆绑厂商 DLL。图标由 `tools/Generate-AppIcon.ps1` 绘制，自包含的 `app.ico` 用于主程序和安装器。可用 Python 3 运行 `tools/verify_windows_icons.py <Setup.exe> <主程序.exe>` 比对 EXE 中索引 0 的全部图标帧；该检查只读取 PE 资源。

## 构建并自检

在 Developer PowerShell 的仓库根目录执行：

```powershell
msbuild TangerineRigControl.sln /p:Configuration=Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$p = Start-Process src\TangerineRigControl\bin\Release\TangerineRigControl.exe -ArgumentList '--self-test' -WindowStyle Hidden -PassThru
if (-not $p.WaitForExit(60000)) { throw 'Self-test timed out; process preserved for inspection' }
$p.WaitForExit()
if ($p.ExitCode -ne 0) { throw "Self-test failed: $($p.ExitCode)" }
```

输出位于 `src\TangerineRigControl\bin\Release`。退出码 0 表示自检通过。自检只使用临时配置、合成控件、本机测试管道及 SDK 替身，不启动原厂软件、不发送设备点击、不修改登录启动项。

项目默认编译 x64。Windows Setup 构建方式、固定 NSIS 版本及隔离安装测试见 [安装版说明](INSTALLER.md)；`tools/Package-Windows.ps1` 同时生成 Setup 和 Portable，不需要分别重复打包。

自检覆盖集中目录的复制校验、迁移中断/冲突/旧版改写保护、配置恢复、旧配置迁移、等待编辑、窗口保护、界面布局和 SDK 超时等边界，不能证明真实灯效或全部软件版本兼容。设备验证见 [验收清单](NIGHTLY-DEVICE-TEST.md)。

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

## 本地桌面验收入口的交付

桌面入口通常直接指向一个固定的 Release EXE。提交代码不会更新该 EXE，也不会更新正在运行的实例。维护时执行以下步骤，正常点击入口不需要重新构建。

1. 读取本项目 `.lnk` 的目标、参数、工作目录、图标以及实际 EXE 的 ProductVersion 和 SHA-256。检查本项目进程路径和单实例互斥量。入口如果指向脚本或其他启动器，先追踪完整链路，不直接覆盖。进程不存在只证明当前未运行，不证明真实数据可迁移。
2. 在新的 `artifacts/desktop-acceptance/<交付标识>/` 子目录构建 Release，将 OutputPath 与 IntermediateOutputPath 分别指向该目录的 `build/`、`obj/`，避免覆盖 `artifacts/stable` 或在用文件。运行上文无窗口 `--self-test`、隐私检查，记录源码提交、工作区状态、版本和 EXE 哈希。构建产物保留，不能随清理临时文件删除。若需要便携包，用既有打包脚本生成到独立目录。
3. 核对数据身份。0.4.3 首次正常运行会读取新旧配置并可能复制旧数据，见 [数据说明](DATA.md)。真实迁移未获授权时，保留原日常入口，仅准备新版产物和忽略目录中的待切换快捷方式。不能改为 fixture、空配置、`--self-test` 或重定向用户目录，冒充日常新版验收。
4. 确认无活动任务、未保存操作与数据阻塞，且当前授权允许切换后，将原 `.lnk` 复制到本次忽略的交付目录。重新读取原链接确认未被其他会话更改，再只更新本项目目标、对应工作目录和 EXE 图标，保留既有参数、窗口设置及其他属性。保留旧 EXE，其他入口、自启项和安装候选“当前”目录不在此步骤中维护。遇到审批拒绝立即保留现场，不换方式重试。
5. 回读最终链接，使用下方脚本核对目标和已验证哈希。若真实启动尚未获准，不启动主界面，报告“入口已更新、实际实例未验证”。需要迁移或有活动任务时报告“待安全切换”，同时明确当前桌面仍启动哪个版本。获准启动后核对实际进程路径与版本，再单独进行用户体验/设备验收，不能用自检代替。

只读核对工具（先将变量设为本次已验证 EXE 及此前记录的 SHA-256）：

```powershell
./tools/Test-DesktopEntry.ps1 -ExpectedExecutable $verifiedExe -ExpectedSha256 $verifiedHash
```

默认读取系统实际桌面下 `Tangerine/TangerineRigControl.lnk`，也可用 `-ShortcutPath` 检查隔离准备的同名链接。脚本不保存快捷方式、不启动程序、不读配置内容；目标、版本、哈希、工作目录或参数不匹配会失败。它同时报告进程、互斥及配置文件是否存在，这些信息不证明设备空闲或数据迁移安全。非空既有参数用 `-ExpectedArguments` 精确核对，不能为了通过检查删掉参数。

绝对路径、原快捷方式备份、构建和自检日志、交付清单保存在忽略的 `artifacts/` 或 `private/`。交接分别列出源码、已准备产物、桌面入口与运行实例状态。仅维护文档或核对工具变化时无需改变应用版本、重新生成安装候选或重复安装测试，但仍运行项目必需检查。

## 修改与发布边界

提交前运行 `powershell -NoProfile -ExecutionPolicy Bypass -File tools\Check-PublicContent.ps1`，检查 Git 跟踪文件是否混入绝对路径、本地配置、调试文件或常见密钥格式。新增文件应先暂存，让检查覆盖它们。CI 检查跟踪文件，打包检查实际纳入包中的源码、文档和 EXE；命中时只输出文件名和规则，不输出敏感值。它不是全面的秘密扫描，截图内容和任意命名的个人信息仍需人工复核。

Release 使用 `DebugType=none`，避免 EXE 内嵌构建机的 PDB 路径；Debug 构建只供本地开发。打包还会检查 EXE 中的绝对路径，不要用 Debug 程序替代发布版。真实配置与备份留在 LocalAppData；临时日志、截图和导出可放在被忽略的 `private/` 或 `artifacts/`，不要强制添加到 Git。

- `UI`：主界面、设置草稿与等待编辑；`Automation`：F8 录制及坐标回放。
- `Infrastructure`：窗口、权限、配置与自检；`Services`：Aura COM 及后台会话。
- 修改后运行自检；涉及录制或 SDK 时，保留旧配置兼容性并说明设备验证范围。
- 功能图请使用合成配置，说明截图状态；不要提交本机路径、坐标、日志或真实用户截图。

CI 在推送或 PR 时构建、自检、打包并运行隔离身份安装生命周期测试；不上传公开安装包，不创建 Release。正式分发前核对版本、LICENSE、第三方声明、包内说明和校验值，并记录设备/原厂软件版本的实测结果。不要把历史验证或 SDK 调用成功当作当前版本硬件效果已确认。
