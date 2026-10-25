# 更新发布规范 / Update publication

## 发布者

1. 修改补丁版本、工具箱/宿主/助手项目版本和 Package.ps1 中的版本，保持一致。UpdateProtocol.Version 自动读取程序集版本；清单生成器会核对组件版本。
2. 运行 Build.ps1，确保测试和二进制审计通过。
3. 运行 Package.ps1，得到使用包、源码包、PACKAGE-SHA256 清单与 PCL-update.json。
4. 在本仓库创建标签 vX.Y.Z 的正式 Release，上传这四个文件及发布说明。PCL-update.json 必须来自这一份使用包，不可混用不同打包结果。
5. 核对 GitHub 附件的 SHA-256 与本地清单一致，设置为 Latest。预发布和草稿不会被自动检查选中。

更新元数据固定访问本仓库 /releases/latest 接口。清单 Format=1，Version 对应标签的数字版本；ArchiveName/Sha256 指向使用 ZIP；Files 描述六个固定程序文件的名称、大小和 SHA-256。清单是独立附件，不能放入其自身计算哈希的 ZIP。

使用 ZIP 可以附带说明，但自动安装器只提取和替换以下六个程序文件：
Plain Craft Launcher (PCL) 本地修改版.exe
PclLocalTools.dll
PCL Local Tools.exe
PCL Local Tools.exe.config
PCL Update Helper.exe
PCL Update Helper.exe.config

不会覆盖 PCL 配置、.minecraft、世界、账号、Java 或模组。用户自行改名的主 EXE 会保留原名称，其他组件名称必须保持。

## 用户

提示窗口可以关闭；“稍后”不下载，“跳过此版本”保存该版本。手动检查可忽略跳过记录。首次下载不等于授权安装，安装还需要再次确认正常退出启动器。

安装事务位于启动器目录 PCL/LocalUpdates/随机编号。先验证完整 ZIP，再验证逐文件哈希。助手从独立 runner 目录运行，等待原进程退出后保留旧文件备份，再逐个原子替换。占用、损坏、取消或启动器没有退出都不会强制替换。

替换中途失败会回退旧组件。若停电等导致事务中断，退出所有 PCL 和工具箱后，双击 PCL Update Helper.exe；它会检查未完成事务并询问是否恢复。不要删除事务目录的 backup 和 journal.json 后再尝试恢复。助手不会申请管理员权限。

## English

Upload the matching usage ZIP and PCL-update.json to each stable release in this repository and select Latest. Numeric vX.Y.Z tags only; draft/prerelease builds are excluded. The installer accepts exactly six allowlisted application components, validates archive and component hashes, waits for the original process, backs up old files and rolls back on failure. Configs, worlds, credentials and Java installations are outside the write set. Dismissal is not consent to download or install; restarting is opt-in.
