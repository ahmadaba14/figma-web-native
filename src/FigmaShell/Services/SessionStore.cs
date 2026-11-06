using System.IO;
using System.Text.Json;
using FigmaShell.Web;

namespace FigmaShell.Services;

public sealed class SessionData
{
    public List<string> Tabs { get; set; } = [];
    public int ActiveIndex { get; set; }
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

public static class SessionStore
{
    private static readonly string FilePath = Path.Combine(WebViewFactory.DataRoot, "session.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static SessionData? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return JsonSerializer.Deserialize<SessionData>(File.ReadAllText(FilePath), Options);
        }
        catch
        {
            return null; // corrupt file: start fresh
        }
    }

    public static void Save(SessionData data)
    {
        try
        {
            Directory.CreateDirectory(WebViewFactory.DataRoot);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(data, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch
        {
            // Best effort — never block shutdown on this.
        }
    }
}
