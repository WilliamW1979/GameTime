namespace GameTime;

public sealed class PersistedEvent
{
    public string EventID { get; }
    public int StartMinute { get; }
    public int EndMinute { get; }
    public GameEventRepeat Repeat { get; }
    public int DayOfWeek { get; }
    public int MonthDay { get; }
    public int YearMonth { get; }
    public int YearDay { get; }
    public byte[] Payload { get; }

    public PersistedEvent(string eventID, int startMinute, int endMinute, GameEventRepeat repeat, int dayOfWeek = 0, int monthDay = 0, int yearMonth = 0, int yearDay = 0, byte[]? payload = null)
    {
        EventID = eventID;
        StartMinute = startMinute;
        EndMinute = endMinute;
        Repeat = repeat;
        DayOfWeek = dayOfWeek;
        MonthDay = monthDay;
        YearMonth = yearMonth;
        YearDay = yearDay;
        Payload = payload ?? Array.Empty<byte>();
    }
}