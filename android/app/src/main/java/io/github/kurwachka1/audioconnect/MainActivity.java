package io.github.kurwachka1.audioconnect;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.media.AudioManager;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.Gravity;
import android.view.View;
import android.view.WindowInsets;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.SeekBar;
import android.widget.Spinner;
import android.widget.ArrayAdapter;
import android.widget.Switch;
import android.widget.TextView;
import java.util.Locale;

/** A small utility screen: connection, buffering, and explicitly enabled digital gain. */
public final class MainActivity extends Activity {
    private static final int INK = 0xff192637, MUTED = 0xff607084, BLUE = 0xff246be0;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private TextView status, detail, gainLabel, limiterLabel, stats;
    private Button connect, stop;
    private Spinner buffer;
    private SeekBar gain;
    private Switch boost;
    private boolean pendingConnect, boostEnabled, wasRunning, visible;
    private final Runnable connectionStarter = new Runnable() {
        @Override public void run() {
            if (!visible) return;
            if (PlaybackState.running) handler.postDelayed(this, 100);
            else startPlayback();
        }
    };
    private final Runnable refresh = new Runnable() {
        @Override public void run() { refreshState(); handler.postDelayed(this, 250); }
    };

    @Override public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setVolumeControlStream(AudioManager.STREAM_MUSIC);
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(0xfff4f7fa);
        LinearLayout content = column();
        content.setPadding(dp(24), dp(22), dp(24), dp(24));
        scroll.addView(content);
        scroll.setOnApplyWindowInsetsListener((view, insets) -> {
            if (Build.VERSION.SDK_INT >= 30) {
                android.graphics.Insets bars = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
                view.setPadding(bars.left, bars.top, bars.right, bars.bottom);
            } else applyLegacyInsets(view, insets);
            return insets;
        });
        setContentView(scroll);
        add(content, text("Audio Connect", 29, INK, true), 0);
        add(content, text("Звук компьютера по USB", 14, MUTED, false), 5);

        LinearLayout connection = panel();
        add(content, connection, 24);
        status = text("Подключите телефон", 21, INK, true);
        detail = text("Запустите передачу в приложении Windows.", 14, MUTED, false);
        add(connection, status, 0); add(connection, detail, 10);
        TextView format = text("48 кГц · стерео · без сжатия", 12, MUTED, false);
        add(connection, format, 15);
        LinearLayout actions = new LinearLayout(this); actions.setOrientation(LinearLayout.HORIZONTAL);
        connect = button("Подключиться", true); stop = button("Стоп", false);
        LinearLayout.LayoutParams mainButton = new LinearLayout.LayoutParams(0, dp(50), 1);
        actions.addView(connect, mainButton);
        LinearLayout.LayoutParams stopButton = new LinearLayout.LayoutParams(dp(88), dp(50)); stopButton.leftMargin = dp(10);
        actions.addView(stop, stopButton); add(connection, actions, 18);
        connect.setOnClickListener(view -> startPlayback());
        stop.setOnClickListener(view -> startService(new Intent(this, PlaybackService.class).setAction(PlaybackService.STOP)));

        add(content, text("Буфер воспроизведения", 15, INK, true), 25);
        buffer = new Spinner(this);
        ArrayAdapter<String> buffers = new ArrayAdapter<>(this, android.R.layout.simple_spinner_dropdown_item,
                new String[]{"20 мс · минимальный", "40 мс · сбалансированный", "80 мс · устойчивый"});
        buffer.setAdapter(buffers); buffer.setSelection(1);
        add(content, buffer, 7);
        add(content, text("Если звук прерывается, выберите больший буфер перед подключением.", 12, MUTED, false), 5);

        LinearLayout amplifier = panel(); add(content, amplifier, 23);
        add(amplifier, text("Усиление", 18, INK, true), 0);
        boost = new Switch(this); boost.setText("Разрешить усиление"); boost.setTextSize(14); boost.setTextColor(INK);
        add(amplifier, boost, 15);
        gainLabel = text("0 дБ · исходный уровень", 16, INK, true); add(amplifier, gainLabel, 18);
        gain = new SeekBar(this); gain.setMax(12); gain.setEnabled(false); gain.setContentDescription("Усиление от 0 до 12 децибел");
        add(amplifier, gain, 10);
        gain.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
            @Override public void onProgressChanged(SeekBar seekBar, int value, boolean user) { if (user) PlaybackState.gainDb = boostEnabled ? value : 0; updateGainLabel(); }
            @Override public void onStartTrackingTouch(SeekBar seekBar) { }
            @Override public void onStopTrackingTouch(SeekBar seekBar) { }
        });
        boost.setOnCheckedChangeListener((control, enabled) -> {
            if (!enabled) { boostEnabled = false; PlaybackState.gainDb = 0; gain.setProgress(0); gain.setEnabled(false); return; }
            if (boostEnabled) return;
            boost.setChecked(false);
            new AlertDialog.Builder(this).setTitle("Повышенная нагрузка на динамик")
                    .setMessage("Усиление повышает среднюю громкость и нагрузку на динамик. Возможны нагрев, хрип и повреждение. Лимитер ограничивает цифровые пики, но не измеряет температуру и ход мембраны.\n\nНачните с +1–3 дБ. При хрипе или нагреве сразу снизьте усиление. Аппаратный максимум и защиты телефона остаются в силе. Также избегайте опасной для слуха громкости.")
                    .setPositiveButton("Включить", (dialog, which) -> { boostEnabled = true; boost.setChecked(true); gain.setEnabled(true); })
                    .setNegativeButton("Отмена", null).show();
        });
        Switch bass = new Switch(this); bass.setText("Срез баса ниже 120 Гц"); bass.setTextSize(14); bass.setTextColor(INK);
        bass.setChecked(PlaybackState.bassCut); bass.setOnCheckedChangeListener((view, enabled) -> PlaybackState.bassCut = enabled);
        add(amplifier, bass, 15);
        add(amplifier, text("Снижает нагрузку от низких частот. Физическую защиту динамика не гарантирует.", 12, MUTED, false), 7);
        limiterLabel = text("Лимитер: −1 дБFS", 12, MUTED, false); add(amplifier, limiterLabel, 15);
        stats = text("Буфер — это настройка, а не измеренная задержка.", 12, MUTED, false); add(content, stats, 19);
        Button help = button("Как подключить", false); add(content, help, 15); help.setOnClickListener(view -> showHelp());
        add(content, text("v" + BuildConfig.VERSION_NAME + " · Android 7+ · локальное соединение", 11, MUTED, false), 17);
        handleIntent(getIntent());
    }

    @SuppressWarnings("deprecation") private void applyLegacyInsets(View view, WindowInsets insets) {
        view.setPadding(insets.getSystemWindowInsetLeft(), insets.getSystemWindowInsetTop(), insets.getSystemWindowInsetRight(), insets.getSystemWindowInsetBottom());
    }

    @Override protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent); setIntent(intent); handleIntent(intent);
        if (visible) consumePendingConnection();
    }
    private void handleIntent(Intent intent) {
        String token = intent.getStringExtra("pairing");
        if (token != null && token.matches("[a-fA-F0-9]{64}")) {
            getSharedPreferences("connection", MODE_PRIVATE).edit().putString("pairing", token).apply();
            pendingConnect = intent.getBooleanExtra("connect", false);
        }
    }
    @Override protected void onResume() {
        super.onResume(); visible = true; handler.post(refresh); consumePendingConnection();
    }
    @Override protected void onPause() {
        visible = false; handler.removeCallbacks(refresh); handler.removeCallbacks(connectionStarter); super.onPause();
    }

    private void consumePendingConnection() {
        if (!pendingConnect) return;
        pendingConnect = false;
        if (PlaybackState.running) stopService(new Intent(this, PlaybackService.class));
        handler.removeCallbacks(connectionStarter);
        handler.postDelayed(connectionStarter, 100);
    }

    private void startPlayback() {
        if (PlaybackState.running) return;
        if (getSharedPreferences("connection", MODE_PRIVATE).getString("pairing", "").isEmpty()) { showHelp(); return; }
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 1);
        PlaybackState.bufferMs = new int[]{20, 40, 80}[buffer.getSelectedItemPosition()];
        Intent service = new Intent(this, PlaybackService.class);
        if (Build.VERSION.SDK_INT >= 26) startForegroundService(service); else startService(service);
    }

    private void refreshState() {
        boolean running = PlaybackState.running;
        status.setText(PlaybackState.connected ? "Подключено по USB" : running ? "Подключаюсь…" : "Передача остановлена");
        status.setTextColor(PlaybackState.connected ? 0xff16876a : INK);
        detail.setText(PlaybackState.message);
        connect.setEnabled(!running); connect.setAlpha(running ? 0.5f : 1);
        stop.setEnabled(running); stop.setAlpha(running ? 1 : 0.5f);
        buffer.setEnabled(!running);
        if (wasRunning && !running) { boostEnabled = false; boost.setChecked(false); gain.setProgress(0); gain.setEnabled(false); }
        wasRunning = running;
        if (PlaybackState.gainDb > 0 && !boostEnabled) { boostEnabled = true; boost.setChecked(true); gain.setEnabled(true); }
        gain.setProgress(PlaybackState.gainDb); updateGainLabel();
        limiterLabel.setText(String.format(Locale.getDefault(), "Лимитер: −1 дБFS · снижение %.1f дБ", PlaybackState.reductionDb));
        stats.setText(running ? String.format(Locale.getDefault(), "Буфер %d мс · пропуски %d · недозаполнения %d", PlaybackState.bufferMs, PlaybackState.dropped, PlaybackState.underruns)
                : "Буфер — это настройка, а не измеренная задержка.");
    }

    private void updateGainLabel() { gainLabel.setText(PlaybackState.gainDb == 0 ? "0 дБ · исходный уровень" : "+" + PlaybackState.gainDb + " дБ · цифровое усиление"); }
    private void showHelp() {
        new AlertDialog.Builder(this).setTitle("Подключение по USB").setMessage("1. Установите Audio Connect на Windows.\n2. На телефоне: Настройки → О телефоне → Сведения о ПО → нажмите «Номер сборки» 7 раз.\n3. Для разработчиков → включите «Отладка по USB».\n4. Соедините телефон и ПК кабелем с передачей данных. Разрешите USB-отладку на телефоне.\n5. В Windows выберите телефон и нажмите «Передавать звук».\n\nИспользуйте кнопки громкости телефона для обычной громкости. Для усиления тихого звука отдельно включите «Разрешить усиление».\n\nПри подключении доверяйте только своему компьютеру. После использования можно выключить USB-отладку.")
                .setPositiveButton("Понятно", null).show();
    }

    private LinearLayout column() { LinearLayout layout = new LinearLayout(this); layout.setOrientation(LinearLayout.VERTICAL); return layout; }
    private LinearLayout panel() { LinearLayout layout = column(); layout.setPadding(dp(20), dp(20), dp(20), dp(20)); layout.setBackground(background(Color.WHITE, 0xffdce4ed, 10)); return layout; }
    private TextView text(String value, int size, int color, boolean bold) { TextView view = new TextView(this); view.setText(value); view.setTextSize(size); view.setTextColor(color); view.setLineSpacing(dp(2), 1); if (bold) view.setTypeface(null, Typeface.BOLD); return view; }
    private Button button(String value, boolean primary) { Button view = new Button(this); view.setText(value); view.setAllCaps(false); view.setTextSize(14); view.setTextColor(primary ? Color.WHITE : INK); view.setBackground(background(primary ? BLUE : 0xffe8eef4, Color.TRANSPARENT, 6)); view.setGravity(Gravity.CENTER); return view; }
    private GradientDrawable background(int color, int stroke, int radius) { GradientDrawable shape = new GradientDrawable(); shape.setColor(color); shape.setCornerRadius(dp(radius)); shape.setStroke(dp(1), stroke); return shape; }
    private void add(LinearLayout parent, View child, int top) { LinearLayout.LayoutParams layout = new LinearLayout.LayoutParams(-1, -2); layout.topMargin = dp(top); parent.addView(child, layout); }
    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }
}
