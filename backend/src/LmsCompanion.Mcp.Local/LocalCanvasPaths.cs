namespace LmsCompanion.Mcp.Local;

public static class LocalCanvasPaths
{
    // UserProfile is stable across ordinary terminals and packaged desktop processes.
    public static string Directory => Environment.GetEnvironmentVariable("LMS_COMPANION_HOME") is { Length: > 0 } path
        && Path.IsPathFullyQualified(path)
            ? path
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".lms-companion");

    public static string KeyDirectory => Path.Combine(Directory, "keys");
}
