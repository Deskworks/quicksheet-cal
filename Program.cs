using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

/// <summary>
/// QuickSheet Calendar Extension — reads .ics files and shows upcoming events.
/// Prefix: "cal". Usage: "cal: ~/calendar.ics" or "cal: today" (uses default path).
/// Parses iCalendar (RFC 5545) VEVENT blocks without any NuGet dependencies.
/// </summary>
class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly string DefaultIcsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "evolution", "calendar", "local");

    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                string? type = doc.RootElement.TryGetProperty("type", out var tp) ? tp.GetString() : null;

                switch (type)
                {
                    case "init":
                        HandleInit();
                        break;
                    case "activate":
                        HandleActivate(doc.RootElement);
                        break;
                    case "deactivate":
                        break;
                }
            }
            catch (Exception ex)
            {
                SendError("", $"Parse error: {ex.Message}");
            }
        }
    }

    static void HandleInit()
    {
        SendJson(new
        {
            type = "register",
            prefix = "cal",
            name = "Calendar",
            version = "1.0.0"
        });
        SendLog("Calendar extension registered with prefix 'cal'");
    }

    static void HandleActivate(JsonElement root)
    {
        string id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
        int gridCols = root.TryGetProperty("gridCols", out var gc) ? gc.GetInt32() : 4;
        int gridRows = root.TryGetProperty("gridRows", out var gr) ? gr.GetInt32() : 8;

        string[] extParams = [];
        if (root.TryGetProperty("params", out var paramsProp) && paramsProp.ValueKind == JsonValueKind.Array)
        {
            extParams = paramsProp.EnumerateArray()
                .Select(p => p.GetString() ?? "")
                .ToArray();
        }

        string param = extParams.Length > 0 ? string.Join(" ", extParams).Trim() : "";

        try
        {
            var events = LoadEvents(param);
            var cells = FormatEvents(events, gridRows, gridCols);
            SendJson(new { type = "write", id, cells });
        }
        catch (Exception ex)
        {
            SendJson(new { type = "write", id, cells = new object[]
            {
                new { r = 0, c = 0, v = "📅 Calendar Error" },
                new { r = 1, c = 0, v = ex.Message }
            }});
        }
    }

    static List<CalEvent> LoadEvents(string param)
    {
        var allEvents = new List<CalEvent>();

        // Determine days to show
        int daysAhead = 7;
        string? icsPath = null;

        if (param.Equals("today", StringComparison.OrdinalIgnoreCase))
            daysAhead = 1;
        else if (param.Equals("week", StringComparison.OrdinalIgnoreCase) || param == "")
            daysAhead = 7;
        else if (param.Equals("month", StringComparison.OrdinalIgnoreCase))
            daysAhead = 30;
        else if (int.TryParse(param, out int days))
            daysAhead = days;
        else
            icsPath = ExpandPath(param);

        var now = DateTime.Now;
        var horizon = now.AddDays(daysAhead);

        if (icsPath != null)
        {
            // Single file mode
            if (!File.Exists(icsPath))
                throw new FileNotFoundException($"File not found: {icsPath}");
            allEvents.AddRange(ParseIcsFile(icsPath));
        }
        else
        {
            // Scan common calendar directories for .ics files
            foreach (var dir in GetCalendarDirs())
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir, "*.ics", SearchOption.AllDirectories))
                {
                    try { allEvents.AddRange(ParseIcsFile(file)); }
                    catch { /* skip unparseable files */ }
                }
            }
        }

        // Filter to upcoming events within the horizon
        return allEvents
            .Where(e => e.Start >= now && e.Start <= horizon)
            .OrderBy(e => e.Start)
            .ToList();
    }

    static string[] GetCalendarDirs()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[]
        {
            // GNOME Evolution
            Path.Combine(home, ".local", "share", "evolution", "calendar", "local"),
            // GNOME Calendar / GNOME Online Accounts
            Path.Combine(home, ".local", "share", "gnome-calendar", "local"),
            // Thunderbird (common profile path — scans recursively)
            Path.Combine(home, ".thunderbird"),
            // KDE Akonadi
            Path.Combine(home, ".local", "share", "akonadi"),
            // Calcurse
            Path.Combine(home, ".local", "share", "calcurse"),
            Path.Combine(home, ".calcurse"),
            // Generic .ics drop folder
            Path.Combine(home, "Calendars"),
            Path.Combine(home, ".calendars"),
            // Windows Outlook exports
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Calendars"),
        };
    }

    static string ExpandPath(string path)
    {
        if (path.StartsWith("~/"))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        return Path.GetFullPath(path);
    }

    static List<CalEvent> ParseIcsFile(string path)
    {
        var events = new List<CalEvent>();
        var lines = File.ReadAllLines(path);

        // Unfold continuation lines (RFC 5545 §3.1)
        var unfolded = new List<string>();
        foreach (var rawLine in lines)
        {
            if (rawLine.Length > 0 && (rawLine[0] == ' ' || rawLine[0] == '\t') && unfolded.Count > 0)
                unfolded[^1] += rawLine[1..];
            else
                unfolded.Add(rawLine);
        }

        bool inEvent = false;
        string? summary = null;
        string? location = null;
        DateTime? dtStart = null;
        DateTime? dtEnd = null;

        foreach (var line in unfolded)
        {
            if (line.StartsWith("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                inEvent = true;
                summary = null;
                location = null;
                dtStart = null;
                dtEnd = null;
            }
            else if (line.StartsWith("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (inEvent && summary != null && dtStart.HasValue)
                {
                    events.Add(new CalEvent
                    {
                        Summary = UnescapeIcs(summary),
                        Location = location != null ? UnescapeIcs(location) : null,
                        Start = dtStart.Value,
                        End = dtEnd ?? dtStart.Value.AddHours(1)
                    });
                }
                inEvent = false;
            }
            else if (inEvent)
            {
                if (TryGetIcsValue(line, "SUMMARY", out var s))
                    summary = s;
                else if (TryGetIcsValue(line, "LOCATION", out var loc))
                    location = loc;
                else if (TryGetIcsDateValue(line, "DTSTART", out var ds))
                    dtStart = ds;
                else if (TryGetIcsDateValue(line, "DTEND", out var de))
                    dtEnd = de;
            }
        }

        return events;
    }

    static bool TryGetIcsValue(string line, string property, out string value)
    {
        value = "";
        // Match "PROPERTY:" or "PROPERTY;params:"
        if (line.StartsWith(property, StringComparison.OrdinalIgnoreCase))
        {
            int colonIdx = line.IndexOf(':');
            if (colonIdx >= property.Length)
            {
                // Verify it's actually this property (not a prefix match)
                string beforeColon = line[..colonIdx];
                if (beforeColon.Equals(property, StringComparison.OrdinalIgnoreCase) ||
                    beforeColon.StartsWith(property + ";", StringComparison.OrdinalIgnoreCase))
                {
                    value = line[(colonIdx + 1)..];
                    return true;
                }
            }
        }
        return false;
    }

    static bool TryGetIcsDateValue(string line, string property, out DateTime result)
    {
        result = default;
        if (!TryGetIcsValue(line, property, out var val))
            return false;

        return TryParseIcsDateTime(val, out result);
    }

    static bool TryParseIcsDateTime(string val, out DateTime result)
    {
        result = default;
        val = val.Trim();

        // YYYYMMDD (all-day event)
        if (val.Length == 8 && DateTime.TryParseExact(val, "yyyyMMdd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            return true;

        // YYYYMMDDTHHmmss (local time)
        if (val.Length == 15 && DateTime.TryParseExact(val, "yyyyMMdd'T'HHmmss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            return true;

        // YYYYMMDDTHHmmssZ (UTC)
        if (val.Length == 16 && val.EndsWith("Z") && DateTime.TryParseExact(val, "yyyyMMdd'T'HHmmss'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result))
        {
            result = result.ToLocalTime();
            return true;
        }

        return false;
    }

    static string UnescapeIcs(string val)
    {
        return val.Replace("\\n", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");
    }

    static object[] FormatEvents(List<CalEvent> events, int maxRows, int cols)
    {
        var cells = new List<object>();

        if (events.Count == 0)
        {
            cells.Add(new { r = 0, c = 0, v = "📅 No upcoming events" });
            cells.Add(new { r = 1, c = 0, v = "Drop .ics files in ~/Calendars or specify path" });
            cells.Add(new { r = 2, c = 0, v = "Usage: cal: ~/my.ics | cal: today | cal: week" });
            return cells.ToArray();
        }

        // Header
        cells.Add(new { r = 0, c = 0, v = $"📅 {events.Count} upcoming event{(events.Count == 1 ? "" : "s")}" });

        int row = 1;
        string? lastDate = null;

        foreach (var ev in events)
        {
            if (row >= maxRows - 1) break;

            string dateStr = ev.Start.ToString("ddd MMM dd");
            if (dateStr != lastDate)
            {
                // Date separator
                if (row > 1 && row < maxRows - 1)
                {
                    cells.Add(new { r = row, c = 0, v = "" });
                    row++;
                }
                if (row >= maxRows - 1) break;

                string dateLabel = ev.Start.Date == DateTime.Today ? "📌 Today"
                    : ev.Start.Date == DateTime.Today.AddDays(1) ? "📌 Tomorrow"
                    : $"📆 {dateStr}";
                cells.Add(new { r = row, c = 0, v = dateLabel });
                row++;
                lastDate = dateStr;
            }

            if (row >= maxRows) break;

            // Time
            bool allDay = ev.Start.Hour == 0 && ev.Start.Minute == 0 &&
                          ev.End.Hour == 0 && ev.End.Minute == 0;
            string time = allDay ? "All day" : ev.Start.ToString("HH:mm");

            cells.Add(new { r = row, c = 0, v = $"  {time}" });

            if (cols >= 2)
                cells.Add(new { r = row, c = 1, v = Truncate(ev.Summary, 30) });

            if (cols >= 3 && ev.Location != null)
                cells.Add(new { r = row, c = 2, v = Truncate(ev.Location, 20) });

            if (cols >= 4 && !allDay)
            {
                var duration = ev.End - ev.Start;
                string dur = duration.TotalHours >= 1
                    ? $"{duration.TotalHours:F0}h"
                    : $"{duration.TotalMinutes:F0}m";
                cells.Add(new { r = row, c = 3, v = dur });
            }

            row++;
        }

        return cells.ToArray();
    }

    static string Truncate(string s, int max)
    {
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }

    static void SendJson(object obj)
    {
        string json = JsonSerializer.Serialize(obj, JsonOpts);
        Console.WriteLine(json);
        Console.Out.Flush();
    }

    static void SendError(string id, string message)
    {
        SendJson(new { type = "error", id, message });
    }

    static void SendLog(string message)
    {
        SendJson(new { type = "log", level = "info", message });
    }
}

class CalEvent
{
    public string Summary { get; set; } = "";
    public string? Location { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
}
