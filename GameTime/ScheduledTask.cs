namespace GameTime;

public sealed class ScheduledTask
{
    public string TaskID { get; }
    public int Hour { get; }
    public int Minute { get; }
    public Action<GameDateDetails> TaskAction { get; }

    public ScheduledTask(string taskID, int hour, int minute, Action<GameDateDetails> taskAction)
    {
        TaskID = taskID;
        Hour = hour;
        Minute = minute;
        TaskAction = taskAction;
    }
}
