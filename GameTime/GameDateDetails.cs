namespace GameTime;

public record GameDateDetails(int Year, int Month, string MonthName, string MonthShort, int Day, string DayName, string DayShort, string Holiday, bool IsLeapYear, string TimeFormatted, double DayProgress, double YearProgress, int DayOfWeekIndex, int Hour, int Minute, int Second);