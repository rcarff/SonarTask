# Vendored LSL dependencies

Downloaded 2026-11-10 from the official projects. MIT licenses are included alongside this file.

| File | Source | SHA-256 |
| --- | --- | --- |
| ../x86_64/lsl.dll | https://github.com/sccn/liblsl/releases/download/v1.16.2/liblsl-1.16.2-Win_amd64.zip (bin/lsl.dll) | 338b737943a4bd7bf9ff719985e58927be2ad4000820699c1192ee809207c46a |
| ../macOS/liblsl.dylib | https://github.com/sccn/liblsl/releases/download/v1.16.2/liblsl-1.16.2-OSX_amd64.tar.bz2 (lib/liblsl.1.16.2.dylib) | ec12d33c66eaec84ae1b765d2d47a6edf111e995eb618af2638efb6f78f894dc |
| LSL.cs | https://raw.githubusercontent.com/labstreaminglayer/liblsl-Csharp/master/LSL.cs | df5af4ffe966ea64b3e84d6b1c68bcbcf9fda2dc44e43242d64063b1bf315bf7 |

The binding hash describes the checked-in version after adding the `!UNITY_WEBGL || UNITY_EDITOR` guard and changing incoming sample string decoding to UTF-8. Outgoing markers escape non-ASCII JSON text to avoid platform-dependent ANSI marshalling. The upstream macOS asset is Intel only. Tools/LslBridge/build_macos_universal.sh builds the same native version from source for both x86_64 and arm64; update the hash after replacing that artifact and verify signing/notarization on macOS. Unity plugin metadata restricts each library to its matching desktop OS and excludes WebGL.
