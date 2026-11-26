#!/usr/bin/env bash
set -euo pipefail
export PATH="/usr/bin:/mingw64/bin:$PATH"
RECIPE_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "${1:-$RECIPE_DIR}" && pwd)"
TOOLCHAIN="$ROOT/llvm-mingw-20261117-ucrt-x86_64"
export PATH="$TOOLCHAIN/bin:$ROOT/nasm-3.01:$ROOT/pkgconf/mingw64/bin:/usr/bin:/mingw64/bin:$PATH"
export CC=x86_64-w64-mingw32-clang
export AR=llvm-ar
export RANLIB=llvm-ranlib
export STRIP=llvm-strip
export MAKE=mingw32-make
export SHELL=/usr/bin/bash
export LC_ALL=C
PREFIX="$ROOT/prefix"
mkdir -p "$PREFIX/lib/pkgconfig" "$PREFIX/include" "$ROOT/logs" "$ROOT/engine"
PREFIX_WIN="$(cygpath -m "$PREFIX")"
export PKG_CONFIG_LIBDIR="$PREFIX_WIN/lib/pkgconfig"
export PKG_CONFIG_PATH="$PKG_CONFIG_LIBDIR"

echo '[1/3] Building zlib 1.4'
cd "$ROOT/sources/zlib-1.4"
"$MAKE" -f win32/Makefile.gcc -j6 CC="$CC" AR="$AR" RC=llvm-rc STRIP="$STRIP" libz.a >"$ROOT/logs/zlib.log" 2>&1
cp libz.a "$PREFIX/lib/"
cp zlib.h zconf.h "$PREFIX/include/"
sed -e "s|@prefix@|$PREFIX_WIN|g" -e 's|@exec_prefix@|${prefix}|g' -e 's|@libdir@|${prefix}/lib|g' -e 's|@sharedlibdir@|${prefix}/lib|g' -e 's|@includedir@|${prefix}/include|g' -e 's|@VERSION@|1.4|g' zlib.pc.in > "$PREFIX/lib/pkgconfig/zlib.pc"

echo '[2/3] Building x264'
cd "$ROOT/sources/x264-0480cb05fa188d37ae87e8f4fd8f1aea3711f7ee"
./configure --prefix="$PREFIX_WIN" --host=x86_64-w64-mingw32 --cross-prefix=x86_64-w64-mingw32- --enable-static --disable-cli --disable-opencl --enable-strip >"$ROOT/logs/x264-configure.log" 2>&1
"$MAKE" -j6 >"$ROOT/logs/x264.log" 2>&1
"$MAKE" install-lib-static >"$ROOT/logs/x264-install.log" 2>&1

echo '[3/3] Building FFmpeg 8.1.3'
cd "$ROOT/sources/ffmpeg-8.1.3"
./configure --prefix="$PREFIX_WIN" --target-os=mingw32 --arch=x86_64 --enable-cross-compile \
  --cc="$CC" --cxx=x86_64-w64-mingw32-clang++ --ar="$AR" --ranlib="$RANLIB" --nm=llvm-nm --strip="$STRIP" --windres=llvm-windres \
  --pkg-config=pkgconf --pkg-config-flags=--static --enable-gpl --enable-version3 --enable-libx264 --enable-zlib \
  --disable-autodetect --disable-network --disable-doc --disable-debug --disable-shared --enable-static \
  --disable-avdevice --disable-devices --disable-ffplay --disable-pthreads --enable-w32threads \
  --disable-encoders --enable-encoder=libx264,aac,pcm_s16le,png \
  --extra-cflags="-I$PREFIX_WIN/include" --extra-ldflags="-L$PREFIX_WIN/lib -static" \
  --extra-version=framecut-source-build-20261127 >"$ROOT/logs/ffmpeg-configure.log" 2>&1
bash "$RECIPE_DIR/apply-build-fixes.sh" "$ROOT/sources/ffmpeg-8.1.3"
"$MAKE" -j6 ffmpeg.exe ffprobe.exe >"$ROOT/logs/ffmpeg.log" 2>&1
cp ffmpeg.exe ffprobe.exe "$ROOT/engine/"
"$STRIP" "$ROOT/engine/ffmpeg.exe" "$ROOT/engine/ffprobe.exe"
cp config.h config_components.h "$ROOT/logs/"
cp ffbuild/config.mak ffbuild/config.log "$ROOT/logs/"
"$ROOT/engine/ffmpeg.exe" -version > "$ROOT/logs/version.txt"
"$ROOT/engine/ffmpeg.exe" -buildconf > "$ROOT/logs/buildconf.txt" 2>&1
echo 'Engine build completed'
