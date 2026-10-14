# USB audio protocol v1

Transport: TCP socket bound to IPv4 loopback on Windows, port 40123. The desktop creates `adb -s SERIAL reverse tcp:40123 tcp:40123`. Android initiates the loopback connection. Android 7+ supports this transport. All integers are little-endian; TCP reads must handle fragmentation.

1. Client sends exactly 36 bytes: ASCII `ACQ1`, then the 32 raw bytes of the session pairing token. The desktop compares the token in constant time and closes unauthorized sockets without audio. Handshake timeout: 3 seconds.
2. Server sends exactly 20 bytes: ASCII `ACF1`; uint32 sample rate 48000; uint16 channel count 2; uint16 sample bits 16; uint32 frames per packet 480; uint32 reserved 0. Android rejects any different format/version/reserved value.
3. Each audio packet: uint32 sequence; uint32 payload length 1920; 1920 bytes of signed PCM16, stereo LR interleaved. Sequence increments modulo 2^32. Gaps are allowed because stale queued audio can be dropped. Duplicates or backwards sequences are rejected. Receiver length is fixed; peer-supplied lengths never allocate memory.

Packets represent 10 ms of audio. Silence is transmitted continuously so disconnection detection does not depend on audible content. Sender uses elapsed monotonic time to compensate for coarse Windows timer ticks. The capture queue holds at most 3 packets and drops oldest packets. A network write taking more than 1 second disconnects the receiver. Android receive timeout: 3 seconds. Android's playback queue holds the requested preload plus 3 packets and drops oldest on overflow.

Only one client is streamed at a time. A reconnect begins with a new header and fresh queue. The token rotates when the desktop starts a new session; ADB explicitly launches the Android Activity with the new token. Audio is not encrypted beyond USB's transport. A local administrator or a process able to observe/debug the applications is outside this trust model.

DSP runs only on Android: optional 120 Hz second-order Butterworth high-pass; smoothed gain up to 12 dB; stereo-linked 240-frame lookahead limiter with a monotonic peak deque, immediate attenuation and ~150 ms release. Output is bounded to −1 dBFS. No EQ bass boost, system volume change, root, or vendor protection bypass is used.
