# Release 1.1.0 validation

Windows 11 x64, 2026-10-14. Android wire protocol and DSP are unchanged; the existing 1.0.0 APK is compatible. Historical Android 7 / 16 and USB tunnel results are in [VALIDATION.md](VALIDATION.md).

- Release desktop build: 0 warnings, 0 errors with warnings treated as errors.
- All protocol, authentication, packet parsing, slow-client shutdown and downmix regressions pass.
- New process-loopback capture obtains an independent 440 Hz / 0.003-gain test signal while the actual Windows output's master mute is true. This tests the new capture path, not ordinary endpoint loopback.
- Every active render endpoint is muted during a mute lease; numerical master-volume values are unchanged.
- Attempts to unmute an active endpoint during the lease are corrected through notification/monitor reconciliation.
- Normal lease disposal restores each endpoint's original mute flag.
- A separate test process acquired a mute lease and was forcibly terminated. Its independent recovery process restored the original Windows mute flags without restarting the application.

The implementation uses Windows process loopback, requiring build 20348+. Windows 11 is supported; ordinary Windows 10 builds are no longer supported for 1.1.0. Protected content and exclusive audio that bypasses software mute are not covered. A newly activated endpoint can sound briefly before the 250 ms reconciliation sees it. Physical Galaxy S24 / Android 17 and speaker safety are not newly validated.
