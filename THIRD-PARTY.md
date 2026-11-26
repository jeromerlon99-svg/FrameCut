# 第三方组件与完整对应源码

本项目现在随源码仓库保存固定的 FFmpeg、x264 和 zlib 原始源码包，并提供实际使用的编译脚本。新的 1.2.1 绿色包采用这些源码直接编译的组件，替换早期第三方大合集。

## FrameCut 应用

FrameCut 自身 C# / WPF 源码沿用根目录 [MIT 许可证](LICENSE)。它通过独立本地进程调用 FFmpeg，没有链接 FFmpeg DLL。

## 新版视频组件

| 组件 | 固定输入 | 许可 |
| --- | --- | --- |
| FFmpeg | 8.1.3，官方原始发行包 | 构建启用 GPL 与 version3，整体 GPL-3.0-or-later |
| x264 | 0480cb05fa188d37ae87e8f4fd8f1aea3711f7ee | GPL-2.0-or-later，在本组合中按 GPL-3.0-or-later 分发 |
| zlib | 1.4，官方原始发行包 | Zlib |

每个原始包的来源、精确版本和 SHA-256 在 [engine-sources.lock.json](third-party/engine-sources.lock.json)，文件本体位于 `third-party/source-archives/`。x264 原始包由该固定提交的 `git archive` 生成，其他两个原始发行包保留原字节。原始源码包保持原字节；构建时对 FFmpeg 的 Windows make 响应文件输出应用一个补丁（`third-party/ffmpeg-windows-make.patch`），完整补丁和自动应用脚本已提供。配置与构建命令见 [build-engine.sh](scripts/build-engine.sh)。

所有直接链接的视频处理外部库只有 x264 和 zlib。工具链运行库的 LLVM 及 MinGW 许可通知另见 `third-party/licenses/`。Windows 系统组件由操作系统提供，编译器、汇编器和 pkgconf 属于构建工具，不随运行包提供。

新运行包里的 `sources/FrameCut-Source-v1.2.1.zip` 包含应用源码、全部上述原始源码包、构建脚本、锁定文件和许可证。获取随包对应源码不需要另找服务器，也不依赖只有网址的承诺。

## 构建记录

- [原始源码锁定](third-party/engine-sources.lock.json)
- [实际发布二进制与散列](third-party/tested-engine.json)
- `third-party/build-record/`：实际生成的配置、构建选项和依赖配置记录。
- `third-party/licenses/`：FFmpeg、x264、zlib、LLVM / MinGW 许可文本。

本组件面向本地剪辑，仅保留工具需要的 H.264、AAC、WAV PCM 和 PNG 输出编码器；关闭网络输入与非必要外部库。不是早期多格式大合集的全部扩展库 / 硬件功能复制品。具体启用组件可由 `ffmpeg -buildconf`、`-decoders` 和 `-encoders` 查看。

## 早期版本记录

1.2.0 曾使用 BtbN 的 `n8.1.3-24-g92611900bb-20261025` 构建。那套组件不再作为新版发布包的一部分，其散列记录仅作为历史资料保存。本轮对应源码材料针对新的 1.2.1 自编译组件，不把新源码错误套用于旧二进制。

