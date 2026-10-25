# PCL 本地修改版 2.99.3

本次增加从 chadqian2012-sudo/Plain-Craft-Launcher-PCL-Local-Modified 的 GitHub Releases 检查正式版本，并提供非强制更新。旧上游更新仍然禁用，原作者及上游链接保留。

## 更新行为

启动主界面后延迟 8 秒在后台检查一次。同一安装目录 30 分钟内的启动检查会复用检查时间，手动检查不受此限制。只接收更高的正式版本，不接收草稿或预发布，不降级。

发现更新后显示可关闭的普通窗口，不置顶；如果你正在其他应用或游戏中，等 PCL 主窗口重新激活才显示。关闭和“稍后”不下载、不安装。“跳过此版本”会保存选择；手动检查仍可以查看该版本。

只有点击“下载更新”才下载并核对 ZIP 和程序组件的 SHA-256。完成后还需要确认“退出启动器并安装”。助手等待主进程正常退出，120 秒内没有退出就放弃；不会强行结束启动器、Java 或游戏。更新后重新打开启动器默认不勾选。

入口：关于页面的“检查本仓库更新”，以及本地工具箱“版本与更新”。自动检查可在工具箱中关闭，配置独立保存在 PCL/LocalUpdateSettings.json。

## 发布与构建

使用 Build.ps1，参数与上一版相同：OriginalExe 为固定哈希的官方 2.99.0.0，DotNet 为 .NET 10 SDK。输出在 dist/PCL-2.99.3；启动文件为 Plain Craft Launcher (PCL) 本地修改版.exe。

Package.ps1 分别生成使用包、源码包、SHA-256 清单和 PCL-update.json。未来每个正式 Release 都要同时上传使用包与对应 PCL-update.json，更新助手才可自动下载安装。没有清单的新版仍可提示并打开发布页。

2.99.2 没有本次新增的检查器，需要手动安装 2.99.3 一次；后续版本才能从此仓库检查更新。完整解压并保留六个程序组件，不要只复制 EXE。详细规范见 UPDATING.md。

## 验证

自动化验证使用模拟世界、日志、HTTP 响应和模拟程序目录；独立助手子进程也只处理人工数据，不运行真实 Minecraft。实际执行结果见 verification/fixtures/latest-results.txt。源文件通过固定输入 EXE 限定方法补丁生成，同时重新编译工具箱与独立助手。

## 来源

原作者：龙腾猫跃（LTCatt）；本地修改：chadqian2012-sudo。
修改版：https://github.com/chadqian2012-sudo/Plain-Craft-Launcher-PCL-Local-Modified
上游：https://github.com/Meloong-Git/PCL
原作者赞助：https://meloong.com/afd/a/LTCat
保留上游自定义 LICENCE，不重新授权上游代码。

## English

PCL 2.99.3 adds optional GitHub Releases updates from this fork only. Startup checks are asynchronous and limited to stable, newer versions. The dismissible, non-topmost notice offers Download, Later and Skip; it never downloads silently. Installation requires a second confirmation, normal launcher exit, verified SHA-256 payloads and old-file backups. No process is forcibly terminated. Restart is opt-in. The original upstream updater remains disabled. Publish the matching PCL-update.json with every future usage ZIP. Upstream credits, support links and LICENCE remain intact.
