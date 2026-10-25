# PCL 本地修改版 2.99.2

当前正式版，基于官方 PCL 2.99.0.0，增加存档路径、备份恢复、目录复制、下载诊断和日志排查。[使用说明](使用说明.md) · [更新说明](更新说明.md)

## 下载

- [2.99.2 最新正式版](https://github.com/chadqian2012-sudo/Plain-Craft-Launcher-PCL-Local-Modified/releases/tag/v2.99.2)：使用包和完整源码包分别提供。
- [上一稳定版 2.99.1](https://github.com/chadqian2012-sudo/Plain-Craft-Launcher-PCL-Local-Modified/releases/tag/v2.99.1)。

用户于 2026-10-25 确认上一版验收通过，现将同一 2.99.2 构建转为正式版。此前的 47 项模拟检查和二进制审计结果保留；用户验收与本助手执行的检查分别记录，详见[发布说明](发布说明.md)。原版及本地历史版本见[版本档案](VERSIONS.md)。

为保留已验收程序和两个 ZIP 的字节与 SHA-256，未重新编译或打包；文件名、界面及包内历史说明仍可能保留“开发预览 / preview”标识，正式发行状态以本次发布页为准。

原作者：龙腾猫跃（LTCatt）；本地修改：chadqian2012-sudo。

- 修改版：https://github.com/chadqian2012-sudo/Plain-Craft-Launcher-PCL-Local-Modified
- 上游：https://github.com/Meloong-Git/PCL
- 原作者赞助：https://meloong.com/afd/a/LTCat

## 构建

需要 Windows、.NET 10 SDK 和用于运行工具箱/验证的 .NET Framework 4.8。NuGet 自动获取 net48 参考程序集和 Mono.Cecil 0.11.6。缓存、临时文件和输出均在本目录。

```powershell
.\Build.ps1 -OriginalExe 'X:\baseline\PCL-2.99.0.0-original.exe' -DotNet 'X:\sdk\dotnet.exe'
```

输入必须是 SHA-256 为 `6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386` 的官方 EXE，工具拒绝覆盖输入。脚本编译工具箱和补丁工具，执行人工数据验证，生成并审计 `dist/PCL-2.99.2-preview/` 下的新程序。

`upstream-source` 保留上游源码快照；运行程序来自官方 EXE 的限定方法补丁与自行编译的 `toolbox-source`，并非声称将不完整上游工程全量重编译。`patch-source` 提供补丁源码。原始 EXE 不随源码包提供，可从原版版本档案自行取得。

保留上游自定义 LICENCE，不重新授权上游代码。

## English

Current third-party stable release, PCL 2.99.2. The user confirmed acceptance on 2026-10-25; the accepted executable and both ZIP archives are unchanged. Existing preview labels describe the original build, not the current release status. The 47 automated synthetic/mock checks and binary audit remain recorded separately from user acceptance. Build.ps1 uses .NET 10 SDK, local caches and a single build worker; the toolbox targets .NET Framework 4.8. The upstream source snapshot, attribution, support links and LICENCE are retained.
