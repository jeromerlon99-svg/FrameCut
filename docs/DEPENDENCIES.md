# 构建本地视频组件

当前源码仓库已经包含完整固定输入，无需另外寻找 FFmpeg / x264 / zlib 对应源码。

## 直接使用绿色版

解压 1.2.1 绿色包后运行 FrameCut.exe，无需安装编译器。完整对应源码已放在绿色包的 `sources/` 文件夹。

## 从源码重建

需要 Windows x64、Git for Windows 的 Bash、支持 xz/zstd 的 tar、PowerShell，以及一个无空格的编译目录。工具只下载到该目录，不更改系统安装。

```powershell
.\scripts\build-media-engine.ps1 -WorkDirectory C:\FrameCutBuild
.\scripts\build.ps1 -EngineDirectory C:\FrameCutBuild\engine
.\tests\run-tests.ps1 -AppDirectory .\build\FrameCut
```

第一步验证随仓库提供的 3 个源码包，下载并校验锁定版本的 LLVM-MinGW、NASM、pkgconf，依次编译 zlib、x264、FFmpeg。Git Bash 可以通过 `-GitBash` 指定路径；已有工具包可通过 `-DownloadCache` 提供。

源码、工具包版本和 SHA-256 均在 `third-party/engine-sources.lock.json`；编译命令在 `scripts/build-engine.sh`。构建产物位于 `C:\FrameCutBuild\engine`，日志位于 `C:\FrameCutBuild\logs`。构建会生成来源与二进制散列记录 `BUILD-PROVENANCE.json`。

再编译 FrameCut 应用只需要 .NET Framework 4.8。Visual Studio 工程需要 4.8 Developer Pack；已经验证的基础构建入口为 `scripts/build.ps1`。

应用使用系统 WPF；视频组件静态链接自己的 FFmpeg、x264 和 zlib，不需要随包携带旧版 avcodec DLL。查看运行组件导入表，可以核对是否只依赖 Windows 系统 DLL。

## 组件范围

原生 FFmpeg 媒体解析、解码、过滤器与 libx264 / zlib 配合完成工具功能。输出编码只启用 libx264、AAC、PCM s16le、PNG，网络输入关闭。没有启用旧版合集里的全部外部解码器或硬件扩展；处理不支持的特殊格式时会报告解码失败。
