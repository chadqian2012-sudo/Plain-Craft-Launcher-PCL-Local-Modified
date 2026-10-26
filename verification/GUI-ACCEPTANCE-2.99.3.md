# Windows MCP 实机验收 / Desktop acceptance

Date: 2026-10-25 (Asia/Shanghai). Operator: Codex using Windows MCP, at the user's explicit request. This is agent-executed acceptance, not a claim that the user personally accepted 2.99.3.

## Actual launcher

The deployed 2.99.3 executable was started on the user's Windows desktop. Its SHA-256 was verified before launch:
`B99182EDA27F36FA312A7BA23728EC5412BBC3BE67AA19B73FFF03A9EF68BDB1`.

| Operation | Observed result |
|---|---|
| Start actual launcher | Main window displayed normally; no startup error dialog. Minecraft was not started. |
| More → About | Version 2.99.3, original author 龙腾猫跃, support entry and local toolbox present. |
| Check this repository | Actual network check returned: 当前版本 2.99.3，未发现本仓库更高的正式版本. The repository's current stable release was 2.99.2. |
| Open toolbox → Version and updates | Checkbox initially on. Clicking off persisted `Enabled=false` in the separate updater settings. |
| Close and reopen toolbox | Checkbox remained off. It was then restored to on; persisted `Enabled=true`, empty skipped version. |
| Original-game download page | Successfully loaded the version list, including 26.3; no disabled-download dialog. No game download was started. |
| Launcher close button | Launcher window closed normally. No forced process termination. |

## Production notice with isolated fixture data

`gui-acceptance/NoticeHarness.cs` compiles a separate WPF host that references the unchanged shipped DLL. Its SHA-256 was:
`EADA40D233C2305AA7694182DCA4D138CB39B23CDC4B2A5FF59F3132CB439AD8`.
The visible v2.99.4 notice was explicitly labelled artificial test data, not a real release. No fake GitHub version was created. The fixture has no manifest, exercising the production manual-download fallback and decline controls. Settings are isolated from the actual launcher.

Windows MCP clicked, separately:

1. Title-bar X: notice closed; no skipped version and no transaction directory.
2. Later: notice closed; no skipped version and no transaction directory.
3. Skip this version: notice closed; only fixture settings stored `SkippedVersion=v2.99.4`; no transaction directory.

The restart checkbox was initially unchecked. Fixture event records are saved in `gui-acceptance/events.txt`. The harness was closed after testing.

## Scope and limits

84 automated checks, separate real helper subprocess tests on artificial application files, live repository checks, package validation and static patch audits are recorded separately. They complement this desktop exercise. This acceptance did not install a future online release into the user's real launcher directory, did not start Minecraft and did not exercise a full future-release download/exit/restart sequence through the GUI. Real desktop screenshots, account/config data, logs and world files are not included in public evidence.

中文：真实启动、联网检查、开关持久化、下载列表、正常退出，以及隔离提示窗的关闭/稍后/跳过均通过。本记录不冒充用户亲自验收，不把模拟安装测试写成未来正式版的完整在线更新验收。
