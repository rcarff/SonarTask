#!/usr/bin/env bash
# Run on macOS with Xcode Command Line Tools, git and cmake installed.
set -euo pipefail
project_root="$(cd "$(dirname "$0")/../.." && pwd)"
build_root="$(mktemp -d -t sonartask-liblsl)"
git clone --branch v1.16.2 --depth 1 https://github.com/sccn/liblsl.git "$build_root/source"
cmake -S "$build_root/source" -B "$build_root/build" \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_OSX_ARCHITECTURES='x86_64;arm64' \
  -DCMAKE_OSX_DEPLOYMENT_TARGET=11.0 -DLSL_BUILD_EXAMPLES=OFF
cmake --build "$build_root/build" --config Release --parallel
dylib="$(find "$build_root/build" -type f -name 'liblsl*.dylib' | head -n 1)"
test -n "$dylib"
lipo -verify_arch x86_64 arm64 "$dylib"
cp "$dylib" "$project_root/Assets/Plugins/macOS/liblsl.dylib"
install_name_tool -id '@rpath/liblsl.dylib' "$project_root/Assets/Plugins/macOS/liblsl.dylib"
codesign --force --sign - "$project_root/Assets/Plugins/macOS/liblsl.dylib"
echo "Universal liblsl built. Set macOS plugin CPU to AnyCPU in Unity before a universal build."
