namespace GameTime;

public enum GameEventRepeat : byte
{
    Once = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
    Yearly = 4
}

public sealed class GameEventArgs
{
    public string EventID { get; }
    public GameDateDetails Date { get; }
    public bool IsStart { get; }
    public byte[] Payload { get; }

    public GameEventArgs(string eventID, GameDateDetails date, bool isStart, byte[] payload)
    {
        EventID = eventID;
        Date = date;
        IsStart = isStart;
        Payload = payload;
    }
}

public sealed class GameEvent
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
    public bool IsActive { get; internal set; }

    internal Action<GameEventArgs>? OnStart;
    internal Action<GameEventArgs>? OnEnd;

    public GameEvent(string eventID, int startMinute, int endMinute, GameEventRepeat repeat, int dayOfWeek = 0, int monthDay = 0, int yearMonth = 0, int yearDay = 0, byte[]? payload = null)
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

    public void SubscribeStart(Action<GameEventArgs> handler) => OnStart += handler;
    public void UnsubscribeStart(Action<GameEventArgs> handler) => OnStart -= handler;
    public void SubscribeEnd(Action<GameEventArgs> handler) => OnEnd += handler;
    public void UnsubscribeEnd(Action<GameEventArgs> handler) => OnEnd -= handler;
}