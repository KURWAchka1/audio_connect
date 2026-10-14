# Release 1.0.0 validation

Verified on 2026-10-14, using Windows 11 x64 and Android 7.0 / API 24 and Android 16 / API 36 x86_64 emulators. Physical USB audio playback on the user's Galaxy S24 / Android 17 is not verified because no phone is attached to the build host.

Completed:

- Desktop Release build, warnings treated as errors: 0 warnings, 0 errors.
- Desktop protocol tests: correct/incorrect/truncated authentication, fragmented TCP handshake, exact packet delivery, finite PCM conversion, signed PCM bounds, and non-blocking shutdown with a stalled receiver.
- Windows WASAPI loopback captured a 440 Hz tone at gain 0.003. Media clock produced 100 audio packets/sec despite coarse Windows timer intervals.
- Android unit suite: 15 tests, 0 failures. Tested quiet-signal gain, limiter bounds, random PCM across packets, stereo linking, sub-bass attenuation, invalid gain, fragmented headers, bad lengths, truncation, sequence wrap, duplicates and pairing-token validation.
- Android release lint: no API compatibility errors. APK minimum SDK is 24; target and compile SDK are 36. Newer service/focus/UI APIs are guarded.
- Signed and minified universal release APK: `apksigner verify` succeeds; APK Signature Scheme v2 supports Android 7. Signing certificate SHA-256: `fdc7a85981b2a59dfaaa7065784dce58f8af3212d38f9f9dd5c25ad826ffbf99`.
- USB tunnel integration (`adb reverse`) authenticated and sent approximately 5900 packets over 60 seconds; background playback remained active with screen off. Android AudioFlinger reported the application's 48 kHz AudioTrack and zero reported underruns at the captured observation point.
- Android 7.0 installed the same signed, minified release APK and streamed PCM through ADB reverse. Both API 24 and API 36 recovered automatically after a three-second server interruption; each received 4197 packets after recovery in a 60-second run. Foreground playback was confirmed with the screen off on both versions.
- Mono, quad, 5.0 and 5.1 downmix regression checks passed, including correct quad channel placement and exclusion of the 5.1 LFE channel.
- GitHub-hosted Verify workflow passed both Windows and Android jobs after replacing the removed SDK `tools` package in the CI setup.
- Windows installer installed to a fresh test directory with exit 0. The installed self-contained executable launched and rendered the interface with exit 0. Uninstall completed with exit 0 and removed its executable.
- Screenshots inspected for readable controls and complete access to connection, buffer and gain settings. Desktop guide scrolls at small window heights; Android layout scrolls in landscape.

Limits: emulator testing cannot measure physical speaker temperature, excursion, distortion, actual acoustic gain, USB cable behaviour, Samsung battery policy, or end-to-end latency. Digital limiter tests establish PCM bounds only. Windows installer is unsigned with Authenticode; the Android APK is signed with the project's permanent release key. Detailed speaker-risk notes and source links are in the README.
