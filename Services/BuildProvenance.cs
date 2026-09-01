using System.IO;
using System.Reflection;
using System.Text;

namespace CloudLightBlizzard.Services;

/// <summary>Build identity shared by startup diagnostics and release verification.</summary>
internal static class BuildProvenance
{
    private const string StartupLogName = "application.log";

    private static Assembly Assembly => typeof(BuildProvenance).Assembly;

    public static string ApplicationVersion =>
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public static string AssemblyVersion => Assembly.GetName().Version?.ToString() ?? "unknown";

    public static string BuildCommit => Metadata("BuildCommit");

    public static string BuildTimestamp => Metadata("BuildTimestamp");

    public static string BilibiliUiSchema => Metadata("BilibiliUiSchema");

    public static string UiSchemaMarker => $"bilibili-ui-v{BilibiliUiSchema}";

    public static void WriteStartupLog(string logsDirectory)
    {
        try
        {
            Directory.CreateDirectory(logsDirectory);
            var processPath = Environment.ProcessPath ?? "unknown";
            var assemblyPath = Assembly.Location;
            var text = new StringBuilder()
                .Append('[').Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")).AppendLine("] startup")
                .Append("Application version: ").AppendLine(ApplicationVersion)
                .Append("Assembly version: ").AppendLine(AssemblyVersion)
                .Append("Build commit: ").AppendLine(BuildCommit)
                .Append("Build timestamp: ").AppendLine(BuildTimestamp)
                .Append("UI schema: ").AppendLine(UiSchemaMarker)
                .Append("Process image: ").AppendLine(processPath)
                .Append("Assembly path: ").AppendLine(assemblyPath)
                .ToString();
            File.AppendAllText(Path.Combine(logsDirectory, StartupLogName), text, Encoding.UTF8);
        }
        catch
        {
            // Startup diagnostics must never prevent the application from opening.
        }
    }

    private static string Metadata(string name) =>
        Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, name, StringComparison.Ordinal))?.Value
        ?? "unknown";
}
