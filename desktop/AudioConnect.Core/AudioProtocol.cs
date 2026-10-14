using System.Buffers.Binary;
using System.Security.Cryptography;

namespace AudioConnect.Core;

/// <summary>Version 1: authenticated, fixed-format PCM over a local USB tunnel.</summary>
public static class AudioProtocol
{
    public const int Port = 40123;
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int FramesPerPacket = 480;
    public const int PayloadBytes = FramesPerPacket * Channels * sizeof(short);
    public const int TokenBytes = 32;
    public const int HeaderBytes = 20;

    public static byte[] CreateHeader()
    {
        var header = new byte[HeaderBytes];
        "ACF1"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), SampleRate);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(8), Channels);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(10), 16);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), FramesPerPacket);
        return header;
    }

    public static bool Authenticate(ReadOnlySpan<byte> hello, ReadOnlySpan<byte> token) =>
        hello.Length == 4 + TokenBytes && token.Length == TokenBytes &&
        hello[..4].SequenceEqual("ACQ1"u8) &&
        CryptographicOperations.FixedTimeEquals(hello[4..], token);

    public static byte[] CreatePacket(ReadOnlySpan<float> samples, uint sequence)
    {
        if (samples.Length != FramesPerPacket * Channels)
            throw new ArgumentException("A packet must contain exactly 10 ms of stereo audio.", nameof(samples));
        var packet = new byte[8 + PayloadBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, sequence);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), PayloadBytes);
        for (int i = 0; i < samples.Length; i++)
        {
            float sample = float.IsFinite(samples[i]) ? Math.Clamp(samples[i], -1f, 1f) : 0;
            short value = (short)Math.Round(sample * 32767f);
            BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(8 + i * 2), value);
        }
        return packet;
    }
}
