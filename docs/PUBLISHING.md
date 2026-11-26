# 上传 GitHub 与完整发布

源码、精确依赖输入、许可证、构建脚本和完整运行包现在均已准备为可一起交付的材料。

## 上传仓库

把项目根目录内容提交到自己的仓库，保留隐藏的 `.github`、`.gitignore`、`.gitattributes`。源码包需先解压，不能只把 ZIP 当作仓库首页。

`third-party/source-archives/` 中的 3 个源码包应一并上传，文件均小于 GitHub 普通文件上限。不要上传编译缓存、个人工程或私人视频。

## 构建与验证

```powershell
.\scripts\build-media-engine.ps1 -WorkDirectory C:\FrameCutBuild
.\scripts\build.ps1 -EngineDirectory C:\FrameCutBuild\engine
.\tests\run-tests.ps1 -AppDirectory .\build\FrameCut
.\scripts\package-release.ps1 -EngineDirectory C:\FrameCutBuild\engine
```

打包脚本验证源码锁定和二进制来源记录，输出：

- `FrameCut-Portable-v1.2.1.zip`：完整绿色版，内含 `sources/` 下的完整对应源码压缩包。
- `FrameCut-Source-v1.2.1.zip`：同一套源码、依赖、构建方式和许可证。
- `SHA256SUMS.txt`：下载附件的校验和。

把这些文件作为 GitHub Release 附件一起上传。绿色版已经内嵌源代码副本，下载者解压后能直接取得与二进制对应的源码。不要发布旧 1.2.0 二进制配新 1.2.1 源码。

## 工作流

`Windows build` 仅编译应用。手动运行 `Build portable package with matching sources` 会从锁定源码构建组件，运行检查，再上传打包结果为工作流 artifact。它不会自行创建公共 Release。

当前项目整理并未连接或上传你的 GitHub 账号。创建远程仓库及选择公开 / 私有仍是后续的上传操作。
