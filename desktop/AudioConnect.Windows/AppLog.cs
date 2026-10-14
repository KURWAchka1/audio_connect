namespace AudioConnect.Windows;

internal static class AppLog
{
    public static string PathName => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioConnect", "app.log");
    private static readonly object Gate = new();
    public static void Write(Exception error)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)!);
                if (File.Exists(PathName) && new FileInfo(PathName).Length > 1_000_000) File.Delete(PathName);
                File.AppendAllText(PathName, $"{DateTimeOffset.Now:u} {error}\n");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
