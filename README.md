# 帧剪 FrameCut

免安装的 Windows 视频剪辑工具，使用 C#、WPF 和本机 FFmpeg。当前版本 **1.2.1**。

![FrameCut 多视频编排界面](docs/screenshot.png)

## 功能

- 多个视频追加到同一工程，保留已有裁剪和顺序。
- 按实际帧时间前后逐帧查看，设置入点、出点，保存原尺寸 PNG。
- 将画轨切成 A、B、C 等片段，拖动重排，预览并拼接导出。
- 默认只重排画面，声音保持连续；也可让音频随片段移动，或独立调整音频范围。
- 导出 MP4、纯画面、M4A / WAV，或分别导出音画文件。
- 保存工程、撤销重做，支持读取早期单视频工程。

详细操作见 [使用说明](docs/USER-GUIDE.zh-CN.txt)。

## 环境与依赖

- Windows 10 / 11，64 位；.NET Framework 4.8。
- FFmpeg 与 ffprobe。程序通过独立本地进程调用这些工具。
- 无 NuGet、Python、Node.js 或在线服务依赖。

**仓库已含 FFmpeg、x264 和 zlib 的完整固定源码及构建脚本。** 二进制只放在绿色发布包；该运行包的 `sources/` 也包含完整对应源码。见 [依赖说明](docs/DEPENDENCIES.md)。

## 编译

在仓库根目录的 PowerShell 中执行：

```powershell
.\scripts\build.ps1
```

输出为 `build/FrameCut/FrameCut.exe`。脚本使用 Windows 自带的 Framework C# 编译器，包含界面、图标和程序配置，并复制仓库中的小型示例视频。

已有旧版绿色包时，可把它的 `engine` 文件夹作为依赖：

```powershell
.\scripts\build.ps1 -EngineDirectory 'D:\Tools\FrameCut\engine'
```

然后运行 `build/FrameCut/FrameCut.exe`。构建脚本保留 FFmpeg 许可证，记录实际使用的依赖版本。

也可在 Visual Studio 中打开 `FrameCut.sln`，安装 .NET 桌面开发工作负载和 .NET Framework 4.8 Developer Pack 后选择 Release / x64 编译。`Main.xaml` 按嵌入资源加载，不使用自动生成的 WPF 入口。

## 验证

构建时准备好 FFmpeg 后，在 Windows 桌面会话中执行：

```powershell
.\tests\run-tests.ps1 -AppDirectory '.\build\FrameCut'
```

测试使用附带并锁定 SHA-256 的合成视频，检查实际导出的画面、帧数、声音顺序、静音补齐、多素材导入、工程保存恢复和取消行为。界面检查在程序自己的屏外窗口中运行，无需鼠标键盘自动化。

1.2.1 版已用新的源码构建组件重新通过 123 项检查，记录见 [验证记录](docs/verification-v1.2.1.txt)。基础 GitHub 工作流仅编译应用；另有手动完整工作流，从固定源码构建组件、运行媒体检查并生成带源码的绿色包。

## 项目结构

```text
src/          应用代码、界面、图标、编译脚本和 Visual Studio 工程
tests/        实际媒体处理与应用内部界面检查
assets/       项目专用示例视频
scripts/      从仓库构建并准备本地运行目录
docs/         使用、依赖、发布和验证说明
third-party/  FFmpeg 许可、已验证组件版本及散列记录
.github/      仅编译的 Windows 工作流
```

## 当前范围

支持 SDR 视频的精确剪辑和多素材拼接，不含字幕、转场、滤镜、叠画或多层合成。每个素材使用第一条非封面画面轨和第一条音轨。多视频输出以首个素材的画幅为准，其余视频保持比例并补黑边。工程记录路径，不嵌入原视频；最多 200 个素材和 200 个画面片段。

首次导入需扫描帧索引。某些格式连续播放需要生成本地兼容预览。逐帧查看与导出始终读取原文件。源码中的若干控制器方法较集中，后续可继续拆分；当前整理保留了已发布 1.2 版的应用代码。

## 开源与发布

FrameCut 应用源码沿用 [MIT 许可证](LICENSE)。外部 FFmpeg 构建有单独的许可要求，见 [第三方说明](THIRD-PARTY.md)。根许可证不覆盖外部二进制组件。

对应源码、依赖、许可和构建材料已归档在仓库中。新的绿色发布包会自动内嵌完整同版源码 ZIP，可以把运行包和源码包一起放到 Release。见 [发布说明](docs/PUBLISHING.md)。

测试素材可由 tests/generate-fixtures.ps1 的完整配方重新生成；需使用启用了 lavfi 输入的开发用 FFmpeg。运行时不包含设备输入，正常剪辑和媒体测试不依赖该生成器。

