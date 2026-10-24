# Plain Craft Launcher (PCL) 本地修改版 2.99.0.0（历史构建）

该历史修改版基于官方 PCL 2.99.0.0（版本代码 413），不是官方修改版，也不代表上游发布了新的 2.99.0.0。

## 修改状态与内容

**已修改。** 本构建移除了启动器自动更新下载、差分更新、文件替换和更新后重启代码，并锁定启动器更新设置为关闭；“检查更新”只提示功能已移除。恢复“下载 → 原版游戏”入口，使离线账号可按原流程下载游戏（游戏下载仍需要联网）。普通公告、登录、皮肤、主题、Mod、Java、启动参数、配置和缓存路径保持原版逻辑。

此历史构建没有在 About 页面显示本地 GitHub 仓库链接；该项在 2.99.1 版补上。当前维护版： https://github.com/chadqian2012-sudo/Plain-Craft-Launcher-PCL-Local-Modified/releases/tag/v2.99.1

## 贡献者与来源

- PCL 原作者：龙腾猫跃
- 本地修改与打包：chadqian2012-sudo
- 上游仓库：https://github.com/Meloong-Git/PCL

保留上游 `LICENCE`。此版本为历史存档，建议使用 `v2.99.1`。

## 包含内容

- `Plain Craft Launcher (PCL) 本地修改版.exe`：历史修改版启动器。
- `upstream-source/`：PCL 2.99.0.0 上游源码快照。
- `patch-source/`：当时使用的定向补丁工具源码。
- `patch-audit.json`：修改审计。

不包含个人账号配置、游戏目录、日志或世界存档。