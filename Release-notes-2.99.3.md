# PCL 本地修改版 2.99.3 / Optional GitHub updates

本次为预发布版，2.99.2 继续作为当前正式版。基于官方 PCL 2.99.0.0 的第三方修改版；原作者：龙腾猫跃（LTCatt），本地修改与打包：chadqian2012-sudo。

## 修改内容

- 新增从本仓库 GitHub Releases 检查更新：启动后延迟 8 秒后台检查，同一安装目录 30 分钟内不重复自动请求；也可手动检查。仅接受更高的正式版本。
- 有更新时显示可关闭、非置顶、非模态提示。关闭或“稍后”不下载、不安装；“跳过此版本”保存选择。后台检查不会主动把 PCL 拉到前台。
- 下载需要点击确认；下载后校验清单、ZIP 和六个程序组件的 SHA-256，安装再单独确认。更新成功后重启默认关闭。
- 独立助手等待 PCL 正常退出；超时、取消或占用会放弃，保留备份并支持失败回退/中断恢复。不强行结束 PCL、Java 或游戏。
- 旧上游自动更新仍禁用；保留离线账号下载入口、原作者、赞助入口、上游 URL 和自定义 LICENCE。

## 使用

2.99.2 没有新检查器，需要手动安装本版一次。完整解压使用 ZIP，保留 EXE、DLL、工具箱、更新助手及两个配置文件；入口为 `Plain Craft Launcher (PCL) 本地修改版.exe`。包内没有 Minecraft、存档、账号、个人设置或日志。这里的“离线账号”指登录方式；下载游戏文件仍需联网。

自动检查开关：更多 → 关于与鸣谢 → 打开本地工具箱 → 版本与更新。设置独立保存在 `PCL/LocalUpdateSettings.json`。本版不接收预发布推送，后续正式版本才会被自动检查器提示；不会因本次预发布而把当前正式版切换为 2.99.3。

未来正式版须同时上传相匹配的使用 ZIP 与 `PCL-update.json`，缺少清单时仅提供手动发布页入口。细节见 `UPDATING.md`。

## 验证与验收来源

- 构建零警告、零错误；84 项自动化检查通过，覆盖模拟 HTTP、版本过滤、校验失败、取消、占用、文件白名单、备份、回退和中断恢复。独立助手以真实子进程操作人工数据验证安装/恢复/取消。
- 固定上游输入补丁审计：22 个预期方法变化、6317 个方法未改、3603 个字段/常量未改、2 个原嵌入资源未改。
- 2026-10-25，按用户要求由 Codex 使用 Windows MCP 操作实际 2.99.3：启动、关于入口、真实仓库联网检查、开关关闭保存并重新打开、恢复开启、原版游戏下载列表、正常退出均通过。
- 同一发布 DLL 的隔离提示窗中，X 与“稍后”关闭不产生事务；“跳过此版本”只保存测试设置，无下载/安装事务。v2.99.4 为明确标注的人工数据，未在 GitHub 创建该版本。
- 2.99.2 的用户验收记录不移用到本版。本版记录的是代理执行的实机验收；未在用户真实目录完成未来线上正式版的整条下载、退出、更新、重启演练。公开证据不包含用户桌面截图、游戏或账号资料。

使用包和源码包沿用 2026-10-25 13:03 打包的同一构建，没有因追加实机报告而重新编译或重新压缩。独立发布说明与 Git 仓库中的验收报告补充打包后的验收结果；ZIP 内原有说明保留构建时状态。

## 资产与 SHA-256

| 文件 | SHA-256 |
|---|---|
| PCL-2.99.3-launcher-20261025-130354.zip | `678ACBF9A4B619A2514721DD1C52D4C3A59440267C4DF3B214B2EA043194DB48` |
| PCL-2.99.3-source-20261025-130354.zip | `0018F60C6B92633BD2DF39FC14361C245EFF24F906DC779AA83199EB299AD4CB` |
| PCL-update.json | `8082025197E5C676CFC35105325E010001780C28F32782A4745E6D1613EE6282` |

另附 `PACKAGE-SHA256-20261025-130354.txt` 和本独立发布说明。

上游：[Meloong-Git/PCL](https://github.com/Meloong-Git/PCL)；本修改版：[仓库](https://github.com/chadqian2012-sudo/Plain-Craft-Launcher-PCL-Local-Modified)；原作者：[赞助入口](https://meloong.com/afd/a/LTCat)。本仓库与上游独立，不代表上游背书；保留上游自定义 LICENCE，不重新授权上游代码，也不伪造共同作者。

## English

This prerelease adds optional updates from this fork's GitHub Releases. Version 2.99.2 remains the current stable release. Startup checks run asynchronously after eight seconds with a 30-minute per-directory cooldown; manual checks bypass that cooldown. Only newer stable releases are eligible. Notices are dismissible, non-topmost and non-modal. Later/X do not download or install; Skip persists the choice. Background checks do not bring the launcher to the foreground.

Downloading and installing require separate consent. ZIP and six allowlisted program files are verified using SHA-256; the independent helper waits for normal launcher exit, preserves backups and supports rollback/recovery. It never forcibly terminates the launcher, Java or games. Restart is opt-in. The original upstream updater remains disabled. Game files, worlds, account/config data, Java and mods are not update targets.

Install this build manually once from 2.99.2 and retain all six program components. Future stable releases must include the matching usage ZIP and `PCL-update.json`. This checker does not opt users into prereleases. The provided archives contain no user/game data.

Validation: zero build warnings/errors, 84 automated checks, real helper subprocesses against artificial files, live read-only repository checks and package validation. On 2026-10-25 Codex used Windows MCP to test the actual launcher's startup, About, live manual update check, settings persistence across toolbox reopening, restored enabled state, vanilla version listing and normal exit. The unchanged shipped DLL's notice was also operated with clearly labelled isolated v2.99.4 fixture data: X/Later/Skip created no transaction. No fake public version was created. This is agent-executed acceptance, not user acceptance of 2.99.3 and not a full future-release online upgrade in the user's real install.

The original usage/source ZIP bytes remain unchanged. This independent note and the Git acceptance report add the later desktop results. Original author 龙腾猫跃 (LTCatt), upstream repository/support links, original history and custom LICENCE are retained; modifications and packaging are by chadqian2012-sudo.
