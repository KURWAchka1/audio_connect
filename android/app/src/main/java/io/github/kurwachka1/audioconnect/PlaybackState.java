package io.github.kurwachka1.audioconnect;

/** In-process observable snapshot, deliberately holding no Activity or Context. */
public final class PlaybackState {
    public static volatile boolean running, connected;
    public static volatile String message = "Подключите телефон к компьютеру";
    public static volatile int gainDb, bufferMs = 40;
    public static volatile boolean bassCut = true;
    public static volatile float reductionDb;
    public static volatile long packets, dropped;
    public static volatile int underruns;
    private PlaybackState() { }
}
