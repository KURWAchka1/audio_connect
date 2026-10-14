package io.github.kurwachka1.audioconnect;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.pm.ServiceInfo;
import android.media.AudioAttributes;
import android.media.AudioFocusRequest;
import android.media.AudioManager;
import android.media.session.MediaSession;
import android.media.session.PlaybackState.Builder;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.os.PowerManager;
import java.io.IOException;
import java.util.concurrent.atomic.AtomicBoolean;

/** Playback begins only from the visible Activity and remains visible through a media notification. */
public final class PlaybackService extends Service {
    static final String STOP = "io.github.kurwachka1.audioconnect.STOP";
    private static final String CHANNEL = "usb_playback";
    private final AtomicBoolean stopping = new AtomicBoolean();
    private final Handler main = new Handler(Looper.getMainLooper());
    private Thread worker;
    private volatile UsbAudioPlayer player;
    private AudioManager audioManager;
    private AudioFocusRequest focusRequest;
    private PowerManager.WakeLock wakeLock;
    private MediaSession mediaSession;
    private boolean noisyRegistered;
    private final AudioManager.OnAudioFocusChangeListener focusListener = change -> {
        if (change < 0) stopPlayback("Воспроизведение остановлено другим приложением");
    };
    private final BroadcastReceiver noisyReceiver = new BroadcastReceiver() {
        @Override public void onReceive(Context context, Intent intent) { stopPlayback("Наушники отключены. Нажмите «Подключиться», когда будете готовы."); }
    };

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent != null && STOP.equals(intent.getAction())) { stopPlayback("Передача остановлена"); return START_NOT_STICKY; }
        if (worker != null) return START_NOT_STICKY;
        NotificationManager notifications = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
        if (Build.VERSION.SDK_INT >= 26) notifications.createNotificationChannel(new NotificationChannel(CHANNEL, "Передача звука", NotificationManager.IMPORTANCE_LOW));
        mediaSession = new MediaSession(this, "AudioConnect");
        mediaSession.setCallback(new MediaSession.Callback() { @Override public void onStop() { stopPlayback("Передача остановлена"); } @Override public void onPause() { stopPlayback("Передача остановлена"); } });
        mediaSession.setPlaybackState(new Builder().setState(android.media.session.PlaybackState.STATE_PLAYING, 0, 1).setActions(android.media.session.PlaybackState.ACTION_STOP | android.media.session.PlaybackState.ACTION_PAUSE).build());
        mediaSession.setActive(true);
        Notification notification = createNotification();
        if (Build.VERSION.SDK_INT >= 29) startForeground(1, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK);
        else startForeground(1, notification);

        audioManager = (AudioManager) getSystemService(AUDIO_SERVICE);
        int focus;
        if (Build.VERSION.SDK_INT >= 26) {
            focusRequest = new AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN)
                    .setAudioAttributes(new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build())
                    .setOnAudioFocusChangeListener(focusListener, main).build();
            focus = audioManager.requestAudioFocus(focusRequest);
        } else focus = requestLegacyFocus();
        if (focus != AudioManager.AUDIOFOCUS_REQUEST_GRANTED) { stopPlayback("Другой аудиоплеер не разрешил воспроизведение"); return START_NOT_STICKY; }
        IntentFilter noisy = new IntentFilter(AudioManager.ACTION_AUDIO_BECOMING_NOISY);
        if (Build.VERSION.SDK_INT >= 33) registerReceiver(noisyReceiver, noisy, Context.RECEIVER_NOT_EXPORTED);
        else registerReceiver(noisyReceiver, noisy);
        noisyRegistered = true;
        PowerManager power = (PowerManager) getSystemService(POWER_SERVICE);
        wakeLock = power.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "AudioConnect:Playback");
        wakeLock.acquire(6 * 60 * 60 * 1000L);
        PlaybackState.running = true;
        PlaybackState.packets = PlaybackState.dropped = 0;
        PlaybackState.underruns = 0;
        String token = getSharedPreferences("connection", MODE_PRIVATE).getString("pairing", "");
        int bufferMs = PlaybackState.bufferMs;
        worker = new Thread(() -> runConnection(token, bufferMs), "usb-connection");
        worker.start();
        return START_NOT_STICKY;
    }

    @SuppressWarnings("deprecation") private int requestLegacyFocus() { return audioManager.requestAudioFocus(focusListener, AudioManager.STREAM_MUSIC, AudioManager.AUDIOFOCUS_GAIN); }

    private void runConnection(String token, int bufferMs) {
        int attempts = 0;
        String failure = "Проверьте кабель и запустите передачу в Windows";
        try {
            while (!stopping.get() && attempts < 20) {
                PlaybackState.message = attempts == 0 ? "Подключаюсь к компьютеру…" : "Восстанавливаю USB-соединение…";
                UsbAudioPlayer next = new UsbAudioPlayer();
                player = next;
                if (stopping.get()) { next.stop(); break; }
                long started = PlaybackState.packets;
                try { next.stream(token, bufferMs); }
                catch (IOException error) { failure = error.getMessage(); }
                finally { next.stop(); player = null; }
                if (PlaybackState.packets - started > 100) attempts = 0;
                attempts++;
                if (!stopping.get()) Thread.sleep(1500);
            }
        } catch (InterruptedException interrupted) { Thread.currentThread().interrupt(); }
        finally {
            String reason = failure;
            main.post(() -> { if (!stopping.get()) stopPlayback(reason + ". Подключитесь снова из Windows."); });
        }
    }

    private Notification createNotification() {
        PendingIntent open = PendingIntent.getActivity(this, 0, new Intent(this, MainActivity.class), PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        PendingIntent stop = PendingIntent.getService(this, 1, new Intent(this, PlaybackService.class).setAction(STOP), PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Notification.Builder builder = Build.VERSION.SDK_INT >= 26 ? new Notification.Builder(this, CHANNEL) : new Notification.Builder(this);
        return builder.setSmallIcon(R.drawable.ic_notification).setContentTitle("Audio Connect")
                .setContentText("Звук компьютера по USB").setContentIntent(open).setOngoing(true)
                .addAction(new Notification.Action.Builder(R.drawable.ic_notification, "Остановить", stop).build())
                .setStyle(new Notification.MediaStyle().setMediaSession(mediaSession.getSessionToken()).setShowActionsInCompactView(0)).build();
    }

    private void stopPlayback(String reason) {
        if (stopping.getAndSet(true)) return;
        PlaybackState.running = PlaybackState.connected = false;
        PlaybackState.gainDb = 0;
        PlaybackState.reductionDb = 0;
        PlaybackState.message = reason;
        UsbAudioPlayer current = player;
        if (current != null) current.stop();
        if (worker != null) worker.interrupt();
        stopForeground(STOP_FOREGROUND_REMOVE);
        stopSelf();
    }

    @Override public void onDestroy() {
        stopPlayback("Передача остановлена");
        if (worker != null) {
            try { worker.join(300); }
            catch (InterruptedException interrupted) { Thread.currentThread().interrupt(); }
        }
        if (noisyRegistered) unregisterReceiver(noisyReceiver);
        if (audioManager != null) {
            if (Build.VERSION.SDK_INT >= 26 && focusRequest != null) audioManager.abandonAudioFocusRequest(focusRequest);
            else abandonLegacyFocus();
        }
        if (wakeLock != null && wakeLock.isHeld()) wakeLock.release();
        if (mediaSession != null) mediaSession.release();
        super.onDestroy();
    }

    @SuppressWarnings("deprecation") private void abandonLegacyFocus() { audioManager.abandonAudioFocus(focusListener); }
    @Override public IBinder onBind(Intent intent) { return null; }
}
