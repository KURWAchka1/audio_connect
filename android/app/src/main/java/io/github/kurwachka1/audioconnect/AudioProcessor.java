package io.github.kurwachka1.audioconnect;

/** Stereo-linked 5 ms lookahead peak limiter; no native code or per-sample allocations. */
public final class AudioProcessor {
    public static final float CEILING = 0.89125094f; // -1 dBFS, not a physical speaker safety limit.
    private static final int LOOKAHEAD = 240;
    private final float[] leftDelay = new float[LOOKAHEAD + 1], rightDelay = new float[LOOKAHEAD + 1];
    private final float[] maxima = new float[LOOKAHEAD + 2];
    private final long[] positions = new long[LOOKAHEAD + 2];
    private int head, tail;
    private long frameIndex;
    private float currentGain = 1, limiterGain = 1;
    private final HighPass leftFilter = new HighPass(), rightFilter = new HighPass();
    private volatile float targetGain = 1;
    private volatile boolean bassCut = true;
    private boolean previousBassCut = true;
    private volatile float reductionDb;

    public void setGainDb(float db) {
        if (!Float.isFinite(db)) db = 0;
        targetGain = (float) Math.pow(10, Math.max(0, Math.min(12, db)) / 20);
    }
    public void setBassCut(boolean enabled) { bassCut = enabled; }
    public float getReductionDb() { return reductionDb; }

    public void process(byte[] pcm, short[] output) {
        if (pcm.length != output.length * 2 || output.length % 2 != 0) throw new IllegalArgumentException("Invalid stereo PCM buffer");
        boolean filterEnabled = bassCut;
        if (filterEnabled != previousBassCut) { leftFilter.reset(); rightFilter.reset(); previousBassCut = filterEnabled; }
        float requestedGain = targetGain;
        float minimumGain = 1;
        int capacity = maxima.length;
        for (int i = 0; i < output.length; i += 2) {
            float left = decode(pcm, i * 2), right = decode(pcm, (i + 1) * 2);
            if (filterEnabled) { left = leftFilter.process(left); right = rightFilter.process(right); }
            currentGain += (requestedGain - currentGain) * 0.00104f; // ~20 ms control smoothing.
            left *= currentGain; right *= currentGain;
            int ring = (int) (frameIndex % leftDelay.length);
            leftDelay[ring] = left; rightDelay[ring] = right;
            float peak = Math.max(Math.abs(left), Math.abs(right));
            while (head != tail && positions[head] < frameIndex - LOOKAHEAD) head = (head + 1) % capacity;
            while (head != tail) {
                int previous = (tail + capacity - 1) % capacity;
                if (maxima[previous] > peak) break;
                tail = previous;
            }
            maxima[tail] = peak; positions[tail] = frameIndex; tail = (tail + 1) % capacity;
            float needed = maxima[head] > CEILING ? CEILING / maxima[head] : 1;
            limiterGain = needed < limiterGain ? needed : Math.min(needed, limiterGain + 0.000139f); // ~150 ms release.
            minimumGain = Math.min(minimumGain, limiterGain);
            int delayed = (int) ((frameIndex + 1) % leftDelay.length);
            output[i] = frameIndex < LOOKAHEAD ? 0 : encode(leftDelay[delayed] * limiterGain);
            output[i + 1] = frameIndex < LOOKAHEAD ? 0 : encode(rightDelay[delayed] * limiterGain);
            frameIndex++;
        }
        reductionDb = Math.max(0, (float) (-20 * Math.log10(Math.max(0.0001f, minimumGain))));
    }

    private static float decode(byte[] pcm, int index) { return (short) ((pcm[index] & 255) | (pcm[index + 1] << 8)) / 32768f; }
    private static short encode(float value) { return (short) Math.round(Math.max(-CEILING, Math.min(CEILING, value)) * 32767); }

    /** Second-order Butterworth high-pass at 120 Hz reduces low-frequency excursion demand. */
    private static final class HighPass {
        private final double b0, b1, b2, a1, a2;
        private double x1, x2, y1, y2;
        HighPass() {
            double omega = 2 * Math.PI * 120 / AudioProtocol.SAMPLE_RATE;
            double cosine = Math.cos(omega), alpha = Math.sin(omega) / Math.sqrt(2), a0 = 1 + alpha;
            b0 = (1 + cosine) / (2 * a0); b1 = -(1 + cosine) / a0; b2 = b0;
            a1 = -2 * cosine / a0; a2 = (1 - alpha) / a0;
        }
        float process(float x) {
            double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            return (float) y;
        }
        void reset() { x1 = x2 = y1 = y2 = 0; }
    }
}
