# quicksheet-cal

📅 Calendar extension for [QuickSheet](https://github.com/cemheren/QuickSheet) — see upcoming events from `.ics` files right on your desktop spreadsheet.

## Install

In any QuickSheet cell:

```
ext: github:cemheren/quicksheet-cal
```

## Usage

| Cell value | What it shows |
|---|---|
| `cal: ~/calendar.ics` | Events from a specific `.ics` file |
| `cal: today` | Today's events (auto-scans common calendar dirs) |
| `cal: week` | Next 7 days (default) |
| `cal: month` | Next 30 days |
| `cal: 14` | Next N days |

## How it works

The extension parses iCalendar (RFC 5545) `.ics` files directly — no external dependencies, no NuGet packages, no API keys.

When no file path is given, it scans common calendar directories:

- **GNOME Evolution** — `~/.local/share/evolution/calendar/local/`
- **GNOME Calendar** — `~/.local/share/gnome-calendar/local/`
- **Thunderbird** — `~/.thunderbird/` (recursive)
- **KDE Akonadi** — `~/.local/share/akonadi/`
- **Calcurse** — `~/.local/share/calcurse/` or `~/.calcurse/`
- **Drop folder** — `~/Calendars/` or `~/.calendars/`
- **Windows** — `Documents/Calendars/`

Export your Google Calendar, Outlook, or Apple Calendar as `.ics` and drop it into any of these locations.

## Output

Events are displayed grouped by date with smart labels:

```
📅 5 upcoming events

📌 Today
  09:00  Team standup          Room 3      1h
  14:30  Design review         Zoom        2h

📌 Tomorrow
  All day  Project deadline

📆 Fri May 16
  10:00  1:1 with manager      Office      30m
  16:00  Happy hour             Rooftop     2h
```

Columns shown (adapts to available grid space):
1. **Time** — `HH:mm` or "All day"
2. **Summary** — event title
3. **Location** — if available
4. **Duration** — e.g. "1h", "30m"

## Build from source

```bash
dotnet build CalendarExtension.csproj
```

Requires .NET 9. Zero NuGet dependencies.

## License

MIT — see [QuickSheet](https://github.com/cemheren/QuickSheet) for the main project.
