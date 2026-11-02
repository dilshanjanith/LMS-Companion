using System.Text.Json;
using LmsCompanion.Domain;

namespace LmsCompanion.Mcp.Local;

public static class LocalCanvasSnapshotStore
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private static string SnapshotPath => Path.Combine(LocalCanvasPaths.Directory, "canvas-courses-snapshot.json");
    private static string UpcomingPath => Path.Combine(LocalCanvasPaths.Directory, "canvas-upcoming-snapshot.json");

    public static void ImportCourses(string canvasUrl, string inputPath)
    {
        if (!LocalCanvasConnectionStore.TryCanvasBaseUri(canvasUrl, out var baseUri))
            throw new ArgumentException("Enter the root HTTPS Canvas URL.");
        var file = new FileInfo(inputPath);
        if (!file.Exists || file.Length > MaxBytes)
            throw new InvalidOperationException("Choose a Canvas courses JSON file smaller than 2 MB.");

        using var document = ReadCanvasArray(file);

        var courses = document.RootElement.EnumerateArray()
            .Take(101)
            .Select(course => ParseCourse(course, baseUri))
            .Where(course => course is not null)
            .Cast<CourseDto>()
            .ToArray();
        if (courses.Length == 0 || courses.Length > 100)
            throw new InvalidOperationException("The Canvas JSON must contain between 1 and 100 named courses.");

        SaveSnapshot(SnapshotPath, courses);
    }

    public static void ImportUpcoming(string canvasUrl, string inputPath)
    {
        if (!LocalCanvasConnectionStore.TryCanvasBaseUri(canvasUrl, out var baseUri))
            throw new ArgumentException("Enter the root HTTPS Canvas URL.");
        using var document = ReadCanvasArray(new FileInfo(inputPath));
        var events = document.RootElement.EnumerateArray()
            .Take(101)
            .Select(item => ParseUpcoming(item, baseUri))
            .Where(item => item is not null)
            .Cast<SnapshotUpcomingEvent>()
            .ToArray();
        if (events.Length > 100)
            throw new InvalidOperationException("The Canvas JSON contains more than 100 upcoming events.");
        SaveSnapshot(UpcomingPath, events);
    }

    public static IReadOnlyList<SnapshotUpcomingEvent> LoadUpcoming()
    {
        if (!File.Exists(UpcomingPath))
            throw new InvalidOperationException("No Canvas upcoming snapshot found. Run import-upcoming first.");
        return JsonSerializer.Deserialize<SnapshotUpcomingEvent[]>(File.ReadAllText(UpcomingPath))
            ?? throw new InvalidOperationException("The Canvas upcoming snapshot is invalid.");
    }

    private static JsonDocument ReadCanvasArray(FileInfo file)
    {
        if (!file.Exists || file.Length > MaxBytes)
            throw new InvalidOperationException("Choose a Canvas JSON file smaller than 2 MB.");
        var document = JsonDocument.Parse(File.ReadAllText(file.FullName));
        if (document.RootElement.ValueKind == JsonValueKind.Array) return document;
        document.Dispose();
        throw new InvalidOperationException("The file must contain a Canvas JSON array. Save the API response itself, not an HTML page.");
    }

    private static void SaveSnapshot<T>(string destination, T[] items)
    {
        Directory.CreateDirectory(LocalCanvasPaths.Directory);
        var temporaryPath = destination + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(items));
            File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static SnapshotUpcomingEvent? ParseUpcoming(JsonElement item, Uri baseUri)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        var title = StringProperty(item, "title");
        if (string.IsNullOrWhiteSpace(title)) return null;
        var id = item.TryGetProperty("id", out var element)
            ? element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                _ => null
            }
            : null;
        var startsAt = DateTimeOffset.TryParse(StringProperty(item, "start_at"), out var start) ? start : (DateTimeOffset?)null;
        var endsAt = DateTimeOffset.TryParse(StringProperty(item, "end_at"), out var end) ? end : (DateTimeOffset?)null;
        var context = StringProperty(item, "context_code");
        var courseId = context?.StartsWith("course_", StringComparison.Ordinal) == true ? context[7..] : null;
        var rawUrl = StringProperty(item, "html_url");
        var url = Uri.TryCreate(rawUrl, UriKind.Absolute, out var candidate) &&
            candidate.Scheme == Uri.UriSchemeHttps &&
            candidate.Host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase) &&
            candidate.Port == baseUri.Port ? candidate.AbsoluteUri : null;
        return new SnapshotUpcomingEvent(id, title, startsAt, endsAt, courseId, url);
    }

    private static string? StringProperty(JsonElement item, string name) =>
        item.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() : null;

    public static IReadOnlyList<CourseDto> LoadCourses()
    {
        if (!File.Exists(SnapshotPath))
            throw new InvalidOperationException("No Canvas course snapshot found. Run import-courses first.");
        return JsonSerializer.Deserialize<CourseDto[]>(File.ReadAllText(SnapshotPath))
            ?? throw new InvalidOperationException("The Canvas course snapshot is invalid.");
    }

    private static CourseDto? ParseCourse(JsonElement course, Uri baseUri)
    {
        if (course.ValueKind != JsonValueKind.Object) return null;
        if (!course.TryGetProperty("id", out var idElement) ||
            !course.TryGetProperty("name", out var nameElement)) return null;
        var id = idElement.ValueKind switch
        {
            JsonValueKind.String => idElement.GetString(),
            JsonValueKind.Number => idElement.GetRawText(),
            _ => null
        };
        var name = nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) return null;
        var code = course.TryGetProperty("course_code", out var codeElement) &&
            codeElement.ValueKind == JsonValueKind.String ? codeElement.GetString() : null;
        var term = course.TryGetProperty("term", out var termElement) &&
            termElement.ValueKind == JsonValueKind.Object &&
            termElement.TryGetProperty("name", out var termName) &&
            termName.ValueKind == JsonValueKind.String ? termName.GetString() : null;
        var url = course.TryGetProperty("html_url", out var urlElement) &&
            urlElement.ValueKind == JsonValueKind.String &&
            Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var candidate) &&
            candidate.Scheme == Uri.UriSchemeHttps &&
            candidate.Host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase) &&
            candidate.Port == baseUri.Port ? candidate.AbsoluteUri : null;
        return new CourseDto(id, "Canvas", name, code, term, null, url);
    }
}

public sealed record SnapshotUpcomingEvent(
    string? Id,
    string Title,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? CourseId,
    string? Url);
