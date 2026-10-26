# PCL 本地修改版 2.99.3

## 正式版更新说明

此正式版包含可选的 GitHub 仓库更新，以及 Windows 实机验收发现的两项修复：

- 更新助手从安装目录运行时，现通过事务目录中的辅助进程安全恢复自身，不再因自身 EXE 被占用而无法回退。
- 安装前若 PCL 显示下载任务退出确认，更新提示会先让出界面。用户取消退出后安装取消，程序文件保持原样。
- 更新可关闭且非强制；下载和安装均由用户确认。不会强制结束 PCL、Java 或 Minecraft。

## 验收与构建

Windows MCP 实机验收通过；85 项自动化测试通过。最终构建 0 警告、0 错误。补丁审计核对了 22 个预期变更方法、6317 个未变方法、3603 个未变字段/常量及 2 个未变嵌入资源。详细范围及限制见随源码包提供的 `FORMAL-ACCEPTANCE-2.99.3.md`。

本次验收覆盖更新、下载取消、确认、正常退出、安装、重启选择、文件锁、损坏备份与回退；没有启动 Minecraft 游戏。个人配置和世界存档不属于发布包。

## 文件与校验

正式 ZIP 文件名及 SHA-256 以 `PACKAGE-SHA256-20261025-171928.txt` 为准。自动更新读取同一 Release 中的 `PCL-update.json`，该清单只指向本正式版使用包。

同一 Release 中旧时间戳的使用包和源码包保留为历史预发布资产，原下载地址及历史校验清单不变；它们不代表本正式版构建。旧预发布清单另存为 `PCL-update-preview-20261025.json`。

## 源码、署名与许可

保留上游 PCL 源码历史、原作者龙腾猫跃（LTCatt）、上游仓库 URL、原作者赞助入口和原有许可。修改版本仓库：<https://github.com/chadqian2012-sudo/Plain-Craft-Launcher-PCL-Local-Modified>；上游：<https://github.com/Meloong-Git/PCL>。

## English

This stable release adds optional, dismissible GitHub updates and fixes two Windows acceptance findings: updater self-recovery now runs from a transaction copy, and the update notice yields to PCL's task-exit confirmation. Cancelling launcher exit cancels installation without changing program files. Download and installation remain user-controlled; no PCL, Java, or Minecraft process is forcibly terminated. Windows desktop acceptance and 85 automated tests passed; the final build has zero warnings and errors. Minecraft gameplay was not launched. Historical preview ZIPs remain available under their original names and hashes; they are not the stable build. Original upstream history, credits, URL, sponsor link, and licence are retained.
