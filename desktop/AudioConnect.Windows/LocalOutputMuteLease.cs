using System.Diagnostics;
using System.Text.Json;
using NAudio.CoreAudioApi;

namespace AudioConnect.Windows;

/// <summary>Owns only endpoint mute flags, saving them before mutation and restoring them on release.</summary>
public sealed class LocalOutputMuteLease : IDisposable
{
    private readonly object gate = new();
    private readonly MMDeviceEnumerator devices = new();
    private readonly Dictionary<string, (MMDevice Device, bool WasMuted)> originals = new();
    private readonly EventWaitHandle released;
    private readonly Process watchdog;
    private readonly Timer monitor;
    private readonly string journal;
    private bool disposed;
    private int refreshQueued;
    public event Action<Exception>? Faulted;
    internal static string JournalDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioConnect", "volume-recovery");

    public LocalOutputMuteLease()
    {
        string identity = Guid.NewGuid().ToString("N");
        journal = Path.Combine(JournalDirectory, identity + ".json");
        string eventName = "Local\\AudioConnect.VolumeReleased." + identity;
        released = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        // Start a separate restorer BEFORE touching Windows audio. The OS process handle
        // remains signalled even if a new process later reuses the parent's numeric PID.
        try { watchdog = StartWatchdog(eventName, journal); }
        catch { released.Dispose(); devices.Dispose(); throw; }
        try { Reconcile(); }
        catch
        {
            RestoreJournal(journal);
            foreach (var entry in originals.Values) entry.Device.Dispose();
            released.Set(); watchdog.Dispose(); released.Dispose(); devices.Dispose();
            throw;
        }
        monitor = new Timer(_ => QueueRefresh(), null, 250, 250);
    }

    private static Process StartWatchdog(string eventName, string journal)
    {
        string host = Environment.ProcessPath ?? throw new InvalidOperationException("Не найден путь приложения.");
        string runtimeHost = Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
        if (File.Exists(runtimeHost)) host = runtimeHost;
        bool dotnetHost = Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        if (!dotnetHost) host = Path.Combine(Path.GetDirectoryName(typeof(App).Assembly.Location)!, "AudioConnect.exe");
        var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        if (dotnetHost) start.ArgumentList.Add(typeof(App).Assembly.Location);
        start.ArgumentList.Add("--restore-volume");
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add(eventName);
        start.ArgumentList.Add(journal);
        return Process.Start(start) ?? throw new IOException("Не удалось запустить восстановление звука Windows.");
    }

    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref refreshQueued, 1) != 0) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                lock (gate)
                {
                    if (disposed) return;
                    if (watchdog.HasExited) throw new IOException("Служба восстановления звука завершилась. Передача остановлена.");
                    Reconcile();
                }
            }
            catch (Exception error) { Faulted?.Invoke(error); }
            finally { Volatile.Write(ref refreshQueued, 0); }
        });
    }

    private void Reconcile()
    {
        foreach (MMDevice candidate in devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            string id = candidate.ID;
            if (!originals.TryGetValue(id, out var entry))
            {
                bool wasMuted;
                try { wasMuted = candidate.AudioEndpointVolume.Mute; }
                catch { candidate.Dispose(); throw; }
                originals.Add(id, (candidate, wasMuted));
                SaveJournal(journal, originals.ToDictionary(pair => pair.Key, pair => pair.Value.WasMuted));
                candidate.AudioEndpointVolume.OnVolumeNotification += notification => { if (!notification.Muted) QueueRefresh(); };
                entry = (candidate, wasMuted);
            }
            else candidate.Dispose();
            if (!entry.Device.AudioEndpointVolume.Mute) entry.Device.AudioEndpointVolume.Mute = true;
            if (!entry.Device.AudioEndpointVolume.Mute) throw new IOException("Не удалось отключить локальный аудиовыход Windows.");
        }
    }

    internal static void SaveJournal(string path, Dictionary<string, bool> values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(file, values);
            file.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    internal static void RestoreJournal(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var pending = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(path)) ?? new();
            using var enumerator = new MMDeviceEnumerator();
            foreach ((string id, bool wasMuted) in pending.ToArray())
            {
                try
                {
                    using MMDevice device = enumerator.GetDevice(id);
                    device.AudioEndpointVolume.Mute = wasMuted;
                    pending.Remove(id);
                }
                catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ArgumentException) { AppLog.Write(error); }
            }
            if (pending.Count == 0) File.Delete(path);
            else SaveJournal(path, pending);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { AppLog.Write(error); }
    }

    internal static void RecoverPreviousSessions()
    {
        if (!Directory.Exists(JournalDirectory)) return;
        foreach (string path in Directory.EnumerateFiles(JournalDirectory, "*.json")) RestoreJournal(path);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            monitor.Dispose();
            try { RestoreJournal(journal); }
            finally
            {
                released.Set();
                foreach (var entry in originals.Values) entry.Device.Dispose();
                devices.Dispose(); released.Dispose(); watchdog.Dispose();
            }
        }
    }
}

internal static class VolumeRestoreWatchdog
{
    public static void Run(int parentId, string eventName, string journal)
    {
        try
        {
            using var released = EventWaitHandle.OpenExisting(eventName);
            using Process parent = Process.GetProcessById(parentId);
            while (!parent.WaitForExit(200)) if (released.WaitOne(0)) break;
        }
        catch (Exception error) when (error is ArgumentException or WaitHandleCannotBeOpenedException or InvalidOperationException) { }
        finally { LocalOutputMuteLease.RestoreJournal(journal); }
    }
}
