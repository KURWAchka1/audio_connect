# Third-party components

Audio Connect is MIT licensed. Its distributions contain separately licensed components:

- NAudio 2.2.1 — MIT, Copyright Mark Heath and contributors. https://github.com/naudio/NAudio/blob/v2.2.1/license.txt
- NAudio's WDL resampler — Cockos/WDL permissive license, included in the NAudio source license notices. https://github.com/naudio/NAudio
- Microsoft .NET 8 runtime / Windows Desktop runtime — MIT and component notices. The self-contained Windows distribution includes the runtime's `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt`. https://github.com/dotnet/runtime
- Android Debug Bridge and Windows USB support libraries — Android Open Source Project and component licenses. The installer includes `platform-tools/NOTICE.txt` from the platform-tools distribution. Only ADB and its required DLLs are included; no system USB drivers are installed. https://android.googlesource.com/platform/packages/modules/adb/

Build-only dependencies: Android Gradle Plugin (Apache 2.0), Gradle (Apache 2.0), JUnit 4 (EPL 1.0), Inno Setup (its own license). These tools are not bundled with the app.

NAudio license:

Copyright 2020 Mark Heath

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
