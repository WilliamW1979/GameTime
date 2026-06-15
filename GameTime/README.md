# GameTime

A universal game time management library for .NET 10. Converts real wall-clock time into game time at any configurable ratio, drives a fully customizable calendar (any number of months, days, week lengths, leap years, holidays), fires scheduled events and daily tasks, and persists everything to binary storage with no external dependencies.

Designed to run as a standalone time server process — other services register events over the network in advance, the time server fires them at the right moment, and each service receives the event ID and its own opaque payload back.

---

## Dependencies

**None.** GameTime has zero required external dependencies.

Settings access is abstracted behind `IGameTimeSettings`. Implement it with whatever settings backend your project uses — INI files, JSON, a database, hardcoded values, anything. If you use the [SettingsFile](https://github.com/WilliamW1979/SettingsFile) library, a ready-made adapter is included (see below).

---

## Features

- **Any calendar** — configure month names, month lengths, day names, days per week, leap year intervals, and holidays entirely through your settings implementation. No calendar logic is hardcoded.
- **Any time ratio** — `1h_24h` means 1 real hour = 24 game hours. `30m_1d` means 30 real minutes = 1 game day. Supports hours (`h`), minutes (`m`), and days (`d`).
- **Opaque event payloads** — events carry `byte[]` payloads. The time server stores and fires them without knowing what is inside. Callers serialize and deserialize their own data.
- **Thread-safe registration** — `RegisterEvent` and `UnregisterEvent` are safe to call from network handler threads while the tick loop runs on a separate thread.
- **Isolated handler invocation** — one bad subscriber never kills others. Every handler is invoked individually with exceptions caught and logged.
- **Binary event persistence** — events survive process restarts. The binary file uses a versioned format with magic header validation so corrupt files are detected and reported rather than silently misread.
- **Pluggable logger** — pass any `IGameTimeLogger` implementation. Defaults to console output. A null logger is provided to suppress all output.
- **Pluggable event store** — extend `GameEventStore` to back events with any storage without touching `GameTime`.
- **Pluggable settings** — implement `IGameTimeSettings` to use any configuration source.

---

## Wiring Up Settings

### With the SettingsFile Library

If your project uses the [SettingsFile](https://github.com/WilliamW1979/SettingsFile) library, define the `GAMETIME_USE_SETTINGSFILE` symbol in your project and use the included `SettingsFileAdapter`:

```xml
<!-- In your .csproj -->
<DefineConstants>GAMETIME_USE_SETTINGSFILE</DefineConstants>
```

```csharp
GameTime clock = new GameTime(
    settings:     new SettingsFileAdapter(new SettingsFile("Settings.ini")),
    taskSettings: new SettingsFileAdapter(new SettingsFile("Tasks.ini")),
    store:        new BinaryGameEventStore(),
    logger:       new ConsoleGameTimeLogger());
```

### With Any Other Settings Source

Implement `IGameTimeSettings` directly:

```csharp
public sealed class JsonSettings : IGameTimeSettings
{
    private readonly Dictionary<string, Dictionary<string, string>> _data;

    public JsonSettings(string path)
    {
        string json = File.ReadAllText(path);
        _data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json)!;
    }

    public string Get(string section, string key, string defaultValue) =>
        _data.TryGetValue(section, out Dictionary<string, string>? s) &&
        s.TryGetValue(key, out string? v) ? v : defaultValue;

    public T Get<T>(string section, string key, T defaultValue)
    {
        string raw = Get(section, key, string.Empty);
        if (string.IsNullOrEmpty(raw)) return defaultValue;
        try { return (T)Convert.ChangeType(raw, typeof(T)); }
        catch { return defaultValue; }
    }

    public void Set(string section, string key, string value)
    {
        if (!_data.ContainsKey(section)) _data[section] = new Dictionary<string, string>();
        _data[section][key] = value;
    }

    public void Flush() => File.WriteAllText(_path, JsonSerializer.Serialize(_data));

    public IEnumerable<string> GetSettings(string section) =>
        _data.TryGetValue(section, out Dictionary<string, string>? s)
            ? s.Keys : Enumerable.Empty<string>();
}
```

### With Hardcoded Values (testing / embedding)

```csharp
public sealed class HardcodedSettings : IGameTimeSettings
{
    private readonly Dictionary<string, Dictionary<string, string>> _data = new()
    {
        ["Server"]         = new() { ["TimeMode"] = "1h_24h", ["LiveDate"] = "2024-01-01T00:00:00+00:00" },
        ["CalendarConfig"] = new() { ["StartYear"] = "0", ["MonthsInYear"] = "12",
                                     ["DaysPerWeek"] = "7", ["LeapYearInterval"] = "4", ["LeapMonth"] = "2" },
        ["MonthNames"]     = new() { ["1"]="January",["2"]="February",["3"]="March",["4"]="April",
                                     ["5"]="May",["6"]="June",["7"]="July",["8"]="August",
                                     ["9"]="September",["10"]="October",["11"]="November",["12"]="December" },
        ["MonthLengths"]   = new() { ["1"]="31",["2"]="28",["3"]="31",["4"]="30",["5"]="31",["6"]="30",
                                     ["7"]="31",["8"]="31",["9"]="30",["10"]="31",["11"]="30",["12"]="31" },
        ["DayNames"]       = new() { ["1"]="Sunday",["2"]="Monday",["3"]="Tuesday",["4"]="Wednesday",
                                     ["5"]="Thursday",["6"]="Friday",["7"]="Saturday" }
    };

    public string Get(string section, string key, string defaultValue) =>
        _data.TryGetValue(section, out var s) && s.TryGetValue(key, out string? v) ? v : defaultValue;

    public T Get<T>(string section, string key, T defaultValue)
    {
        string raw = Get(section, key, string.Empty);
        if (string.IsNullOrEmpty(raw)) return defaultValue;
        try { return (T)Convert.ChangeType(raw, typeof(T)); }
        catch { return defaultValue; }
    }

    public void Set(string section, string key, string value)
    {
        if (!_data.ContainsKey(section)) _data[section] = new();
        _data[section][key] = value;
    }

    public void Flush() { }

    public IEnumerable<string> GetSettings(string section) =>
        _data.TryGetValue(section, out var s) ? s.Keys : Enumerable.Empty<string>();
}
```

---

## Quick Start

### 2. Basic Usage

```csharp
GameTime clock = new GameTime(
    settings:     mySettings,
    taskSettings: myTaskSettings,
    store:        new BinaryGameEventStore());

// Subscribe to time change events
clock.OnMinuteChanged  += date => Console.WriteLine($"Minute: {date.TimeFormatted}");
clock.OnDayChanged     += date => Console.WriteLine($"New day: {date.DayName} {date.Day} {date.MonthName}");
clock.OnHolidayStarted += date => Console.WriteLine($"Holiday: {date.Holiday}");

// Tick loop — call as often as you want, GameTime deduplicates within the same second
while (true)
{
    GameDateDetails date = clock.GetCalendarDate();
    Thread.Sleep(100);
}
```

### Reading the Current Date

```csharp
GameDateDetails d = clock.Current;

Console.WriteLine(d.Year);           // e.g. 142
Console.WriteLine(d.MonthName);      // e.g. "March"
Console.WriteLine(d.Day);            // e.g. 15
Console.WriteLine(d.DayName);        // e.g. "Tuesday"
Console.WriteLine(d.TimeFormatted);  // e.g. "14:32:07"
Console.WriteLine(d.Hour);           // 14
Console.WriteLine(d.Minute);         // 32
Console.WriteLine(d.Second);         // 7
Console.WriteLine(d.Holiday);        // "None" or holiday name
Console.WriteLine(d.IsLeapYear);     // true/false
Console.WriteLine(d.DayProgress);    // 0.0 – 1.0 (how far through the day)
Console.WriteLine(d.YearProgress);   // 0.0 – 1.0 (how far through the year)
Console.WriteLine(d.DayOfWeekIndex); // 1-based index into DayNames
```

Convenience accessors are also available directly on the `GameTime` instance:

```csharp
Console.WriteLine(clock.Year);
Console.WriteLine(clock.MonthName);
Console.WriteLine(clock.TimeOfDay);
Console.WriteLine(clock.IsHoliday);
Console.WriteLine(clock.DayProgress);
```

---

## Settings Keys Reference

GameTime reads the following keys from your `IGameTimeSettings` implementation.

### [Server]

| Key | Type | Description |
|---|---|---|
| `TimeMode` | string | Time ratio, e.g. `1h_24h`. Written by `WriteDefaults` if missing. |
| `LiveDate` | string | UTC baseline date in ISO 8601 format. Written automatically on first run. |

### [CalendarConfig]

| Key | Type | Description |
|---|---|---|
| `StartYear` | int | The year number the calendar begins at. |
| `MonthsInYear` | int | Number of months in a year. |
| `DaysPerWeek` | int | Number of days in a week. |
| `LeapYearInterval` | int | Every N years is a leap year. 0 = no leap years. |
| `LeapMonth` | int | Which month gets the extra day in a leap year (1-based). |

### [MonthNames], [MonthNames_Short], [MonthLengths]

Keys are `1` through `MonthsInYear`. `MonthNames_Short` defaults to the first 3 characters of the full name if not specified.

### [DayNames], [DayNames_Short]

Keys are `1` through `DaysPerWeek`. `DayNames_Short` defaults to the first 3 characters of the full name if not specified.

### [Holidays]

Keys are `month-day` (e.g. `12-25`). Values are holiday names (e.g. `Winter Festival`). Optional — missing section means no holidays.

### Writing Defaults

Call `CalendarConfig.WriteDefaults(settings)` to populate your settings implementation with a complete Earth-like calendar on first run:

```csharp
if (string.IsNullOrEmpty(mySettings.Get("Server", "TimeMode", "")))
    CalendarConfig.WriteDefaults(mySettings);
```

GameTime calls this automatically if `TimeMode` is missing when it starts.

---

## Time Ratio Format

Set `TimeMode` in `[Server]` to control how fast game time passes.

| TimeMode | Meaning |
|---|---|
| `1h_1h` | Real time (1:1) |
| `1h_24h` | 1 real hour = 24 game hours (1 game day per hour) |
| `1h_1d` | 1 real hour = 1 full game day |
| `30m_1d` | 30 real minutes = 1 game day |
| `1d_1d` | 1 real day = 1 game day |
| `1m_1h` | 1 real minute = 1 game hour |

Units: `h` = hours, `m` = minutes, `d` = days. Decimal values are supported: `1.5h_1d`.

---

## Events

Events fire at a specific minute of the game day and carry an opaque `byte[]` payload. The time server stores and fires them without knowing what is inside.

### Registering an Event

```csharp
byte[] payload = MySerializer.Serialize(new { Temperature = 22.5, WindSpeed = 12.0 });

GameEvent ev = clock.RegisterEvent(
    eventID:     "DaytimeBegins",
    startMinute: 360,
    endMinute:   1080,
    repeat:      GameEventRepeat.Daily,
    payload:     payload);

ev.SubscribeStart(args =>
{
    var data = MySerializer.Deserialize(args.Payload);
    Console.WriteLine($"Daytime started at {args.Date.TimeFormatted}");
});

ev.SubscribeEnd(args => Console.WriteLine($"Daytime ended at {args.Date.TimeFormatted}"));
```

### Repeat Modes

| Repeat | Fires | Extra parameter |
|---|---|---|
| `Once` | Once then auto-unregisters | — |
| `Daily` | Every game day | — |
| `Weekly` | One specific day of the week | `dayOfWeek` (1-based) |
| `Monthly` | One specific day of the month | `monthDay` |
| `Yearly` | One specific month and day | `yearMonth`, `yearDay` |

```csharp
// Every Monday (day 2)
clock.RegisterEvent("WeeklyReset", 0, -1, GameEventRepeat.Weekly, dayOfWeek: 2);

// Every 15th of the month
clock.RegisterEvent("MonthlyTax", 0, -1, GameEventRepeat.Monthly, monthDay: 15);

// Month 6, day 21 every year
clock.RegisterEvent("SummerFestival", 480, 1320, GameEventRepeat.Yearly, yearMonth: 6, yearDay: 21);
```

Pass `endMinute: -1` for events that have no end.

### Restoring Events After Restart

```csharp
clock.LoadPersistedEvents((eventID, payload) => eventID switch
{
    "DaytimeBegins"  => (OnDaytimeStart, OnDaytimeEnd),
    "WeeklyReset"    => (OnWeeklyReset, null),
    "SummerFestival" => (OnFestivalStart, OnFestivalEnd),
    _                => (null, null)
});
```

### Querying Events

```csharp
bool   active  = clock.IsEventActive("DaytimeBegins");
GameEvent? ev  = clock.GetEvent("DaytimeBegins");
double secs    = clock.SecondsUntilNextEvent();  // -1 if no events registered
```

---

## Scheduled Tasks

Daily tasks fire at a specific game hour and minute every day.

```csharp
ScheduledTask task = clock.RegisterDailyTask(
    taskID:     "MidnightReset",
    hour:       0,
    minute:     0,
    taskAction: date => Console.WriteLine($"Midnight on {date.DayName}"));

clock.UnregisterDailyTask(task);
```

Restore on restart:

```csharp
clock.LoadPersistentTasks(taskID => taskID switch
{
    "MidnightReset" => date => DoMidnightReset(date),
    _               => null
});
```

---

## Per-Day Subscriptions

```csharp
clock.SubscribeToDay("Monday",   date => Console.WriteLine($"Monday, year {date.Year}"));
clock.UnsubscribeFromDay("Monday", handler);
```

---

## Time Change Events

| Event | Fires when |
|---|---|
| `OnSecondChanged` | Every game second |
| `OnMinuteChanged` | Every game minute |
| `OnHourChanged` | Every game hour |
| `OnDayChanged` | Every game day |
| `OnMonthChanged` | Every game month |
| `OnYearChanged` | Every game year |
| `OnHolidayStarted` | When the day begins and it is a holiday |

---

## Event Payloads

Payloads are opaque `byte[]`. Serialize however your project requires.

**BinaryWriter (no dependencies):**
```csharp
byte[] Serialize(double azimuth, double altitude)
{
    using MemoryStream ms = new MemoryStream();
    using BinaryWriter bw = new BinaryWriter(ms);
    bw.Write(azimuth);
    bw.Write(altitude);
    return ms.ToArray();
}

(double azimuth, double altitude) Deserialize(byte[] data)
{
    using MemoryStream ms = new MemoryStream(data);
    using BinaryReader br = new BinaryReader(ms);
    return (br.ReadDouble(), br.ReadDouble());
}
```

**System.Text.Json:**
```csharp
byte[] payload = JsonSerializer.SerializeToUtf8Bytes(myData);
MyData data    = JsonSerializer.Deserialize<MyData>(args.Payload)!;
```

---

## Custom Event Store

```csharp
public sealed class DatabaseEventStore : GameEventStore
{
    public override void Save(PersistedEvent ev)   { /* write to db */ }
    public override void Delete(string eventID)    { /* delete from db */ }
    public override List<PersistedEvent> LoadAll() { /* read from db */ }
}
```

---

## Custom Logger

```csharp
public sealed class MyLogger : IGameTimeLogger
{
    public void Info(string message)  => MyLog.Info(message);
    public void Warn(string message)  => MyLog.Warn(message);
    public void Error(string message) => MyLog.Error(message);
}
```

Pass `new NullGameTimeLogger()` to suppress all output.

---

## Binary Event File Format

```
Header (5 bytes)
  [0..3]  Magic: 0x47 0x54 0x45 0x56  ('G','T','E','V')
  [4]     Version: 0x01

Per-record (variable length)
  int32   EventID byte length
  bytes   EventID (UTF-8)
  int32   StartMinute
  int32   EndMinute
  byte    Repeat (GameEventRepeat enum value)
  int32   DayOfWeek
  int32   MonthDay
  int32   YearMonth
  int32   YearDay
  int32   Payload byte length
  bytes   Payload (opaque)
```

Corrupt records are skipped with a warning logged. Valid records before the corruption are loaded normally.

---

## Thread Safety

`RegisterEvent`, `UnregisterEvent`, `RegisterDailyTask`, `UnregisterDailyTask`, `SubscribeToDay`, and `UnsubscribeFromDay` are safe to call from any thread.

`GetCalendarDate` is designed for a single tick-loop thread. The `Current` property returns an immutable record — take a snapshot from any thread safely:

```csharp
GameDateDetails snapshot = clock.Current;
```

---

## Error Handling

- **Bad config** — `CalendarConfig.Load` throws `CalendarConfigException` listing every missing or invalid key at once.
- **Bad binary file** — corrupt or unrecognized files are detected via the magic header. Corrupt records are skipped and logged; the process continues with readable records.
- **Handler exceptions** — each subscriber is invoked individually. One throwing handler does not prevent others from firing.

---

## Requirements

- .NET 10
- No required external dependencies
- Optional: [SettingsFile](https://github.com/WilliamW1979/SettingsFile) (define `GAMETIME_USE_SETTINGSFILE` to enable the included adapter)