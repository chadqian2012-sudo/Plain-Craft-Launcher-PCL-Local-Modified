# PCL 2.99.0.0 最小功能补丁

本工具以用户提供并核验过的官方 2.99.0.0 EXE 为输入，只恢复自动安装页面的游戏下载入口，移除启动器自我更新的实际执行代码。它不使用工作区中早期的 `src` 草稿作为构建输入。

上游：https://github.com/Meloong-Git/PCL

固定输入 SHA256：`6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386`

## 修改范围

- `PageDownloadInstall.LoaderInit`：把“关闭下载”加载器替换为原有 `DlClientListLoader`，继续使用原版安装、组件选择和下载流程。
- `ModSecret.UpdateStart`、`UpdateRestart`、`UpdateReplace` 及其专用生成函数：清空实际下载、差分更新、文件替换和重启代码。
- `UpdateCheckByButton`：只提示更新功能已移除。
- `ServerSub`：跳过更新公告及其更新动作，保留普通公告和远程配置获取。
- 启动器设置页：仅将启动器更新选项锁定为关闭，移除该选项的更新专用警告。
- 关于页及程序集产品文字：明确标注“本地修改版”，原作者署名和赞助链接保留。

没有恢复整合包安装、已安装版本修改页等另外的受限入口，没有改变微软登录、离线登录、皮肤、主题、Mod、Java、内存设置、正版购买提示、原作者赞助逻辑、配置命名空间及缓存路径。

## 构建和复现

需要 .NET 10 SDK。以下命令在本 `patch-source` 目录中执行。请将示例中的 `X:\original\Plain Craft Launcher 2.exe` 换成你自行准备的官方 2.99.0.0 原始 EXE 的实际路径；原版 EXE 不随本发布包附带。

```powershell
dotnet build .\LauncherPatch.csproj -c Release
dotnet .\bin\Release\net10.0\LauncherPatch.dll patch `
  'X:\original\Plain Craft Launcher 2.exe' `
  '..\Plain Craft Launcher (PCL) 本地修改版.exe' `
  ..\patch-audit.json
```

输入哈希不匹配会拒绝执行，且禁止覆盖输入程序。更换上游版本必须重新核对 IL 和功能差异，不能直接放宽哈希检查。

生成时会重新读取输出，核验全部方法的签名和方法体、全部内嵌资源，并逐项输出允许发生的变化。另可运行只读字段核验：

```powershell
dotnet .\bin\Release\net10.0\LauncherPatch.dll metadata-check `
  'X:\original\Plain Craft Launcher 2.exe' `
  '..\Plain Craft Launcher (PCL) 本地修改版.exe'
```

工具依赖 Mono.Cecil 0.11.6。产品使用上游自定义 LICENCE；本地生成物必须与官方发行版区分。未公开上传用户的程序、配置或凭据。
