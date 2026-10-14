using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AudioConnect.Windows;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 4 && e.Args[0] == "--restore-volume" && int.TryParse(e.Args[1], out int parentId))
        {
            VolumeRestoreWatchdog.Run(parentId, e.Args[2], e.Args[3]);
            Shutdown(); return;
        }
        instance = new Mutex(true, "Local\\AudioConnect.Desktop", out bool firstInstance);
        if (!firstInstance) { MessageBox.Show("Audio Connect уже запущен.", "Audio Connect"); Shutdown(); return; }
        LocalOutputMuteLease.RecoverPreviousSessions();
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Write(args.Exception);
            MessageBox.Show("Произошла ошибка. Перезапустите передачу. Подробности сохранены в журнале.", "Audio Connect");
            args.Handled = true;
        };
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (e.Args.Length == 2 && e.Args[0] == "--render")
        {
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                window.UpdateLayout();
                var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using (FileStream file = File.Create(e.Args[1])) encoder.Save(file);
                window.Close();
            });
        }
    }

    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
