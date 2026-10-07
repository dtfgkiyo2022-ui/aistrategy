using System.Globalization;
using System.Text;
using System.Text.Json;
using Rts.Contracts;

namespace Rts.Headless.Cli;

public sealed class HumanOrderRecord
{
    public long Tick { get; set; }
    public string Kind { get; set; } = "";
    public HumanScope Target { get; set; } = new();
    public HumanGoal Goal { get; set; } = new();
    public int Priority { get; set; }
    public int AllowedLossPermille { get; set; }
    public HumanEnd End { get; set; } = new();
    public int ReservePermille { get; set; }
    public long ValidUntilTick { get; set; }
    public int MaxObservationAgeTicks { get; set; }
    public string Expire { get; set; } = "None";
    public ulong Sequence { get; set; }

    internal static HumanOrderRecord From(PolicyOrder order, long tick, ulong sequence)
    {
        return new HumanOrderRecord
        {
            Tick = tick,
            Kind = order.Kind.ToString(),
            Target = new HumanScope { FactionId = order.Target.FactionId, Kind = order.Target.Kind.ToString(), Id = order.Target.Id },
            Goal = new HumanGoal
            {
                Kind = order.Goal.Kind.ToString(), Id = order.Goal.Id,
                Point = new HumanPoint { X = (decimal)order.Goal.Point.X.Raw / 65536m, Z = (decimal)order.Goal.Point.Z.Raw / 65536m }
            },
            Priority = order.Priority,
            AllowedLossPermille = order.AllowedLoss.Permille,
            End = new HumanEnd { Kind = order.End.Kind.ToString(), Tick = order.End.Tick },
            ReservePermille = order.ReservePermille,
            ValidUntilTick = order.Expiration.ValidUntilTick,
            MaxObservationAgeTicks = order.Expiration.MaxObservationAgeTicks,
            Expire = order.Expiration.Flags.ToString(),
            Sequence = sequence
        };
    }

    internal UserPolicyIntent ToIntent(uint faction)
    {
        if (Target == null || Goal == null || End == null) throw new InvalidDataException("human-orders の対象・目標・終了条件がありません。");
        if (Target.FactionId != faction) throw new InvalidDataException("human-orders の陣営が指定陣営と一致しません。");
        if (!Enum.TryParse(Target.Kind, true, out ScopeKind scopeKind) || !Enum.IsDefined(scopeKind)) throw new InvalidDataException("対象範囲が不明です: " + Target.Kind);
        if (!Enum.TryParse(Kind, true, out PolicyKind policyKind) || !Enum.IsDefined(policyKind)) throw new InvalidDataException("方針が不明です: " + Kind);
        if (!Enum.TryParse(Goal.Kind, true, out GoalKind goalKind) || !Enum.IsDefined(goalKind)) throw new InvalidDataException("目標が不明です: " + Goal.Kind);
        if (!Enum.TryParse(End.Kind, true, out EndKind endKind) || !Enum.IsDefined(endKind)) throw new InvalidDataException("終了条件が不明です: " + End.Kind);
        if (!Enum.TryParse(Expire ?? "None", true, out ExpireFlags expireFlags)) throw new InvalidDataException("失効条件が不明です: " + Expire);
        if (Priority < 0 || Priority > 100 || AllowedLossPermille < 0 || AllowedLossPermille > 1000 || ReservePermille < 0 || ReservePermille > 1000 || MaxObservationAgeTicks < 0)
            throw new InvalidDataException("human-orders の値が範囲外です。");
        if (Goal.Point == null) Goal.Point = new HumanPoint();
        return new UserPolicyIntent(Sequence == 0 ? 1UL : Sequence,
            new ScopeKey(faction, scopeKind, Target.Id), policyKind,
            new PolicyGoal(goalKind, Goal.Id, new SimPoint(ToFix(Goal.Point.X), ToFix(Goal.Point.Z))),
            (byte)Priority, new LossBudget((ushort)AllowedLossPermille),
            new EndCondition(endKind, End.Tick), (ushort)ReservePermille,
            new Expiration(ValidUntilTick, MaxObservationAgeTicks, expireFlags));
    }

    private static Fix64 ToFix(decimal value) => Fix64.FromRaw(checked((long)decimal.Round(value * 65536m, 0, MidpointRounding.ToEven)));
}

public sealed class HumanScope { public uint FactionId { get; set; } public string Kind { get; set; } = ""; public uint Id { get; set; } }
public sealed class HumanGoal { public string Kind { get; set; } = "None"; public uint Id { get; set; } public HumanPoint Point { get; set; } = new(); }
public sealed class HumanPoint { public decimal X { get; set; } public decimal Z { get; set; } }
public sealed class HumanEnd { public string Kind { get; set; } = "UntilReplaced"; public long Tick { get; set; } }

internal static class HumanOrderJson
{
    internal static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

    internal static List<HumanOrderRecord> Read(string path)
    {
        var result = new List<HumanOrderRecord>();
        foreach (string line in File.ReadLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var item = JsonSerializer.Deserialize<HumanOrderRecord>(line, Options) ?? throw new InvalidDataException("human-orders の行が空です。");
            result.Add(item);
        }
        return result.OrderBy(x => x.Tick).ThenBy(x => x.Sequence).ToList();
    }

    internal static void Write(string path, IEnumerable<HumanOrderRecord> records)
    {
        string full = Path.GetFullPath(path);
        string parent = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        using var writer = new StreamWriter(full, false, new UTF8Encoding(false));
        foreach (var record in records) writer.WriteLine(JsonSerializer.Serialize(record, Options));
    }
}

internal static class HumanOrdersCommand
{
    internal static int Run(Dictionary<string, string> options)
    {
        string pack = Required(options, "--pack");
        uint faction = uint.Parse(Required(options, "--faction"), CultureInfo.InvariantCulture);
        if (faction < 1 || faction > 2) throw new InvalidDataException("--faction は1または2です。");
        string replayPath = File.Exists(pack) ? pack : Path.Combine(pack, "replay.rpl");
        if (!File.Exists(replayPath)) throw new FileNotFoundException("replay.rpl がありません。", replayPath);
        string output = Required(options, "--out");
        var records = new List<HumanOrderRecord>();
        var seen = new HashSet<ulong>();
        int economyInputs = 0;
        using (var stream = File.OpenRead(replayPath))
        using (var reader = new Rts.Replay.ReplayReader(stream))
        {
            Rts.Replay.ReplayRecord record;
            while ((record = reader.Read()) != null)
            {
                if (record.Kind != Rts.Replay.ReplayRecordKind.Input) continue;
                ScheduledInput input = Rts.Replay.InputBinary.Decode(record.Payload);
                if (input.Kind == InputKind.Economy) { economyInputs++; continue; }
                foreach (var order in input.Orders)
                {
                    if (order.Source != CommandSource.Human || order.Target.FactionId != faction || !seen.Add(order.CommandId)) continue;
                    records.Add(HumanOrderRecord.From(order, input.AcceptedTick, input.IssuerSequence));
                }
            }
        }
        HumanOrderJson.Write(output, records.OrderBy(x => x.Tick).ThenBy(x => x.Sequence));
        Console.WriteLine("human-orders: orders=" + records.Count.ToString(CultureInfo.InvariantCulture) + " out=" + Path.GetFullPath(output));
        if (economyInputs != 0)
            Console.Error.WriteLine("human-orders: 内政命令は replay.rpl に " + economyInputs.ToString(CultureInfo.InvariantCulture) + " 件ありましたが、入力形式に CommandSource がないため人の命令として識別できませんでした。");
        return 0;
    }

    private static string Required(Dictionary<string, string> options, string key) => options.TryGetValue(key, out var value) ? value : throw new InvalidDataException("Missing " + key);
}
