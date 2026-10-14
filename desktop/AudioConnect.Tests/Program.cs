using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using AudioConnect.Core;
using AudioConnect.Windows;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;

if (args.Length == 2 && args[0] == "--mute-lease-hold")
{
    using var heldMute = new LocalOutputMuteLease();
    File.WriteAllText(args[1], "ready");
    await Task.Delay(Timeout.Infinite);
    return;
}

if (args.Contains("--tone"))
{
    using var toneEnumerator = new MMDeviceEnumerator();
    using MMDevice toneDevice = toneEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    using var toneOutput = new WasapiOut(toneDevice, AudioClientShareMode.Shared, false, 50);
    toneOutput.Init(new SignalGenerator(48000, 2) { Frequency = 440, Gain = 0.003, Type = SignalGeneratorType.Sin });
    toneOutput.Play();
    await Task.Delay(12000);
    toneOutput.Stop();
    return;
}

static void Check(bool value, string description)
{
    if (!value) throw new InvalidOperationException(description);
    Console.WriteLine("PASS " + description);
}

byte[] token = RandomNumberGenerator.GetBytes(32);
byte[] hello = new byte[36]; "ACQ1"u8.CopyTo(hello); token.CopyTo(hello, 4);
Check(AudioProtocol.Authenticate(hello, token), "correct token accepted");
hello[4] ^= 1;
Check(!AudioProtocol.Authenticate(hello, token), "wrong token rejected");
hello[4] ^= 1;
Check(!AudioProtocol.Authenticate(hello.AsSpan(0, 35), token), "truncated handshake rejected");
float[] frame = new float[960];
frame[0] = float.NaN; frame[1] = float.PositiveInfinity; frame[2] = -100; frame[3] = 100;
byte[] packet = AudioProtocol.CreatePacket(frame, uint.MaxValue);
Check(packet.Length == 1928 && BinaryPrimitives.ReadUInt32LittleEndian(packet) == uint.MaxValue, "wire dimensions and sequence");
Check(BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(8)) == 0 && BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(10)) == 0, "non-finite audio becomes silence");
Check(BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(12)) == -32767 && BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(14)) == 32767, "PCM conversion cannot overflow");

foreach ((int channels, float[] input, float expectedLeft, float expectedRight) in new[]
{
    (1, new float[] { 0.25f }, 0.25f, 0.25f),
    (4, new float[] { 0, 0, 1, 0 }, 0.707f / 1.707f, 0f),
    (5, new float[] { 0, 0, 0, 0, 1 }, 0f, 0.707f / 2.414f),
    (6, new float[] { 0, 0, 0, 1, 0, 0 }, 0f, 0f)
})
{
    var downmix = new StereoDownmix(new FixedSamples(channels, input));
    float[] output = new float[2]; downmix.Read(output, 0, output.Length);
    Check(Math.Abs(output[0] - expectedLeft) < 0.00001f && Math.Abs(output[1] - expectedRight) < 0.00001f, $"{channels}-channel downmix preserves layout and excludes LFE");
}

await using (var server = new StreamServer(token, 40124))
{
    server.Start();
    using var invalid = new TcpClient();
    await invalid.ConnectAsync("127.0.0.1", 40124);
    byte[] wrong = (byte[])hello.Clone(); wrong[4] ^= 1;
    await invalid.GetStream().WriteAsync(wrong);
    using var timeout = new CancellationTokenSource(5000);
    Check(await invalid.GetStream().ReadAsync(new byte[1], timeout.Token) == 0, "unauthenticated socket is closed");
    using var client = new TcpClient();
    await client.ConnectAsync("127.0.0.1", 40124);
    // Exercise partial TCP reads instead of assuming one write equals one read.
    await client.GetStream().WriteAsync(hello.AsMemory(0, 3));
    await Task.Delay(10);
    await client.GetStream().WriteAsync(hello.AsMemory(3));
    byte[] header = new byte[20];
    await client.GetStream().ReadExactlyAsync(header, timeout.Token);
    Check(header.AsSpan(0, 4).SequenceEqual("ACF1"u8), "fragmented handshake accepted");
    while (!server.IsConnected) await Task.Delay(1, timeout.Token);
    server.Enqueue(packet);
    byte[] received = new byte[packet.Length];
    await client.GetStream().ReadExactlyAsync(received, timeout.Token);
    Check(received.SequenceEqual(packet), "PCM packet reaches receiver unchanged");
    // A stalled client must not hold shutdown hostage.
    for (uint i = 0; i < 10_000; i++) server.Enqueue(AudioProtocol.CreatePacket(frame, i));
    var stopped = System.Diagnostics.Stopwatch.StartNew();
    await server.DisposeAsync();
    Check(stopped.Elapsed < TimeSpan.FromSeconds(2), "slow receiver does not block shutdown");
}

if (args.Contains("--capture"))
{
    using var enumerator = new MMDeviceEnumerator();
    using MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    long packets = 0; float maximum = 0;
    await using var capture = new CapturePipeline(device, data => { Interlocked.Increment(ref packets); });
    capture.Start();
    using var output = new WasapiOut(device, AudioClientShareMode.Shared, false, 50);
    output.Init(new SignalGenerator(48000, 2) { Frequency = 440, Gain = 0.003, Type = SignalGeneratorType.Sin });
    output.Play();
    for (int i = 0; i < 20; i++) { await Task.Delay(100); maximum = Math.Max(maximum, capture.Peak); }
    output.Stop();
    Check(capture.CaptureCallbacks > 0 && maximum > 0.00001f, "WASAPI captures a low-level test tone");
    Check(packets >= 190, "media clock maintains 100 packets/sec despite Windows timer granularity");
}

if (args.Contains("--capture-muted"))
{
    using var mutedEnumerator = new MMDeviceEnumerator();
    using MMDevice mutedDevice = mutedEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    bool previousMute = mutedDevice.AudioEndpointVolume.Mute;
    float previousVolume = mutedDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
    float maximum = 0;
    try
    {
        mutedDevice.AudioEndpointVolume.Mute = true;
        await using var mutedCapture = new CapturePipeline(_ => { });
        mutedCapture.Start();
        for (int i = 0; i < 30; i++) { await Task.Delay(100); maximum = Math.Max(maximum, mutedCapture.Peak); }
        Check(mutedDevice.AudioEndpointVolume.Mute, "Windows output remains muted during capture");
        Check(maximum > 0.0001f, "process loopback captures an independent test tone while Windows is muted");
        Check(mutedDevice.AudioEndpointVolume.MasterVolumeLevelScalar == previousVolume, "master volume value stays unchanged");
    }
    finally { mutedDevice.AudioEndpointVolume.Mute = previousMute; }
    Check(mutedDevice.AudioEndpointVolume.Mute == previousMute, "original Windows mute state restored");
}

if (args.Contains("--mute-lease"))
{
    using var muteEnumerator = new MMDeviceEnumerator();
    var originalOutputs = new List<(MMDevice Device, bool Muted, float Volume)>();
    foreach (MMDevice outputDevice in muteEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        originalOutputs.Add((outputDevice, outputDevice.AudioEndpointVolume.Mute, outputDevice.AudioEndpointVolume.MasterVolumeLevelScalar));
    try
    {
        using (var lease = new LocalOutputMuteLease())
        {
            await Task.Delay(500);
            Check(originalOutputs.All(item => item.Device.AudioEndpointVolume.Mute), "mute lease silences every active Windows output");
            Check(originalOutputs.All(item => item.Device.AudioEndpointVolume.MasterVolumeLevelScalar == item.Volume), "mute lease preserves master volume values");
            if (originalOutputs.Count > 0)
            {
                originalOutputs[0].Device.AudioEndpointVolume.Mute = false;
                await Task.Delay(500);
                Check(originalOutputs[0].Device.AudioEndpointVolume.Mute, "Windows unmute is suppressed while phone-only session owns the output");
            }
        }
        Check(originalOutputs.All(item => item.Device.AudioEndpointVolume.Mute == item.Muted), "normal stop restores original mute flags");
        if (originalOutputs.Count > 0)
        {
            originalOutputs[0].Device.AudioEndpointVolume.Mute = true;
            using (var alreadyMuted = new LocalOutputMuteLease()) { }
            Check(originalOutputs[0].Device.AudioEndpointVolume.Mute, "an output that was already muted stays muted after stop");
            originalOutputs[0].Device.AudioEndpointVolume.Mute = originalOutputs[0].Muted;
        }

        string marker = Path.Combine(Path.GetTempPath(), "AudioConnect-crash-test-" + Guid.NewGuid().ToString("N"));
        string host = Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
        var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(typeof(FixedSamples).Assembly.Location);
        start.ArgumentList.Add("--mute-lease-hold"); start.ArgumentList.Add(marker);
        using Process worker = Process.Start(start) ?? throw new IOException("Crash-test process did not start");
        try
        {
            using var deadline = new CancellationTokenSource(15000);
            while (!File.Exists(marker)) { if (worker.HasExited) throw new IOException("Crash-test process exited early"); await Task.Delay(50, deadline.Token); }
            Check(originalOutputs.All(item => item.Device.AudioEndpointVolume.Mute), "crash-test process holds endpoint mute");
            worker.Kill(); await worker.WaitForExitAsync(deadline.Token);
            while (originalOutputs.Any(item => item.Device.AudioEndpointVolume.Mute != item.Muted)) await Task.Delay(50, deadline.Token);
            Check(true, "independent watchdog restores Windows sound after forced process termination");
        }
        finally { if (!worker.HasExited) worker.Kill(); if (File.Exists(marker)) File.Delete(marker); }
    }
    finally
    {
        foreach (var item in originalOutputs) { item.Device.AudioEndpointVolume.Mute = item.Muted; item.Device.Dispose(); }
    }
}

if (args.Contains("--android"))
{
    string adbPath = Environment.GetEnvironmentVariable("AC_TEST_ADB") ?? throw new InvalidOperationException("Set AC_TEST_ADB");
    string serial = Environment.GetEnvironmentVariable("AC_TEST_SERIAL") ?? throw new InvalidOperationException("Set AC_TEST_SERIAL");
    var adb = new AdbBridge(adbPath);
    var server = new StreamServer(token);
    try
    {
    server.Start();
    await adb.ConnectAsync(serial, Convert.ToHexString(token).ToLowerInvariant(), CancellationToken.None);
    long total = 0;
    using var ticker = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
    int seconds = int.TryParse(Environment.GetEnvironmentVariable("AC_TEST_SECONDS"), out int duration) ? duration : 30;
    var clock = System.Diagnostics.Stopwatch.StartNew();
    uint sequence = 0;
    long emitted = 0;
    bool restarted = false;
    long beforeRestart = 0;
    while (clock.Elapsed.TotalSeconds < seconds && await ticker.WaitForNextTickAsync())
    {
        if (args.Contains("--reconnect") && !restarted && clock.Elapsed.TotalSeconds > 15)
        {
            beforeRestart = server.SentPackets;
            await server.DisposeAsync();
            await Task.Delay(3000);
            server = new StreamServer(token); server.Start();
            restarted = true;
            emitted = (long)(clock.Elapsed.TotalMilliseconds / 10);
        }
        long due = (long)(clock.Elapsed.TotalMilliseconds / 10);
        while (emitted < due)
        {
            for (int i = 0; i < frame.Length; i += 2)
            {
                float value = (float)(Math.Sin(2 * Math.PI * 440 * total++ / 48000) * 0.003);
                frame[i] = frame[i + 1] = value;
            }
            server.Enqueue(AudioProtocol.CreatePacket(frame, sequence++));
            emitted++;
        }
    }
    Console.WriteLine($"Android packets sent: {server.SentPackets}; generated: {sequence}");
    Check(server.SentPackets > 2000, "Android authenticated and received 20+ seconds of USB-tunnel PCM");
    if (restarted) Check(beforeRestart > 1000, "Android recovered after a three-second server interruption");
    await adb.RemoveTunnelAsync(serial);
    }
    finally { await server.DisposeAsync(); }
}

Console.WriteLine("All checks passed.");

sealed class FixedSamples(int channels, float[] values) : ISampleProvider
{
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, channels);
    public int Read(float[] buffer, int offset, int count) { Array.Copy(values, 0, buffer, offset, Math.Min(count, values.Length)); return Math.Min(count, values.Length); }
}
