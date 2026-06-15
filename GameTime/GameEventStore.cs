using System.Text;

namespace GameTime;

public abstract class GameEventStore
{
    public virtual void Save(PersistedEvent ev) { }
    public virtual void Delete(string eventID) { }
    public virtual List<PersistedEvent> LoadAll() => new List<PersistedEvent>();
}

public sealed class BinaryGameEventStore : GameEventStore
{
    private static readonly byte[] Magic = { 0x47, 0x54, 0x45, 0x56 };
    private const byte CurrentVersion = 0x01;
    private readonly string _path;
    private readonly IGameTimeLogger _logger;
    private readonly object _fileLock = new object();

    public BinaryGameEventStore(string path = "GameEvents.bin", IGameTimeLogger? logger = null)
    {
        _path = path;
        _logger = logger ?? new ConsoleGameTimeLogger();
    }

    public override void Save(PersistedEvent ev)
    {
        lock (_fileLock)
        {
            List<PersistedEvent> all = LoadAllInternal();
            int idx = all.FindIndex(e => string.Equals(e.EventID, ev.EventID, StringComparison.Ordinal));
            if (idx >= 0) all[idx] = ev;
            else all.Add(ev);
            WriteAll(all);
        }
    }

    public override void Delete(string eventID)
    {
        lock (_fileLock)
        {
            List<PersistedEvent> all = LoadAllInternal();
            int removed = all.RemoveAll(e => string.Equals(e.EventID, eventID, StringComparison.Ordinal));
            if (removed > 0) WriteAll(all);
        }
    }

    public override List<PersistedEvent> LoadAll()
    {
        lock (_fileLock)
            return LoadAllInternal();
    }

    private List<PersistedEvent> LoadAllInternal()
    {
        List<PersistedEvent> results = new List<PersistedEvent>();
        if (!File.Exists(_path))
            return results;
        try
        {
            using FileStream fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using BinaryReader br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: false);
            if (fs.Length < 5)
            {
                _logger.Warn($"[GameEventStore] File '{_path}' is too short to contain a valid header — ignoring.");
                return results;
            }
            byte[] magic = br.ReadBytes(4);
            if (magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
            {
                _logger.Warn($"[GameEventStore] File '{_path}' has an invalid magic header — ignoring.");
                return results;
            }
            byte version = br.ReadByte();
            if (version != CurrentVersion)
            {
                _logger.Warn($"[GameEventStore] File '{_path}' has unsupported version {version} (expected {CurrentVersion}) — ignoring.");
                return results;
            }
            while (fs.Position < fs.Length)
            {
                long recordStart = fs.Position;
                try
                {
                    int idLen = br.ReadInt32();
                    if (idLen <= 0 || idLen > 4096)
                        throw new InvalidDataException($"EventID length {idLen} is out of valid range.");
                    string eventID = Encoding.UTF8.GetString(br.ReadBytes(idLen));
                    int startMinute = br.ReadInt32();
                    int endMinute = br.ReadInt32();
                    byte repeatByte = br.ReadByte();
                    int dayOfWeek = br.ReadInt32();
                    int monthDay = br.ReadInt32();
                    int yearMonth = br.ReadInt32();
                    int yearDay = br.ReadInt32();
                    int payloadLen = br.ReadInt32();
                    byte[] payload = payloadLen > 0 ? br.ReadBytes(payloadLen) : Array.Empty<byte>();
                    if (!Enum.IsDefined(typeof(GameEventRepeat), repeatByte))
                        throw new InvalidDataException($"Unknown repeat value {repeatByte} for event '{eventID}'.");
                    results.Add(new PersistedEvent(
                    eventID, startMinute, endMinute,
                    (GameEventRepeat)repeatByte,
                    dayOfWeek, monthDay, yearMonth, yearDay,
                    payload));
                }
                catch (Exception ex)
                {
                    _logger.Warn($"[GameEventStore] Corrupt record at offset {recordStart} in '{_path}' — skipping. ({ex.Message})");
                    break;
                }
            }
        }
        catch (Exception ex) { _logger.Error($"[GameEventStore] Failed to read '{_path}': {ex.Message}"); }
        return results;
    }

    private void WriteAll(List<PersistedEvent> events)
    {
        try
        {
            using FileStream fs = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.None);
            using BinaryWriter bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false);
            bw.Write(Magic);
            bw.Write(CurrentVersion);
            foreach (PersistedEvent ev in events)
            {
                byte[] idBytes = Encoding.UTF8.GetBytes(ev.EventID);
                bw.Write(idBytes.Length);
                bw.Write(idBytes);
                bw.Write(ev.StartMinute);
                bw.Write(ev.EndMinute);
                bw.Write((byte)ev.Repeat);
                bw.Write(ev.DayOfWeek);
                bw.Write(ev.MonthDay);
                bw.Write(ev.YearMonth);
                bw.Write(ev.YearDay);
                bw.Write(ev.Payload.Length);
                if (ev.Payload.Length > 0)
                    bw.Write(ev.Payload);
            }
        }
        catch (Exception ex) { _logger.Error($"[GameEventStore] Failed to write '{_path}': {ex.Message}"); }
    }
}