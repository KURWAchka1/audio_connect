# Working on Audio Connect

Prefer codebase-memory-mcp for code discovery; index this repository when needed, then use search_graph, trace_path, get_code_snippet, and query_graph. Fall back to text search for configs/literals or insufficient graph results.

- Preserve the USB-only loopback transport and authentication. Do not expose the audio listener on LAN interfaces.
- Never commit credentials, Telegram tokens, signing keys, audio recordings, or machine-specific SDK paths.
- Gain is opt-in and resets on stop. Do not change system volume or bypass hardware/vendor protections. The limiter is not a physical speaker protection system.
- Keep audio queues bounded and packet rate tied to monotonic media time.
- Android minimum API is 24. Guard newer APIs and run `testDebugUnitTest lintRelease`.
- Run `dotnet run --project desktop/AudioConnect.Tests -c Release` for desktop protocol and downmix checks. Hardware capture tests are optional and require a real render endpoint.
- Maintain the same APK signing key across releases, with an increasing Android versionCode.
