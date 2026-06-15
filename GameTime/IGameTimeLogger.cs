namespace GameTime;

public interface IGameTimeLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

public sealed class ConsoleGameTimeLogger : IGameTimeLogger
{
    public void Info(string message) => Console.WriteLine($"[INFO] {message}");
    public void Warn(string message) => Console.WriteLine($"[WARN] {message}");
    public void Error(string message) => Console.Error.WriteLine($"[ERROR] {message}");
}

public sealed class NullGameTimeLogger : IGameTimeLogger
{
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message) { }
}