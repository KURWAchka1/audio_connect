using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace AudioConnect.Windows;

/// <summary>Captures the system mix before endpoint mute, excluding this application's process tree.</summary>
public sealed class ProcessLoopbackCapture : IWaveIn
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? thread;
    public WaveFormat WaveFormat { get; set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348);

    public void StartRecording()
    {
        if (!IsSupported) throw new PlatformNotSupportedException("Режим «Только телефон» требует Windows 11 или Windows Server 2022 и новее.");
        if (thread is not null) throw new InvalidOperationException("Capture already started.");
        thread = new Thread(Capture) { IsBackground = true, Name = "System audio capture" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        ready.Task.GetAwaiter().GetResult();
    }

    public void StopRecording()
    {
        lifetime.Cancel();
        if (thread is not null && Thread.CurrentThread != thread && !thread.Join(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Захват системного звука не остановился вовремя.");
    }

    private void Capture()
    {
        Exception? fault = null;
        try
        {
            using AudioClient client = Activate();
            client.Initialize(AudioClientShareMode.Shared,
                AudioClientStreamFlags.Loopback | AudioClientStreamFlags.AutoConvertPcm,
                200000, 0, WaveFormat, Guid.Empty);
            AudioCaptureClient input = client.AudioCaptureClient;
            byte[] bytes = new byte[Math.Max(1, client.BufferSize) * WaveFormat.BlockAlign];
            client.Start();
            ready.TrySetResult();
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    while (!lifetime.IsCancellationRequested && input.GetNextPacketSize() > 0)
                    {
                        IntPtr data = input.GetBuffer(out int frames, out AudioClientBufferFlags flags);
                        try
                        {
                            int length = checked(frames * WaveFormat.BlockAlign);
                            if (bytes.Length < length) bytes = new byte[length];
                            if ((flags & AudioClientBufferFlags.Silent) != 0) Array.Clear(bytes, 0, length);
                            else Marshal.Copy(data, bytes, 0, length);
                            DataAvailable?.Invoke(this, new WaveInEventArgs(bytes, length));
                        }
                        finally { input.ReleaseBuffer(frames); }
                    }
                    lifetime.Token.WaitHandle.WaitOne(5);
                }
            }
            finally { client.Stop(); }
        }
        catch (Exception error) { fault = error; ready.TrySetException(error); }
        finally { RecordingStopped?.Invoke(this, new StoppedEventArgs(fault)); }
    }

    private static AudioClient Activate()
    {
        var parameters = new ActivationParameters { Type = 1, ProcessId = (uint)Environment.ProcessId, Mode = 1 };
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<ActivationParameters>());
        IntPtr variantMemory = Marshal.AllocHGlobal(Marshal.SizeOf<BlobVariant>());
        var callback = new CompletionHandler();
        IActivateOperation? operation = null;
        try
        {
            Marshal.StructureToPtr(parameters, memory, false);
            Marshal.StructureToPtr(new BlobVariant { Type = 65, Size = (uint)Marshal.SizeOf<ActivationParameters>(), Data = memory }, variantMemory, false);
            Guid interfaceId = typeof(IAudioClient).GUID;
            Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref interfaceId, variantMemory, callback, out operation));
            // Activation owns its argument copy after the API returns. Keep the callback and
            // operation alive until completion; the worker is an MTA as required by Core Audio.
            IAudioClient native = callback.Result.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            return new AudioClient(native);
        }
        finally
        {
            GC.KeepAlive(callback);
            if (operation is not null) Marshal.ReleaseComObject(operation);
            Marshal.FreeHGlobal(variantMemory);
            Marshal.FreeHGlobal(memory);
        }
    }

    public void Dispose() { StopRecording(); lifetime.Dispose(); }

    [StructLayout(LayoutKind.Sequential)] private struct ActivationParameters { public int Type; public uint ProcessId; public int Mode; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct BlobVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public uint Size;
        [FieldOffset(16)] public IntPtr Data;
    }
    [DllImport("Mmdevapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int ActivateAudioInterfaceAsync(string devicePath, ref Guid interfaceId, IntPtr parameters,
        ICompletionHandler completion, out IActivateOperation operation);

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivateOperation
    {
        [PreserveSig] int GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object audioInterface);
    }
    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICompletionHandler { [PreserveSig] int ActivateCompleted(IActivateOperation operation); }
    [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAgileObject { }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class CompletionHandler : ICompletionHandler, IAgileObject
    {
        public TaskCompletionSource<IAudioClient> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ActivateCompleted(IActivateOperation operation)
        {
            try
            {
                Marshal.ThrowExceptionForHR(operation.GetActivateResult(out int result, out object instance));
                Marshal.ThrowExceptionForHR(result);
                Result.TrySetResult((IAudioClient)instance);
            }
            catch (Exception error) { Result.TrySetException(error); }
            return 0;
        }
    }
}
