using System.Diagnostics;
using AudioConnect.Core;

namespace AudioConnect.Windows;

public sealed record AndroidDevice(string Serial, string Label, string State)
{
    public override string ToString() => Label + (State == "device" ? "" : $" • {State}");
}

/// <summary>Uses only explicit serials; never enables TCP debugging or kills a shared ADB server.</summary>
public sealed class AdbBridge
{
    private readonly string executable;
    public AdbBridge(string? path = null) => executable = path ?? Path.Combine(AppContext.BaseDirectory, "platform-tools", "adb.exe");

    public async Task<IReadOnlyList<AndroidDevice>> FindDevicesAsync(CancellationToken cancellationToken, bool includeEmulators = false)
    {
        string output = await RunAsync(["devices", "-l"], cancellationToken);
        var devices = new List<AndroidDevice>();
        foreach (string line in output.Split('\n'))
        {
            string[] parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] == "List" || parts[0].Contains(':')) continue;
            if (!includeEmulators && parts[0].StartsWith("emulator-", StringComparison.Ordinal)) continue;
            if (parts[1] is not ("device" or "unauthorized" or "offline")) continue;
            string? model = parts.FirstOrDefault(p => p.StartsWith("model:", StringComparison.Ordinal));
            devices.Add(new AndroidDevice(parts[0], model?[6..].Replace('_', ' ') ?? parts[0], parts[1]));
        }
        return devices;
    }

    public async Task ConnectAsync(string serial, string token, CancellationToken cancellationToken)
    {
        await EnsureTunnelAsync(serial, cancellationToken);
        string result = await RunAsync(["-s", serial, "shell", "am", "start", "-n", "io.github.kurwachka1.audioconnect/.MainActivity", "--es", "pairing", token, "--ez", "connect", "true"], cancellationToken);
        if (result.Contains("Error", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Установите Audio Connect APK на телефон и повторите подключение.");
    }

    public Task<string> EnsureTunnelAsync(string serial, CancellationToken cancellationToken) =>
        RunAsync(["-s", serial, "reverse", $"tcp:{AudioProtocol.Port}", $"tcp:{AudioProtocol.Port}"], cancellationToken);

    public async Task RemoveTunnelAsync(string serial)
    {
        try { await RunAsync(["-s", serial, "reverse", "--remove", $"tcp:{AudioProtocol.Port}"], CancellationToken.None); }
        catch (Exception error) when (error is IOException or InvalidOperationException or OperationCanceledException) { AppLog.Write(error); }
    }

    private async Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("ADB не найден. Переустановите Audio Connect.");
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new IOException("Не удалось запустить ADB.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        string text = await output;
        string errorText = await errors;
        if (process.ExitCode != 0) throw new InvalidOperationException($"USB-подключение недоступно: {errorText.Trim()}");
        return text;
    }
}
