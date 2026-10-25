# PCL 2.99.2 开发预览补丁工具

沿用 2.99.1 的限定方法补丁，以固定 SHA-256 的官方 2.99.0.0 EXE 为输入，移除自我更新并恢复原版游戏下载。2.99.2 额外接入配套 PclLocalTools.dll，在关于页、版本概览页、下载页提供本地工具箱、手动发布页和错误排查入口。

完整构建命令见上级 README.md 和 Build.ps1。生成程序前先将自行编译的 PclLocalTools.dll 放在输出目录，DLL 与启动器一起分发。

工具重新读取输出，对原有全部方法逐一比较，只允许明确列出的更改；原字段、常量与内嵌资源均进行核验。UpdateStart、UpdateRestart、UpdateReplace 必须保持空实现。原作者和上下游 URL 保留。

官方输入 EXE SHA-256：6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386。输入输出相同或输入哈希不同会拒绝执行。

工具依赖 .NET 10 SDK 和 Mono.Cecil 0.11.6；新增工具箱源代码位于上级 toolbox-source，目标 .NET Framework 4.8。保留上游自定义 LICENCE。
