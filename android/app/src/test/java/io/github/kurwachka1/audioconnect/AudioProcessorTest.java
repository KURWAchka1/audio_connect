package io.github.kurwachka1.audioconnect;

import org.junit.Test;
import static org.junit.Assert.*;
import java.util.Random;

public class AudioProcessorTest {
    private byte[] tone(double frequency, double amplitude, int frames) {
        byte[] result = new byte[frames * 4];
        for (int i = 0; i < frames; i++) {
            short sample = (short) Math.round(Math.sin(2 * Math.PI * frequency * i / 48000) * amplitude * 32767);
            for (int channel = 0; channel < 2; channel++) { int p = i * 4 + channel * 2; result[p] = (byte) sample; result[p + 1] = (byte) (sample >> 8); }
        }
        return result;
    }
    private double rms(short[] values, int start) { double sum = 0; for (int i = start; i < values.length; i++) sum += (double) values[i] * values[i]; return Math.sqrt(sum / (values.length - start)); }

    @Test public void gainMakesQuietAudioLouder() {
        AudioProcessor processor = new AudioProcessor(); processor.setBassCut(false); processor.setGainDb(12);
        byte[] input = tone(1000, 0.05, 48000); short[] result = new short[input.length / 2]; processor.process(input, result);
        assertEquals(0.05 * 32767 / Math.sqrt(2) * Math.pow(10, 12.0 / 20), rms(result, 12000), 10);
    }
    @Test public void fullScaleAtMaximumGainNeverClips() {
        AudioProcessor processor = new AudioProcessor(); processor.setBassCut(false); processor.setGainDb(12);
        byte[] input = tone(1000, 1, 48000); short[] result = new short[input.length / 2]; processor.process(input, result);
        for (short value : result) assertTrue(Math.abs((int) value) <= Math.ceil(AudioProcessor.CEILING * 32767));
        assertTrue(processor.getReductionDb() > 10);
    }
    @Test public void stereoImageRemainsLinked() {
        AudioProcessor processor = new AudioProcessor(); processor.setBassCut(false); processor.setGainDb(12);
        byte[] input = tone(1000, 0.8, 5000); short[] result = new short[input.length / 2]; processor.process(input, result);
        for (int i = 0; i < result.length; i += 2) assertEquals(result[i], result[i + 1]);
    }
    @Test public void bassCutAttenuatesSubBass() {
        byte[] input = tone(30, 0.1, 48000);
        AudioProcessor cut = new AudioProcessor(), flat = new AudioProcessor(); flat.setBassCut(false);
        short[] filtered = new short[input.length / 2], original = filtered.clone(); cut.process(input, filtered); flat.process(input, original);
        assertTrue(rms(filtered, 12000) / rms(original, 12000) < 0.08);
    }
    @Test public void silenceStaysSilent() {
        AudioProcessor processor = new AudioProcessor(); processor.setGainDb(12);
        short[] output = new short[960]; processor.process(new byte[1920], output);
        for (short sample : output) assertEquals(0, sample);
    }
    @Test public void impulseAndRandomSignalStayBoundedAcrossPacketBoundaries() {
        AudioProcessor processor = new AudioProcessor(); processor.setGainDb(12);
        Random random = new Random(42); short[] output = new short[960]; byte[] packet = new byte[1920];
        for (int block = 0; block < 250; block++) {
            random.nextBytes(packet); processor.process(packet, output);
            for (short value : output) assertTrue(Math.abs((int) value) <= 29204);
        }
    }
    @Test public void invalidGainDoesNotPoisonOutput() {
        AudioProcessor processor = new AudioProcessor(); processor.setGainDb(Float.NaN); processor.setBassCut(false);
        byte[] input = tone(1000, 0.1, 5000); short[] output = new short[input.length / 2]; processor.process(input, output);
        assertTrue(rms(output, 3000) > 2000 && rms(output, 3000) < 2500);
    }
}
