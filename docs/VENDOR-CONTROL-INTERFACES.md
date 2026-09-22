# 原厂控制接口调查（2026-09-09）

这是 2026-09-09 的历史调查，以下证据与“本轮”结论指当时状态。当前实现已接入 Aura SDK：Armoury Crate 管主板＋内存，iGC.Lite 管显卡，执行时显卡在华硕之前；L-Connect 与 KANALI 分别管理两块屏幕。当前使用和限制以 README 为准。

| 软件 | 已确认的证据 | 结论 |
| --- | --- | --- |
| Armoury Crate / Aura | 官方 Aura SDK 提供 C# COM 示例，本机也有 SDK 文件。 | 可研究接口控制，但需要验证控制权释放及仅控制主板；不是现成通用 CLI。 |
| iGC.Lite | 本机有 iGameAPI 灯光和内存模块；已查官方资料未找到公开外部灯光 CLI/SDK 文档。 | 内部模块不能视为公开接口，暂保留录制。 |
| L-Connect | 官方有 OpenRGB Beta 集成；未查到当前版本公开的 LCD 开关 CLI。 | 本机唤醒入口仅恢复窗口，屏幕继续录制控制。 |
| KANALI | 已查官方软件资料未找到公开屏幕开关 CLI/SDK 文档。 | 继续录制控制。 |

未找到公开文档不等于不存在私有接口。本轮未加载厂商 DLL、切换 SDK 控制权或安装替代软件。

Addressable LED Strip 是主板 ARGB 接口对应的灯光项，不是软件集成接口。兼容风扇灯光、灯条可以通过连接该接口并启用同步来控制；它不代表 LCD 内容或风扇转速控制。截图不能确定实际连接的设备。

来源：
- [ASUS Aura SDK C# 示例](https://www.asus.com/microsite/aurareadydevportal/tutorial_csharp.html)
- [ASUS ARGB 风扇识别为灯条的说明](https://www.asus.com/uk/support/faq/1048012/)
- [iGame Center 官方介绍](https://www.colorfulgroup.com/id/igamecenter/)
- [L-Connect OpenRGB Beta](https://lian-li.com/l-connect-3-x-openrgb/)
- [KANALI 官方软件页](https://www.tryx.com/en/kanali)
- [OpenRGB 官方 SDK 介绍](https://openrgb.org/)
