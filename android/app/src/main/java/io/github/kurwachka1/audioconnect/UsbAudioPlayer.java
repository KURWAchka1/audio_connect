package io.github.kurwachka1.audioconnect;

import android.media.AudioAttributes;
import android.media.AudioFormat;
import android.media.AudioTrack;
import android.os.Build;
import android.os.Process;
import java.io.IOException;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;

/** Network and AudioTrack run independently; the queue always favours fresh audio. */
final class UsbAudioPlayer {
    private final AtomicBoolean stopped = new AtomicBoolean();
    private volatile Socket socket;
    private volatile Thread outputThread;
    private volatile IOException outputError;

    void stop() {
        stopped.set(true);
        closeSocket();
        Thread output = outputThread;
        if (output != null) output.interrupt();
    }

    void stream(String token, int bufferMs) throws IOException, InterruptedException {
        int preload = bufferMs / 10;
        ArrayBlockingQueue<byte[]> queue = new ArrayBlockingQueue<>(preload + 3);
        Socket connection = new Socket();
        socket = connection;
        if (stopped.get()) { closeSocket(); return; }
        try {
            connection.setTcpNoDelay(true);
            connection.setSoTimeout(3000);
            connection.connect(new InetSocketAddress("127.0.0.1", AudioProtocol.PORT), 3000);
            connection.getOutputStream().write(AudioProtocol.hello(token));
            AudioProtocol.readHeader(connection.getInputStream());
            PlaybackState.connected = true;
            PlaybackState.message = "Звук передаётся по USB";
            outputThread = new Thread(() -> play(queue, preload, bufferMs), "audio-output");
            outputThread.start();
            AudioProtocol.Reader reader = new AudioProtocol.Reader();
            while (!stopped.get()) {
                byte[] payload = new byte[AudioProtocol.PAYLOAD_BYTES];
                reader.read(connection.getInputStream(), payload);
                if (!queue.offer(payload)) {
                    queue.poll();
                    PlaybackState.dropped++;
                    queue.offer(payload);
                }
                PlaybackState.packets++;
                if (outputError != null) throw outputError;
            }
        } finally {
            stopped.set(true);
            PlaybackState.connected = false;
            closeSocket();
            Thread output = outputThread;
            if (output != null) { output.interrupt(); output.join(1500); }
            outputThread = null;
        }
    }

    private void play(ArrayBlockingQueue<byte[]> queue, int preload, int bufferMs) {
        Process.setThreadPriority(Process.THREAD_PRIORITY_AUDIO);
        AudioTrack track = null;
        try {
            int minimum = AudioTrack.getMinBufferSize(AudioProtocol.SAMPLE_RATE, AudioFormat.CHANNEL_OUT_STEREO, AudioFormat.ENCODING_PCM_16BIT);
            if (minimum <= 0) throw new IOException("Телефон не поддерживает стерео 48 кГц.");
            AudioTrack.Builder builder = new AudioTrack.Builder()
                    .setAudioAttributes(new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build())
                    .setAudioFormat(new AudioFormat.Builder().setSampleRate(AudioProtocol.SAMPLE_RATE).setChannelMask(AudioFormat.CHANNEL_OUT_STEREO).setEncoding(AudioFormat.ENCODING_PCM_16BIT).build())
                    .setTransferMode(AudioTrack.MODE_STREAM)
                    .setBufferSizeInBytes(Math.max(minimum, AudioProtocol.PAYLOAD_BYTES * preload));
            if (Build.VERSION.SDK_INT >= 26) builder.setPerformanceMode(AudioTrack.PERFORMANCE_MODE_LOW_LATENCY);
            track = builder.build();
            if (track.getState() != AudioTrack.STATE_INITIALIZED) throw new IOException("Не удалось открыть аудиовыход телефона.");
            track.setBufferSizeInFrames(Math.max(minimum / 4, AudioProtocol.SAMPLE_RATE * bufferMs / 1000));
            while (!stopped.get() && queue.size() < preload) Thread.sleep(5);
            if (stopped.get()) return;
            AudioProcessor processor = new AudioProcessor();
            short[] samples = new short[AudioProtocol.FRAMES * AudioProtocol.CHANNELS];
            track.play();
            while (!stopped.get()) {
                byte[] payload = queue.poll(3, TimeUnit.SECONDS);
                if (payload == null) throw new IOException("Аудиоданные не поступают.");
                processor.setGainDb(PlaybackState.gainDb);
                processor.setBassCut(PlaybackState.bassCut);
                processor.process(payload, samples);
                int offset = 0;
                while (offset < samples.length && !stopped.get()) {
                    int written = track.write(samples, offset, samples.length - offset, AudioTrack.WRITE_BLOCKING);
                    if (written <= 0) throw new IOException("Аудиовыход телефона остановился (" + written + ").");
                    offset += written;
                }
                PlaybackState.reductionDb = processor.getReductionDb();
                PlaybackState.underruns = track.getUnderrunCount();
            }
        } catch (InterruptedException interrupted) { Thread.currentThread().interrupt(); }
        catch (IOException | IllegalArgumentException | IllegalStateException error) {
            outputError = error instanceof IOException ? (IOException) error : new IOException("Ошибка аудиовыхода.", error);
            closeSocket();
        } finally {
            if (track != null) {
                try {
                    if (track.getState() == AudioTrack.STATE_INITIALIZED) { track.pause(); track.flush(); }
                } catch (IllegalStateException ignored) { /* The output device may already be gone. */ }
                finally { track.release(); }
            }
        }
    }

    private void closeSocket() {
        Socket connection = socket;
        if (connection != null) try { connection.close(); } catch (IOException ignored) { }
    }
}
