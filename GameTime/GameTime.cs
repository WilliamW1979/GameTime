using System.Globalization;

namespace GameTime;

public sealed class GameTime
{
    private readonly IGameTimeSettings _settings;
    private readonly IGameTimeSettings _taskSettings;
    private readonly GameEventStore _store;
    private readonly IGameTimeLogger _logger;
    private readonly CalendarConfig _calendar;
    private readonly DateTimeOffset _liveDate;
    private readonly double _timeMultiplier;
    private int _lastSecondProcessed = -1;
    private int _lastMinuteProcessed = -1;
    private int _lastHourProcessed = -1;
    private int _lastDayProcessed = -1;
    private int _lastMonthProcessed = -1;
    private int _lastYearProcessed = -1;
    private GameDateDetails _current = null!;
    private readonly object _registryLock = new object();
    private readonly Dictionary<string, GameEvent> _eventRegistry = new Dictionary<string, GameEvent>(StringComparer.Ordinal);
    private readonly Dictionary<int, List<GameEvent>> _startBuckets = new Dictionary<int, List<GameEvent>>();
    private readonly Dictionary<int, List<GameEvent>> _endBuckets = new Dictionary<int, List<GameEvent>>();
    private readonly List<ScheduledTask> _dailyTasks = new List<ScheduledTask>();
    private readonly Dictionary<string, Action<GameDateDetails>?> _onDayHandlers = new Dictionary<string, Action<GameDateDetails>?>(StringComparer.OrdinalIgnoreCase);
    public event Action<GameDateDetails>? OnSecondChanged;
    public event Action<GameDateDetails>? OnMinuteChanged;
    public event Action<GameDateDetails>? OnHourChanged;
    public event Action<GameDateDetails>? OnDayChanged;
    public event Action<GameDateDetails>? OnMonthChanged;
    public event Action<GameDateDetails>? OnYearChanged;
    public event Action<GameDateDetails>? OnHolidayStarted;
    public GameDateDetails Current => _current;
    public int Year => _current.Year;
    public int Month => _current.Month;
    public string MonthName => _current.MonthName;
    public string MonthShort => _current.MonthShort;
    public int Day => _current.Day;
    public string DayName => _current.DayName;
    public string DayShort => _current.DayShort;
    public int DayOfWeek => _current.DayOfWeekIndex;
    public int Hour => _current.Hour;
    public int Minute => _current.Minute;
    public int Second => _current.Second;
    public string TimeOfDay => _current.TimeFormatted;
    public string Holiday => _current.Holiday;
    public bool IsLeapYear => _current.IsLeapYear;
    public bool IsHoliday => !string.Equals(_current.Holiday, "None", StringComparison.Ordinal);
    public double DayProgress => _current.DayProgress;
    public double YearProgress => _current.YearProgress;
    public int TotalDays => _lastDayProcessed >= 0 ? _lastDayProcessed : 0;
    public double TotalGameSeconds => GetTotalGameSeconds();
    public GameTime(IGameTimeSettings settings, IGameTimeSettings taskSettings, GameEventStore store, IGameTimeLogger? logger = null)
    {
        _settings = settings;
        _taskSettings = taskSettings;
        _store = store;
        _logger = logger ?? new ConsoleGameTimeLogger();
        if (string.IsNullOrEmpty(_settings.Get("Server", "TimeMode", "")))
        {
            _logger.Info("[GameTime] No configuration found — writing defaults.");
            CalendarConfig.WriteDefaults(_settings);
        }
        _calendar = CalendarConfig.Load(_settings);
        _timeMultiplier = ParseTimeMultiplier(_settings.Get("Server", "TimeMode", "1h_1h"));
        string savedDate = _settings.Get("Server", "LiveDate", "");
        if (string.IsNullOrEmpty(savedDate))
        {
            _liveDate = DateTimeOffset.UtcNow;
            _settings.Set("Server", "LiveDate", _liveDate.ToString("O", CultureInfo.InvariantCulture));
            _settings.Flush();
        }
        else
            _liveDate = DateTimeOffset.Parse(savedDate, CultureInfo.InvariantCulture);
        for (int i = 1; i <= _calendar.DaysPerWeek; i++)
            _onDayHandlers[_calendar.DayNames[i]] = null;
        _current = BuildDate(GetTotalGameSeconds());
        _lastDayProcessed = (int)(GetTotalGameSeconds() / 86400);
        _lastMonthProcessed = _current.Month;
        _lastYearProcessed = _current.Year;
    }

    public void SubscribeToDay(string dayName, Action<GameDateDetails> handler)
    {
        lock (_registryLock)
        {
            _onDayHandlers.TryGetValue(dayName, out Action<GameDateDetails>? existing);
            _onDayHandlers[dayName] = existing + handler;
        }
    }

    public void UnsubscribeFromDay(string dayName, Action<GameDateDetails> handler)
    {
        lock (_registryLock)
            if (_onDayHandlers.TryGetValue(dayName, out Action<GameDateDetails>? existing))
                _onDayHandlers[dayName] = existing - handler;
    }

    public GameEvent RegisterEvent(string eventID, int startMinute, int endMinute, GameEventRepeat repeat, int dayOfWeek = 0, int monthDay = 0, int yearMonth = 0, int yearDay = 0, byte[]? payload = null)
    {
        lock (_registryLock)
        {
            if (_eventRegistry.TryGetValue(eventID, out GameEvent? existing))
                return existing;
            GameEvent ev = new GameEvent(eventID, startMinute, endMinute, repeat, dayOfWeek, monthDay, yearMonth, yearDay, payload);
            _eventRegistry[eventID] = ev;
            AddToBucket(_startBuckets, startMinute, ev);
            if (endMinute >= 0) AddToBucket(_endBuckets, endMinute, ev);
            _store.Save(new PersistedEvent(eventID, startMinute, endMinute, repeat, dayOfWeek, monthDay, yearMonth, yearDay, payload));
            return ev;
        }
    }

    public void UnregisterEvent(string eventID)
    {
        lock (_registryLock)
        {
            if (!_eventRegistry.TryGetValue(eventID, out GameEvent? ev)) return;
            _eventRegistry.Remove(eventID);
            RemoveFromBucket(_startBuckets, ev.StartMinute, ev);
            if (ev.EndMinute >= 0) RemoveFromBucket(_endBuckets, ev.EndMinute, ev);
            _store.Delete(eventID);
        }
    }

    public void LoadPersistedEvents(Func<string, byte[], (Action<GameEventArgs>? onStart, Action<GameEventArgs>? onEnd)> resolver)
    {
        foreach (PersistedEvent pe in _store.LoadAll())
        {
            GameEvent ev = RegisterEvent(pe.EventID, pe.StartMinute, pe.EndMinute, pe.Repeat, pe.DayOfWeek, pe.MonthDay, pe.YearMonth, pe.YearDay, pe.Payload);
            (Action<GameEventArgs>? onStart, Action<GameEventArgs>? onEnd) = resolver(pe.EventID, pe.Payload);
            if (onStart != null) ev.SubscribeStart(onStart);
            if (onEnd != null) ev.SubscribeEnd(onEnd);
        }
    }

    public bool IsEventActive(string eventID)
    {
        lock (_registryLock)
            return _eventRegistry.TryGetValue(eventID, out GameEvent? ev) && ev.IsActive;
    }

    public GameEvent? GetEvent(string eventID)
    {
        lock (_registryLock)
            return _eventRegistry.TryGetValue(eventID, out GameEvent? ev) ? ev : null;
    }

    public double SecondsUntilNextEvent()
    {
        lock (_registryLock)
        {
            if (_eventRegistry.Count == 0) return -1;
            double dayElapsed = GetTotalGameSeconds() % 86400.0;
            double best = double.MaxValue;
            foreach (GameEvent ev in _eventRegistry.Values)
            {
                double evSec = ev.StartMinute * 60.0;
                double diff = evSec > dayElapsed ? evSec - dayElapsed : 86400.0 - dayElapsed + evSec;
                if (diff < best) best = diff;
            }
            return best == double.MaxValue ? -1 : best;
        }
    }

    public ScheduledTask RegisterDailyTask(string taskID, int hour, int minute, Action<GameDateDetails> taskAction)
    {
        ScheduledTask t = new ScheduledTask(taskID, hour, minute, taskAction);
        lock (_registryLock)
            _dailyTasks.Add(t);
        _taskSettings.Set("ScheduledTasks", taskID, $"{hour},{minute}");
        string idList = _taskSettings.Get("Manifest", "ActiveIDs", "");
        if (!idList.Contains(taskID))
            _taskSettings.Set("Manifest", "ActiveIDs", string.IsNullOrEmpty(idList) ? taskID : $"{idList},{taskID}");
        _taskSettings.Flush();
        return t;
    }

    public void UnregisterDailyTask(ScheduledTask task)
    {
        lock (_registryLock)
            _dailyTasks.Remove(task);
    }

    public void LoadPersistentTasks(Func<string, Action<GameDateDetails>?> resolver)
    {
        string idList = _taskSettings.Get("Manifest", "ActiveIDs", "");
        if (string.IsNullOrEmpty(idList)) return;
        foreach (string id in idList.Split(','))
        {
            string raw = _taskSettings.Get("ScheduledTasks", id, "");
            if (string.IsNullOrEmpty(raw)) continue;
            string[] parts = raw.Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int h) || !int.TryParse(parts[1], out int m)) continue;
            Action<GameDateDetails>? action = resolver(id);
            if (action != null)
            {
                lock (_registryLock)
                    _dailyTasks.Add(new ScheduledTask(id, h, m, action));
            }
        }
    }

    public GameDateDetails GetCalendarDate()
    {
        double totalSecs = GetTotalGameSeconds();
        int currentSecond = (int)totalSecs;
        if (currentSecond == _lastSecondProcessed)
            return _current;
        int totalDays = (int)(totalSecs / 86400);
        _current = BuildDate(totalSecs, totalDays);
        _lastSecondProcessed = currentSecond;
        SafeInvoke(OnSecondChanged, _current, "OnSecondChanged");
        int currentMinute = currentSecond / 60;
        if (currentMinute != _lastMinuteProcessed)
        {
            _lastMinuteProcessed = currentMinute;
            SafeInvoke(OnMinuteChanged, _current, "OnMinuteChanged");
            FireDailyTasks(currentMinute % 1440);
            FireGameEvents(currentMinute % 1440);
        }
        int currentHour = currentSecond / 3600;
        if (currentHour != _lastHourProcessed)
        {
            _lastHourProcessed = currentHour;
            SafeInvoke(OnHourChanged, _current, "OnHourChanged");
        }
        if (totalDays != _lastDayProcessed)
        {
            _lastDayProcessed = totalDays;
            SafeInvoke(OnDayChanged, _current, "OnDayChanged");
            Action<GameDateDetails>? dayHandler;
            lock (_registryLock)
                _onDayHandlers.TryGetValue(_current.DayName, out dayHandler);
            SafeInvoke(dayHandler, _current, $"OnDay[{_current.DayName}]");
            if (IsHoliday)
                SafeInvoke(OnHolidayStarted, _current, "OnHolidayStarted");
        }
        if (_current.Month != _lastMonthProcessed)
        {
            _lastMonthProcessed = _current.Month;
            SafeInvoke(OnMonthChanged, _current, "OnMonthChanged");
        }
        if (_current.Year != _lastYearProcessed)
        {
            _lastYearProcessed = _current.Year;
            SafeInvoke(OnYearChanged, _current, "OnYearChanged");
        }
        return _current;
    }

    private void FireDailyTasks(int minuteOfDay)
    {
        List<ScheduledTask> snapshot;
        lock (_registryLock)
        {
            if (_dailyTasks.Count == 0) return;
            snapshot = new List<ScheduledTask>(_dailyTasks);
        }
        int hour = minuteOfDay / 60;
        int minute = minuteOfDay % 60;
        foreach (ScheduledTask task in snapshot)
        {
            if (task.Hour != hour || task.Minute != minute) continue;
            try { task.TaskAction.Invoke(_current); }
            catch (Exception ex) { _logger.Error($"[GameTime] Task '{task.TaskID}' threw: {ex.Message}"); }
        }
    }

    private void FireGameEvents(int minuteOfDay)
    {
        List<GameEvent> startSnapshot;
        List<GameEvent> endSnapshot;
        lock (_registryLock)
        {
            startSnapshot = _startBuckets.TryGetValue(minuteOfDay, out List<GameEvent>? s) ? new List<GameEvent>(s) : new List<GameEvent>();
            endSnapshot = _endBuckets.TryGetValue(minuteOfDay, out List<GameEvent>? e) ? new List<GameEvent>(e) : new List<GameEvent>();
        }

        foreach (GameEvent ev in startSnapshot)
        {
            if (!EventPassesFilter(ev, _current)) continue;
            ev.IsActive = true;
            GameEventArgs args = new GameEventArgs(ev.EventID, _current, true, ev.Payload);
            SafeFireEvent(ev.OnStart, args, ev.EventID, "start");
            if (ev.Repeat == GameEventRepeat.Once)
                UnregisterEvent(ev.EventID);
        }
        foreach (GameEvent ev in endSnapshot)
        {
            if (!ev.IsActive) continue;
            ev.IsActive = false;
            GameEventArgs args = new GameEventArgs(ev.EventID, _current, false, ev.Payload);
            SafeFireEvent(ev.OnEnd, args, ev.EventID, "end");
        }
    }

    private void SafeFireEvent(Action<GameEventArgs>? handler, GameEventArgs args, string eventID, string phase)
    {
        if (handler == null) return;
        foreach (Delegate d in handler.GetInvocationList())
        {
            try { ((Action<GameEventArgs>)d)(args); }
            catch (Exception ex) { _logger.Error($"[GameTime] Event '{eventID}' {phase} handler threw: {ex.Message}"); }
        }
    }

    private void SafeInvoke(Action<GameDateDetails>? handler, GameDateDetails date, string eventName)
    {
        if (handler == null) return;
        foreach (Delegate d in handler.GetInvocationList())
        {
            try { ((Action<GameDateDetails>)d)(date); }
            catch (Exception ex) { _logger.Error($"[GameTime] {eventName} handler threw: {ex.Message}"); }
        }
    }

    private GameDateDetails BuildDate(double totalSeconds, int totalDaysPassed)
    {
        if (_calendar.TotalDaysInYear <= 0)
            throw new CalendarConfigException("TotalDaysInYear is 0 — calendar configuration is invalid.");
        int year = _calendar.StartYear;
        int remDays = totalDaysPassed;
        if (_calendar.LeapYearInterval > 0)
        {
            int daysPerCycle = _calendar.TotalDaysInYear * _calendar.LeapYearInterval + 1;
            if (daysPerCycle > 0)
            {
                int cycles = remDays / daysPerCycle;
                remDays -= cycles * daysPerCycle;
                year += cycles * _calendar.LeapYearInterval;
            }
        }
        while (true)
        {
            bool isLeap = _calendar.LeapYearInterval > 0 && year % _calendar.LeapYearInterval == 0;
            int daysThisYear = _calendar.TotalDaysInYear + (isLeap ? 1 : 0);
            if (remDays < daysThisYear) break;
            remDays -= daysThisYear;
            year++;
        }
        bool currentLeap = _calendar.LeapYearInterval > 0 && year % _calendar.LeapYearInterval == 0;
        int month = 1;
        int dayOfMonth = remDays + 1;
        int daysIntoYear = 0;
        for (int i = 1; i <= _calendar.MonthCount; i++)
        {
            int len = _calendar.MonthLengths[i] + (currentLeap && i == _calendar.LeapMonth ? 1 : 0);
            if (dayOfMonth <= len) { month = i; break; }
            dayOfMonth -= len;
            daysIntoYear += len;
        }
        daysIntoYear += dayOfMonth - 1;
        double daySecs = totalSeconds % 86400.0;
        TimeSpan t = TimeSpan.FromSeconds(daySecs);
        int dowIdx = totalDaysPassed % _calendar.DaysPerWeek + 1;
        string holiday = _calendar.Holidays.TryGetValue($"{month}-{dayOfMonth}", out string? h) ? h : "None";
        return new GameDateDetails(year, month, _calendar.MonthNames[month], _calendar.MonthShorts[month], dayOfMonth, _calendar.DayNames[dowIdx], _calendar.DayShorts[dowIdx], holiday, currentLeap, $"{t.Hours:D2}:{t.Minutes:D2}:{t.Seconds:D2}", daySecs / 86400.0, (double)daysIntoYear / _calendar.TotalDaysInYear, dowIdx, t.Hours, t.Minutes, t.Seconds);
    }

    private GameDateDetails BuildDate(double totalSeconds) => BuildDate(totalSeconds, (int)(totalSeconds / 86400));

    private double GetTotalGameSeconds() => Math.Max(0, (DateTimeOffset.UtcNow - _liveDate).TotalSeconds * _timeMultiplier);

    private double ParseTimeMultiplier(string mode)
    {
        try
        {
            string lower = mode.ToLowerInvariant();
            string[] parts = lower.Split('_');
            if (parts.Length != 2) return 1.0;
            static double ParseUnit(string input)
            {
                int start = -1, end = -1;
                for (int i = 0; i < input.Length; i++)
                    if (char.IsDigit(input[i]) || input[i] == '.')
                    { if (start == -1) start = i; end = i; }
                if (start == -1) return 1.0;
                double val = double.Parse(input[start..(end + 1)], System.Globalization.CultureInfo.InvariantCulture);
                if (input.Contains('d')) return val * 86400.0;
                if (input.Contains('h')) return val * 3600.0;
                if (input.Contains('m')) return val * 60.0;
                return val;
            }
            double realSeconds = ParseUnit(parts[0]);
            double gameSeconds = ParseUnit(parts[1]);
            if (realSeconds <= 0)
            {
                _logger.Warn($"[GameTime] TimeMode '{mode}' has a zero or negative real-time unit — defaulting to 1.0.");
                return 1.0;
            }
            double multiplier = gameSeconds / realSeconds;
            _logger.Info($"[GameTime] TimeMode '{mode}' → multiplier {multiplier:F6} (1 real second = {multiplier:F4} game seconds).");
            return multiplier;
        }
        catch (Exception ex)
        {
            _logger.Warn($"[GameTime] Failed to parse TimeMode — defaulting to 1.0. ({ex.Message})");
            return 1.0;
        }
    }

    private static void AddToBucket(Dictionary<int, List<GameEvent>> buckets, int key, GameEvent ev)
    {
        if (!buckets.TryGetValue(key, out List<GameEvent>? list))
            buckets[key] = list = new List<GameEvent>();
        list.Add(ev);
    }

    private static void RemoveFromBucket(Dictionary<int, List<GameEvent>> buckets, int key, GameEvent ev)
    {
        if (buckets.TryGetValue(key, out List<GameEvent>? list))
            list.Remove(ev);
    }

    private static bool EventPassesFilter(GameEvent ev, GameDateDetails d) => ev.Repeat switch
    {
        GameEventRepeat.Once => true,
        GameEventRepeat.Daily => true,
        GameEventRepeat.Weekly => ev.DayOfWeek == 0 || d.DayOfWeekIndex == ev.DayOfWeek,
        GameEventRepeat.Monthly => ev.MonthDay == 0 || d.Day == ev.MonthDay,
        GameEventRepeat.Yearly => (ev.YearMonth == 0 || d.Month == ev.YearMonth) && (ev.YearDay == 0 || d.Day == ev.YearDay),
        _ => false
    };

    public void SaveAll()
    {
        _settings.Flush();
        _taskSettings.Flush();
    }
}