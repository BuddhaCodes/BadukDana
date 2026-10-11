#!/usr/bin/env bash
# Builds KataGo for Android (arm64, CPU/Eigen backend) with the Android NDK and puts it where Hoshi.Android
# packs it: src/Hoshi.Android/katago/arm64-v8a/libkatago.so. It is an executable, named like a library so
# Android installs it in the app's native library folder, the only place apps may start programs from.
#
#   ANDROID_NDK_HOME=/path/to/ndk tools/android/build-katago.sh
#
# KataGo is MIT-licensed (licenses/MIT-KataGo.txt); Eigen (headers only) is MPL-2.0.
set -euo pipefail

KATAGO_VERSION="${KATAGO_VERSION:-v1.17.1}"
EIGEN_VERSION="${EIGEN_VERSION:-3.4.0}"
API="${ANDROID_API:-26}"
NDK="${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-${ANDROID_NDK_LATEST_HOME:-}}}"
[ -n "$NDK" ] && [ -f "$NDK/build/cmake/android.toolchain.cmake" ] || { echo "Set ANDROID_NDK_HOME to an Android NDK" >&2; exit 1; }

root="$(cd "$(dirname "$0")/../.." && pwd)"
out="$root/src/Hoshi.Android/katago/arm64-v8a"
work="${WORK_DIR:-$(mktemp -d)}"
jobs="$(nproc 2>/dev/null || sysctl -n hw.ncpu)"

echo "KataGo $KATAGO_VERSION · Eigen $EIGEN_VERSION · android-$API · NDK $NDK"
[ -d "$work/KataGo" ] || git clone --depth 1 --branch "$KATAGO_VERSION" https://github.com/lightvector/KataGo "$work/KataGo"
if [ ! -d "$work/eigen-install" ]; then
  curl -fsSL "https://gitlab.com/libeigen/eigen/-/archive/$EIGEN_VERSION/eigen-$EIGEN_VERSION.tar.gz" | tar -xz -C "$work"
  cmake -S "$work/eigen-$EIGEN_VERSION" -B "$work/eigen-build" -DCMAKE_INSTALL_PREFIX="$work/eigen-install" -DBUILD_TESTING=OFF -DEIGEN_BUILD_DOC=OFF >/dev/null
  cmake --install "$work/eigen-build" >/dev/null
fi

cmake -S "$work/KataGo/cpp" -B "$work/build" \
  -DCMAKE_TOOLCHAIN_FILE="$NDK/build/cmake/android.toolchain.cmake" \
  -DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM="android-$API" -DANDROID_STL=c++_static \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_FIND_ROOT_PATH_MODE_PACKAGE=BOTH \
  -DEigen3_DIR="$work/eigen-install/share/eigen3/cmake" \
  -DEIGEN3_INCLUDE_DIRS="$work/eigen-install/include/eigen3" \
  -DUSE_BACKEND=EIGEN -DUSE_AVX2=0 -DNO_GIT_REVISION=1 -DNO_LIBZIP=1
cmake --build "$work/build" -j"$jobs"

mkdir -p "$out"
cp "$work/build/katago" "$out/libkatago.so"
"$NDK"/toolchains/llvm/prebuilt/*/bin/llvm-strip "$out/libkatago.so"
file "$out/libkatago.so" 2>/dev/null || true
ls -la "$out/libkatago.so"
