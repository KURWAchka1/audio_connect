using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AudioConnect.Core;
using NAudio.CoreAudioApi;

namespace AudioConnect.Windows;

public partial class MainWindow : Window
{
    private readonly AdbBridge adb = new();
    private readonly DispatcherTimer meterTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private StreamServer? server;
    private CapturePipeline? capture;
    private LocalOutputMuteLease? outputMute;
    private CancellationTokenSource? session;
    private Task? reconnectTask;
    private string? serial;
    private bool busy;
    private bool closing;
    private bool closeReady;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
        Closing += OnClosing;
        meterTimer.Tick += (_, _) => LevelMeter.Value = capture?.Peak ?? 0;
        meterTimer.Start();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async Task RefreshAsync()
    {
        if (busy || session is not null) return;
        SetBusy(true);
        try
        {
            IReadOnlyList<AndroidDevice> devices = await adb.FindDevicesAsync(CancellationToken.None);
            DevicePicker.ItemsSource = devices;
            DevicePicker.SelectedItem = devices.FirstOrDefault(d => d.State == "device") ?? devices.FirstOrDefault();
            if (devices.Count == 0) SetStatus("Телефон не найден", "Подключите USB-кабель, включите USB-отладку и нажмите «Обновить».");
            else if (devices.All(d => d.State != "device")) SetStatus("Нужно разрешение телефона", "Разблокируйте телефон и разрешите USB-отладку в появившемся окне.");
            else SetStatus("Телефон готов", "Запустите музыку или видео на компьютере и начните передачу.");
        }
        catch (Exception error) { ShowError(error); }
        finally { SetBusy(false); }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session is not null) return;
        if (DevicePicker.SelectedItem is not AndroidDevice phone || phone.State != "device")
        { SetStatus("Разрешите USB-отладку", "Разблокируйте подключённый телефон, подтвердите доступ и нажмите «Обновить»."); return; }
        if (!ProcessLoopbackCapture.IsSupported)
        { SetStatus("Требуется Windows 11", "Режим «Только телефон» использует системный захват, доступный в Windows 11 и Windows Server 2022+."); return; }
        SetBusy(true);
        byte[] token = RandomNumberGenerator.GetBytes(AudioProtocol.TokenBytes);
        try
        {
            session = new CancellationTokenSource();
            serial = phone.Serial;
            server = new StreamServer(token);
            server.ConnectionChanged += connected => Dispatcher.BeginInvoke(() =>
            {
                if (session is null || closing) return;
                SetStatus(connected ? "Звук только на телефоне" : "Ожидаю телефон", connected ? "Звук Windows отключён на время сеанса. Громкость регулируется на телефоне." : "Звук ПК отключён. Подключитесь на телефоне или нажмите «Остановить», чтобы вернуть звук ПК.", connected);
            });
            server.Faulted += ScheduleFault;
            server.Start();
            outputMute = new LocalOutputMuteLease();
            outputMute.Faulted += ScheduleFault;
            capture = new CapturePipeline(server.Enqueue);
            capture.Faulted += ScheduleFault;
            capture.Start();
            SetStatus("Ожидаю телефон", "Открываю Audio Connect на подключённом телефоне…");
            await adb.ConnectAsync(phone.Serial, Convert.ToHexString(token).ToLowerInvariant(), session.Token);
            if (session is null || closing) return;
            reconnectTask = MaintainTunnelAsync(phone.Serial, session.Token);
        }
        catch (Exception error) { await StopAsync(); if (!closing) ShowError(error); }
        finally { CryptographicOperations.ZeroMemory(token); SetBusy(false); }
    }

    private void ScheduleFault(Exception error) => Dispatcher.BeginInvoke(async () =>
    {
        await StopAsync();
        ShowError(new InvalidOperationException("Передача прервалась. Звук Windows восстановлен; подключитесь снова.", error));
    });

    private async Task MaintainTunnelAsync(string deviceSerial, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try { await adb.EnsureTunnelAsync(deviceSerial, cancellationToken); }
                catch (Exception error) when (error is InvalidOperationException or OperationCanceledException or IOException) { }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        SetBusy(true);
        try { await StopAsync(); SetStatus("Передача остановлена", "Прежнее состояние звука Windows восстановлено. Можно подключить телефон снова."); }
        finally { SetBusy(false); }
    }

    private async Task StopAsync()
    {
        CancellationTokenSource? previous = session;
        session = null;
        Task? previousReconnect = reconnectTask; reconnectTask = null;
        CapturePipeline? previousCapture = capture; capture = null;
        StreamServer? previousServer = server; server = null;
        LocalOutputMuteLease? previousMute = outputMute; outputMute = null;
        string? previousSerial = serial; serial = null;
        if (previous is not null) await previous.CancelAsync();
        try
        {
            if (previousReconnect is not null) await previousReconnect;
            if (previousCapture is not null) await previousCapture.DisposeAsync();
        }
        finally
        {
            try { if (previousServer is not null) await previousServer.DisposeAsync(); }
            finally { previousMute?.Dispose(); }
            if (previousSerial is not null) await adb.RemoveTunnelAsync(previousSerial);
            previous?.Dispose();
            LevelMeter.Value = 0;
        }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        StartButton.IsEnabled = !value && session is null;
        StopButton.IsEnabled = !value && session is not null;
        RefreshButton.IsEnabled = !value && session is null;
        DevicePicker.IsEnabled = !value && session is null;
    }

    private void SetStatus(string title, string detail, bool connected = false)
    {
        StatusTitle.Text = title;
        StatusDetail.Text = detail;
        StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(connected ? "#27A27E" : "#8794A5"));
    }

    private void ShowError(Exception error)
    {
        AppLog.Write(error);
        string detail = error is OperationCanceledException ? "USB-команда не ответила. Проверьте кабель и разрешение отладки на телефоне." : error.Message;
        SetStatus("Не удалось подключиться", detail);
    }

    private void Help_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://github.com/KURWAchka1/audio_connect#подключение") { UseShellExecute = true });

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closeReady) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        meterTimer.Stop();
        SetBusy(true);
        try { await StopAsync(); }
        finally { closeReady = true; _ = Dispatcher.BeginInvoke(Close); }
    }
}
