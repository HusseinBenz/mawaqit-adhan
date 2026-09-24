using System.Web.Script.Serialization;

namespace MawaqitAdhan.Core;

internal static class Json
{
    private static JavaScriptSerializer Create() => new() { MaxJsonLength = 4 * 1024 * 1024, RecursionLimit = 64 };
    public static T Read<T>(string text) => Create().Deserialize<T>(text);
    public static string Write(object value) => Create().Serialize(value);
    public static Dictionary<string, object> Object(string text) =>
        Create().DeserializeObject(text) as Dictionary<string, object>
        ?? throw new FormatException("Expected a JSON object.");
}

internal static class Numbers
{
    public static int Clamp(int n, int min, int max) => Math.Max(min, Math.Min(max, n));
    public static float Clamp(float n, float min, float max) => float.IsNaN(n) ? min : Math.Max(min, Math.Min(max, n));
    public static double Clamp(double n, double min, double max) => double.IsNaN(n) ? min : Math.Max(min, Math.Min(max, n));
}

internal static class AtomicFile
{
    public static void Replace(string temporary, string target)
    {
        if (File.Exists(target)) File.Replace(temporary, target, null);
        else File.Move(temporary, target);
    }
}
