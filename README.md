# TangerineRigControl

一个轻量、仅在本机运行的 Windows 主机灯光与屏幕控制台。

当前目标：

- 通过 SignalRGB 本地 API 开关整机 RGB 灯光。
- 通过可重新录制的操作序列开关联力副屏和 TRYX 曲面屏。
- 从同一个托盘菜单或控制面板完成日常操作。
- 不接触硬件底层协议，不替代原厂软件的高级设置。

## 隐私设计

- 无遥测、无账户、无云服务、无自动更新请求。
- SignalRGB 集成只访问 `127.0.0.1:16038`。
- 软件路径、按钮位置和自动化步骤只保存在
  `%LOCALAPPDATA%\TangerineRigControl\settings.xml`。
- 本地配置、日志和截图均被 `.gitignore` 排除；程序默认不写日志、不生成崩溃转储。

## 使用

首次启动后进入“设置与录制”：

1. 确认 L-Connect 3 和 KANALI 的程序路径。
2. 分别录制每块屏幕的“开启”和“关闭”操作序列。
3. 录制时把鼠标放在原厂软件内需要点击的位置，按 `F8` 记录该步骤。
4. 手动点击该位置进入下一页，继续记录；完成后保存序列。

重放时优先使用 Windows UI Automation 识别按钮；对于 KANALI 等自绘界面，会使用相对于窗口客户区的位置，因此窗口缩放后仍能工作。原厂软件大幅改版后可重新录制，无须修改代码。

SignalRGB 的开关接口通常需要 SignalRGB Pro；如果接口返回 `403`，控制台会显示明确提示，不会影响两块屏幕各自的操作。

## 构建

依赖：

- Windows 10/11
- Visual Studio 2022 Build Tools（.NET 桌面生成工具）
- .NET Framework 4.8 Developer Pack

```powershell
msbuild TangerineRigControl.sln /p:Configuration=Release
```

生成文件位于 `src\TangerineRigControl\bin\Release`。程序没有第三方运行时依赖。

## 当前阶段

这是首个可运行 MVP。下一步会在实际设备上完成两套屏幕操作序列的校准，并根据原厂软件行为增加状态检测与快捷键。

