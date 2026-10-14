using AudioConnect.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;

namespace AudioConnect.Windows;

/// <summary>WASAPI loopback, stereo downmix, and continuous 48 kHz packets, including silence.</summary>
public sealed class CapturePipeline : IAsyncDisposable
{
    private readonly IWaveIn capture;
    private readonly BufferedWaveProvider buffer;
    private readonly ISampleProvider samples;
    private readonly Action<byte[]> publish;
    private readonly CancellationTokenSource lifetime = new();
    private Task? pump;
    private float peak;
    private long callbacks;
    public float Peak => Volatile.Read(ref peak);
    public long CaptureCallbacks => Interlocked.Read(ref callbacks);
    public event Action<Exception>? Faulted;

    public CapturePipeline(MMDevice device, Action<byte[]> publishPacket) : this(new WasapiLoopbackCapture(device), publishPacket) { }

    public CapturePipeline(Action<byte[]> publishPacket) : this(new ProcessLoopbackCapture(), publishPacket) { }

    private CapturePipeline(IWaveIn sourceCapture, Action<byte[]> publishPacket)
    {
        publish = publishPacket;
        capture = sourceCapture;
        buffer = new BufferedWaveProvider(capture.WaveFormat)
        {
            BufferDuration = TimeSpan.FromMilliseconds(250), DiscardOnBufferOverflow = true, ReadFully = true
        };
        ISampleProvider source = buffer.ToSampleProvider();
        if (source.WaveFormat.Channels != 2) source = new StereoDownmix(source);
        samples = source.WaveFormat.SampleRate == AudioProtocol.SampleRate
            ? source : new WdlResamplingSampleProvider(source, AudioProtocol.SampleRate);
        capture.DataAvailable += (_, args) =>
        {
            buffer.AddSamples(args.Buffer, 0, args.BytesRecorded);
            Interlocked.Increment(ref callbacks);
        };
        capture.RecordingStopped += (_, args) =>
        {
            if (args.Exception is not null && !lifetime.IsCancellationRequested) Faulted?.Invoke(args.Exception);
        };
    }

    public void Start()
    {
        capture.StartRecording();
        pump = PumpAsync(lifetime.Token);
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(30, cancellationToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
            var frame = new float[AudioProtocol.FramesPerPacket * AudioProtocol.Channels];
            uint sequence = 0;
            var clock = Stopwatch.StartNew();
            long emitted = 0;
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                long due = (long)(clock.Elapsed.TotalMilliseconds / 10);
                if (due - emitted > 4) { buffer.ClearBuffer(); emitted = due - 1; }
                // Recover from a stalled scheduler instead of playing old audio for seconds.
                if (buffer.BufferedDuration > TimeSpan.FromMilliseconds(120)) buffer.ClearBuffer();
                // Windows timers can tick every ~15.6 ms. The media clock, not tick count,
                // determines packet count so playback stays at exactly 48 kHz.
                while (emitted < due)
                {
                    int read = samples.Read(frame, 0, frame.Length);
                    if (read < frame.Length) Array.Clear(frame, read, frame.Length - read);
                    float maximum = 0;
                    foreach (float value in frame) if (float.IsFinite(value)) maximum = Math.Max(maximum, Math.Abs(value));
                    Volatile.Write(ref peak, Math.Min(maximum, 1));
                    publish(AudioProtocol.CreatePacket(frame, sequence++));
                    emitted++;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) { Faulted?.Invoke(error); }
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        capture.StopRecording();
        if (pump is not null) await pump;
        capture.Dispose();
        lifetime.Dispose();
    }
}

/// <summary>Preserves mono; normalizes surround downmix without adding the LFE channel.</summary>
internal sealed class StereoDownmix(ISampleProvider source) : ISampleProvider
{
    private float[] input = [];
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    public int Read(float[] output, int offset, int count)
    {
        int channels = source.WaveFormat.Channels;
        int needed = count / 2 * channels;
        if (input.Length < needed) input = new float[needed];
        int read = source.Read(input, 0, needed);
        int frames = read / channels;
        for (int frame = 0; frame < frames; frame++)
        {
            int start = frame * channels;
            float left = input[start], right = channels == 1 ? left : input[start + 1];
            if (channels == 4)
            {
                left = (left + input[start + 2] * 0.707f) / 1.707f;
                right = (right + input[start + 3] * 0.707f) / 1.707f;
            }
            else if (channels >= 3)
            {
                left += input[start + 2] * 0.707f;
                right += input[start + 2] * 0.707f;
                float weight = 1.707f;
                // Windows default 5.1 / 7.1 channel order: FL, FR, FC, LFE, BL, BR, SL, SR.
                int surroundStart = channels == 5 ? 3 : 4;
                for (int channel = surroundStart; channel + 1 < channels; channel += 2)
                {
                    left += input[start + channel] * 0.707f;
                    right += input[start + channel + 1] * 0.707f;
                    weight += 0.707f;
                }
                left /= weight;
                right /= weight;
            }
            output[offset + frame * 2] = left;
            output[offset + frame * 2 + 1] = right;
        }
        return frames * 2;
    }
}
