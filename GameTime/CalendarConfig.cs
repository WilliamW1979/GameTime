namespace GameTime;

public sealed class CalendarConfigException : Exception
{
    public CalendarConfigException(string message) : base(message) { }
}

public sealed class CalendarConfig
{
    public int StartYear { get; }
    public int MonthCount { get; }
    public int DaysPerWeek { get; }
    public int LeapYearInterval { get; }
    public int LeapMonth { get; }
    public string[] MonthNames { get; }
    public string[] MonthShorts { get; }
    public int[] MonthLengths { get; }
    public string[] DayNames { get; }
    public string[] DayShorts { get; }
    public int TotalDaysInYear { get; }
    public Dictionary<string, string> Holidays { get; }

    private CalendarConfig(int startYear, int monthCount, int daysPerWeek, int leapYearInterval, int leapMonth, string[] monthNames, string[] monthShorts, int[] monthLengths, string[] dayNames, string[] dayShorts, Dictionary<string, string> holidays)
    {
        StartYear = startYear;
        MonthCount = monthCount;
        DaysPerWeek = daysPerWeek;
        LeapYearInterval = leapYearInterval;
        LeapMonth = leapMonth;
        MonthNames = monthNames;
        MonthShorts = monthShorts;
        MonthLengths = monthLengths;
        DayNames = dayNames;
        DayShorts = dayShorts;
        Holidays = holidays;
        int total = 0;
        for (int i = 1; i <= monthCount; i++) total += monthLengths[i];
        TotalDaysInYear = total;
    }

    public static CalendarConfig Load(IGameTimeSettings settings)
    {
        List<string> errors = new List<string>();
        int startYear = settings.Get<int>("CalendarConfig", "StartYear", 0);
        int monthCount = settings.Get<int>("CalendarConfig", "MonthsInYear", 0);
        int daysPerWeek = settings.Get<int>("CalendarConfig", "DaysPerWeek", 0);
        int leapYearInterval = settings.Get<int>("CalendarConfig", "LeapYearInterval", 0);
        int leapMonth = settings.Get<int>("CalendarConfig", "LeapMonth", 0);
        if (monthCount <= 0)
            errors.Add("CalendarConfig.MonthsInYear must be a positive integer.");
        if (daysPerWeek <= 0)
            errors.Add("CalendarConfig.DaysPerWeek must be a positive integer.");
        if (errors.Count > 0)
            throw new CalendarConfigException("Calendar configuration is invalid:\n" + string.Join("\n", errors));
        string[] monthNames = new string[monthCount + 1];
        string[] monthShorts = new string[monthCount + 1];
        int[] monthLengths = new int[monthCount + 1];
        for (int i = 1; i <= monthCount; i++)
        {
            string key = i.ToString();
            string name = settings.Get("MonthNames", key, "");
            if (string.IsNullOrWhiteSpace(name))
                errors.Add($"MonthNames.{key} is missing.");
            else
            {
                monthNames[i] = name;
                monthShorts[i] = settings.Get("MonthNames_Short", key,
                    name.Length >= 3 ? name[..3] : name);
            }
            int len = settings.Get<int>("MonthLengths", key, 0);
            if (len <= 0)
                errors.Add($"MonthLengths.{key} must be a positive integer.");
            else
                monthLengths[i] = len;
        }
        string[] dayNames = new string[daysPerWeek + 1];
        string[] dayShorts = new string[daysPerWeek + 1];
        for (int i = 1; i <= daysPerWeek; i++)
        {
            string key = i.ToString();
            string name = settings.Get("DayNames", key, "");
            if (string.IsNullOrWhiteSpace(name))
                errors.Add($"DayNames.{key} is missing.");
            else
            {
                dayNames[i] = name;
                dayShorts[i] = settings.Get("DayNames_Short", key, name.Length >= 3 ? name[..3] : name);
            }
        }
        if (leapYearInterval > 0 && (leapMonth < 1 || leapMonth > monthCount))
            errors.Add($"CalendarConfig.LeapMonth must be between 1 and {monthCount} when LeapYearInterval is set.");
        if (errors.Count > 0)
            throw new CalendarConfigException("Calendar configuration is invalid:\n" + string.Join("\n", errors));
        Dictionary<string, string> holidays = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string key in settings.GetSettings("Holidays"))
        {
            string val = settings.Get("Holidays", key, "");
            if (!string.IsNullOrWhiteSpace(val))
                holidays[key] = val;
        }
        return new CalendarConfig(startYear, monthCount, daysPerWeek, leapYearInterval, leapMonth, monthNames, monthShorts, monthLengths, dayNames, dayShorts, holidays);
    }

    public static void WriteDefaults(IGameTimeSettings settings)
    {
        settings.Set("Server", "TimeMode", "1h_1h");
        settings.Set("CalendarConfig", "StartYear", "0");
        settings.Set("CalendarConfig", "MonthsInYear", "12");
        settings.Set("CalendarConfig", "DaysPerWeek", "7");
        settings.Set("CalendarConfig", "LeapYearInterval", "4");
        settings.Set("CalendarConfig", "LeapMonth", "2");
        string[] months = { "", "January","February","March","April","May","June", "July","August","September","October","November","December" };
        int[] lens = { 0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
        for (int i = 1; i <= 12; i++)
        {
            settings.Set("MonthNames", i.ToString(), months[i]);
            settings.Set("MonthLengths", i.ToString(), lens[i].ToString());
        }
        string[] days = { "", "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        for (int i = 1; i <= 7; i++)
            settings.Set("DayNames", i.ToString(), days[i]);
        settings.Flush();
    }
}