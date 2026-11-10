# Third-party dependencies

This source project references packages which Unity/NuGet will restore from their normal package sources; their binaries are not vendored in this archive.

- Unity Visual Studio Editor package (`com.unity.ide.visualstudio`) 2.0.25 — Unity Technologies.
- Newtonsoft JSON for Unity (`com.unity.nuget.newtonsoft-json`) 3.2.1.
- Unity uGUI (`com.unity.ugui`) 2.0.0 — Unity Technologies.
- `Konscious.Security.Cryptography.Argon2` 1.3.1 — server password hashing.
- `Microsoft.Data.Sqlite` 10.0.11 — server result index.
- .NET / ASP.NET Core 10 runtime container images — Microsoft.
- nginx container image — nginx project.

Consult each package's own license metadata when distributing a built application.

- liblsl 1.16.2 native libraries — Christian A. Kothe and contributors, MIT; [official release](https://github.com/sccn/liblsl/releases/tag/v1.16.2). License: Assets/Plugins/LSL/liblsl-LICENSE.txt. Bundled targets are Windows x64 and macOS Intel x86_64.
- liblsl-Csharp LSL.cs — Christian Kothe and contributors, MIT; [upstream binding](https://github.com/labstreaminglayer/liblsl-Csharp). License: Assets/Plugins/LSL/LICENSE.txt. Local changes: excluded from WebGL compilation with a preprocessor guard; inbound sample strings decode as UTF-8 on Windows as required by liblsl. Outgoing marker JSON escapes non-ASCII characters for compatibility with the binding's ANSI string marshaling.
- The optional bridge/examples depend on pylsl, websockets, matplotlib and NumPy; install through Tools/LslBridge/requirements.txt and retain their own license notices when redistributing a packaged bridge.
