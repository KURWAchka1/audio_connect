package io.github.kurwachka1.audioconnect;

import java.io.EOFException;
import java.io.IOException;
import java.io.InputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;

/** Fixed-size framing prevents untrusted lengths from allocating memory or overflowing playback. */
public final class AudioProtocol {
    public static final int PORT = 40123, SAMPLE_RATE = 48000, CHANNELS = 2;
    public static final int FRAMES = 480, PAYLOAD_BYTES = FRAMES * CHANNELS * 2;
    private AudioProtocol() { }

    public static byte[] hello(String token) throws IOException {
        if (token == null || !token.matches("[a-fA-F0-9]{64}")) throw new IOException("Нужно подключение из приложения Windows.");
        byte[] hello = new byte[36];
        System.arraycopy("ACQ1".getBytes(StandardCharsets.US_ASCII), 0, hello, 0, 4);
        for (int i = 0; i < 32; i++) hello[4 + i] = (byte) Integer.parseInt(token.substring(i * 2, i * 2 + 2), 16);
        return hello;
    }

    public static void readHeader(InputStream input) throws IOException {
        byte[] header = new byte[20];
        readFully(input, header);
        ByteBuffer data = ByteBuffer.wrap(header).order(ByteOrder.LITTLE_ENDIAN);
        if (!Arrays.equals(Arrays.copyOf(header, 4), "ACF1".getBytes(StandardCharsets.US_ASCII)) ||
                data.getInt(4) != SAMPLE_RATE || data.getShort(8) != CHANNELS || data.getShort(10) != 16 ||
                data.getInt(12) != FRAMES || data.getInt(16) != 0) throw new IOException("Несовместимая версия приложения Windows.");
    }

    public static final class Reader {
        private final byte[] header = new byte[8];
        private long last = -1;
        public long read(InputStream input, byte[] payload) throws IOException {
            if (payload.length != PAYLOAD_BYTES) throw new IllegalArgumentException("Invalid PCM buffer");
            readFully(input, header);
            ByteBuffer data = ByteBuffer.wrap(header).order(ByteOrder.LITTLE_ENDIAN);
            long sequence = Integer.toUnsignedLong(data.getInt(0));
            if (data.getInt(4) != PAYLOAD_BYTES) throw new IOException("Повреждённый аудиопакет.");
            if (last >= 0) {
                long delta = (sequence - last) & 0xffffffffL;
                if (delta == 0 || delta > 0x7fffffffL) throw new IOException("Неверный порядок аудиопакетов.");
            }
            last = sequence;
            readFully(input, payload);
            return sequence;
        }
    }

    static void readFully(InputStream input, byte[] destination) throws IOException {
        int offset = 0;
        while (offset < destination.length) {
            int read = input.read(destination, offset, destination.length - offset);
            if (read < 0) throw new EOFException("Передача с компьютера остановлена.");
            if (read == 0) continue;
            offset += read;
        }
    }
}
