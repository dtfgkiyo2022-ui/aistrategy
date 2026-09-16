using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Rts.Application;
using Rts.Contracts;
using Rts.Replay;
using Rts.Simulation;

namespace Rts.Headless.Cli;

internal static class JsonInput
{
    internal static readonly JsonSerializerOptions Options = Create();
    private static JsonSerializerOptions Create()
    {
        var o=new JsonSerializerOptions { IncludeFields=true,PropertyNameCaseInsensitive=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow };
        o.Converters.Add(new FixedConverter());
        o.Converters.Add(new ValueConverter<SimPoint>(e=>new SimPoint(Get<Fix64>(e,"x",o),Get<Fix64>(e,"z",o))));
        o.Converters.Add(new ValueConverter<ScopeKey>(e=>new ScopeKey(Get<uint>(e,"factionId",o),Get<ScopeKind>(e,"kind",o),Get<uint>(e,"id",o))));
        o.Converters.Add(new ValueConverter<PolicyGoal>(e=>new PolicyGoal(Get<GoalKind>(e,"kind",o),Get<uint>(e,"id",o),Get<SimPoint>(e,"point",o))));
        o.Converters.Add(new ValueConverter<LossBudget>(e=>new LossBudget(Get<ushort>(e,"permille",o))));
        o.Converters.Add(new ValueConverter<EndCondition>(e=>new EndCondition(Get<EndKind>(e,"kind",o),Get<long>(e,"tick",o))));
        o.Converters.Add(new ValueConverter<PolicyVersion>(e=>new PolicyVersion(Get<ScopeKey>(e,"scope",o),Get<ulong>(e,"revision",o))));
        o.Converters.Add(new ValueConverter<Expiration>(e=>new Expiration(Get<long>(e,"validUntilTick",o),Get<int>(e,"maxObservationAgeTicks",o),Get<ExpireFlags>(e,"flags",o))));
        o.Converters.Add(new ValueConverter<ScheduledInput>(e => new ScheduledInput(
            Get<ulong>(e,"logIndex",o), Get<InputKind>(e,"kind",o), Get<long>(e,"acceptedTick",o), Get<long>(e,"applyTick",o),
            Get<ulong>(e,"requestId",o), Get<ulong>(e,"issuerSequence",o), Get<PolicyOrder[]>(e,"orders",o),
            e.TryGetProperty("deadlineTick",out var deadline) ? deadline.GetInt64() : long.MaxValue, Get<ReasonCode>(e,"resolutionReason",o))));
        return o;
    }
    private static T Get<T>(JsonElement e,string name,JsonSerializerOptions o)=>e.TryGetProperty(name,out var value)?value.Deserialize<T>(o):default;
    private sealed class ValueConverter<T>(Func<JsonElement,T> create):JsonConverter<T>
    {
        public override T Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
        { using var doc=JsonDocument.ParseValue(ref reader); return create(doc.RootElement); }
        public override void Write(Utf8JsonWriter w,T value,JsonSerializerOptions options)=>throw new NotSupportedException();
    }
    internal static ScenarioDefinition Scenario(string path)
    {
        var root=JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty scenario.");
        Rename(root);
        return root.Deserialize<ScenarioDefinition>(Options) ?? throw new InvalidDataException("Null scenario.");
    }
    private static void Rename(JsonNode node)
    {
        if(node is JsonObject obj)
        {
            foreach(var pair in obj.ToArray())
            {
                string name=pair.Key switch { "positionMeters"=>"position", "coreRadiusMeters"=>"coreRadius", "ownedObjectiveVisionMeters"=>"ownedObjectiveVision", "captureRadiusMeters"=>"captureRadius", "speedMetersPerSecond"=>"speed", "visionMeters"=>"vision", "rangeMeters"=>"range", _=>pair.Key };
                if(name!=pair.Key) { obj.Remove(pair.Key); obj.Add(name,pair.Value); }
                if(pair.Value!=null)Rename(pair.Value);
            }
        }
        else if(node is JsonArray arr)foreach(var item in arr)if(item!=null)Rename(item);
    }
    private sealed class FixedConverter:JsonConverter<Fix64>
    {
        public override Fix64 Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
        {
            if(reader.TokenType==JsonTokenType.Number)return Fix64.FromInt(reader.GetInt32());
            using var doc=JsonDocument.ParseValue(ref reader); var e=doc.RootElement;
            if(e.TryGetProperty("raw",out var raw) && e.EnumerateObject().Count()==1)return Fix64.FromRaw(raw.GetInt64());
            if(e.TryGetProperty("numerator",out var n) && e.TryGetProperty("denominator",out var d) && e.EnumerateObject().Count()==2)
            {
                long denominator=d.GetInt64(); if(denominator==0)throw new InvalidDataException("Zero denominator.");
                return Fix64.FromRaw(checked((long)(new System.Numerics.BigInteger(n.GetInt64())*65536/denominator)));
            }
            throw new InvalidDataException("Fixed value needs integer, {raw}, or {numerator,denominator}.");
        }
        public override void Write(Utf8JsonWriter w,Fix64 value,JsonSerializerOptions options) { w.WriteStartObject(); w.WriteNumber("raw",value.Raw); w.WriteEndObject(); }
    }
}
internal static class BuildInfo
{
    internal static string Root()
    {
        for(var d=new DirectoryInfo(Environment.CurrentDirectory);d!=null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"Headless/Rts.Headless.slnx")))return d.FullName;
        throw new InvalidDataException("Run from inside the source repository to identify the pure C# source set.");
    }
    internal static BuildIdentity Current()
    {
        string root=Root(); var assembly=typeof(BuildInfo).Assembly;
        var paths=assembly.GetManifestResourceNames().Where(p=>p.StartsWith("Source/",StringComparison.Ordinal));
        byte[] source=ReplayBinary.Pack(w=>
        {
            foreach(string path in paths.OrderBy(p=>p.Replace('\\','/'),StringComparer.Ordinal))
            {
                ReplayBinary.Text(w,path.Substring("Source/".Length).Replace('\\','/'));
                // Normalize checkout line endings so Windows and Unix identify the same sources.
                using var reader=new StreamReader(assembly.GetManifestResourceStream(path) ?? throw new InvalidDataException("Missing source resource."));
                byte[] text=Encoding.UTF8.GetBytes(reader.ReadToEnd().Replace("\r\n","\n")); w.Write((uint)text.Length); w.Write(text);
            }
        });
        string version=Path.Combine(root,"UnityProject/ProjectSettings/ProjectVersion.txt"), locks=Path.Combine(root,"UnityProject/Packages/packages-lock.json");
        return new BuildIdentity { Commit=Git(root,"rev-parse","HEAD").Trim(),Dirty=Git(root,"status","--porcelain").Length!=0,SourceHash=ReplayBinary.Hex(ReplayBinary.Hash(source)),EditorVersion=File.Exists(version)?File.ReadAllText(version).Trim():"unavailable",Backend="dotnet-"+Environment.Version,PackageLockHash=File.Exists(locks)?ReplayBinary.Hex(ReplayBinary.Hash(Encoding.UTF8.GetBytes(File.ReadAllText(locks).Replace("\r\n","\n")))):"unavailable" };
    }
    private static string Git(string root,params string[] args)
    {
        var start=new ProcessStartInfo("git") { WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true };
        start.ArgumentList.Add("-c"); start.ArgumentList.Add("safe.directory="+root.Replace('\\','/')); foreach(string a in args)start.ArgumentList.Add(a);
        using var p=Process.Start(start) ?? throw new IOException("Cannot start git."); string output=p.StandardOutput.ReadToEnd(); string error=p.StandardError.ReadToEnd(); p.WaitForExit(); if(p.ExitCode!=0)throw new IOException(error); return output;
    }
}
internal sealed class HashHeader
{
    public int SchemaVersion { get; set; }=1;
    public string ReplayHash { get; set; }="";
    public string ReplayPath { get; set; }="";
    public BuildIdentity Build { get; set; }=new();
}
internal sealed class HashRow
{
    public long Tick { get; set; }
    public string StateHash { get; set; }="";
    public string EventHash { get; set; }="";
}
internal static class Program
{
    private static readonly JsonSerializerOptions Json=new() { IncludeFields=true };
    public static int Main(string[] args)
    {
        try
        {
            if(args.Length==0)throw new InvalidDataException("Commands: record, replay, compare, bench, analyze.");
            var options=new Dictionary<string,string>(StringComparer.Ordinal);
            for(int i=1;i<args.Length;i++)
            {
                string key=args[i]; if(!key.StartsWith("--",StringComparison.Ordinal))throw new InvalidDataException("Expected option.");
                string value=key=="--allow-build-mismatch"?"true":(++i<args.Length?args[i]:throw new InvalidDataException("Missing option value."));
                if(!options.TryAdd(key,value))throw new InvalidDataException("Duplicate option "+key);
            }
            string Required(string key)=>options.TryGetValue(key,out var value)?value:throw new InvalidDataException("Missing "+key);
            string[] allowed=args[0] switch { "analyze"=>new[]{"--in","--out","--allow-build-mismatch","--scenario","--ticks","--west-preset","--east-preset"}, "bench"=>new[]{"--scenario","--ticks","--warmup","--out","--record","--inputs"},"record"=>new[]{"--scenario","--out","--ticks","--inputs","--west-preset","--east-preset","--enemy-preset","--ai-delay","--ai-profile"},"replay"=>new[]{"--in","--hash-out","--dump-dir","--allow-build-mismatch"},"compare"=>new[]{"--left","--right","--replay","--allow-build-mismatch"},_=>throw new InvalidDataException("Unknown command.") };
            if(options.Keys.Except(allowed).Any())throw new InvalidDataException("Unknown option.");
            var build=BuildInfo.Current();
            if(args[0]=="bench") return BenchmarkCommand.Run(options, build);
            if(args[0]=="analyze") return AnalyzeCommand.Run(options, build);
            if(args[0]=="record")
            {
                var scenario=JsonInput.Scenario(Required("--scenario"));
                var inputs=options.TryGetValue("--inputs",out var path)?JsonSerializer.Deserialize<ScheduledInput[]>(File.ReadAllText(path),JsonInput.Options) ?? throw new InvalidDataException("Null inputs."):Array.Empty<ScheduledInput>();
                if (options.ContainsKey("--enemy-preset") && options.ContainsKey("--east-preset")) throw new InvalidDataException("--enemy-preset is an alias for --east-preset; use only one.");
                string eastPreset = options.TryGetValue("--east-preset", out var east) ? east : options.GetValueOrDefault("--enemy-preset");
                var aiProfile = AiTimingProfile.Parse(options.GetValueOrDefault("--ai-profile") ?? "default");
                int aiDelay = options.TryGetValue("--ai-delay", out var delay) ? int.Parse(delay, System.Globalization.CultureInfo.InvariantCulture) : -1;
                if (options.ContainsKey("--ai-profile") && aiDelay == -1) throw new InvalidDataException("--ai-profile requires --ai-delay.");
                if (options.ContainsKey("--ai-delay"))
                {
                    if (inputs.Length != 0 || options.ContainsKey("--west-preset") || eastPreset != null)
                        throw new InvalidDataException("--ai-delay runs autonomous auto vs auto; use without inputs or presets.");
                    inputs = PolicyPresets.DelayedInputs(scenario, long.Parse(Required("--ticks"), System.Globalization.CultureInfo.InvariantCulture), aiDelay, aiProfile);
                }
                if (options.ContainsKey("--west-preset") || eastPreset != null)
                {
                    if (inputs.Length != 0) throw new InvalidDataException("Use either presets or --inputs; preset proposals can also be included in an input log.");
                    inputs = PolicyPresets.RecordedInputs(scenario, options.GetValueOrDefault("--west-preset") ?? "none", eastPreset ?? "none", long.Parse(Required("--ticks"),System.Globalization.CultureInfo.InvariantCulture));
                }
                long ticks=long.Parse(Required("--ticks"),System.Globalization.CultureInfo.InvariantCulture);
                using var output=File.Create(Required("--out")); var result=ReplayRunner.Record(output,scenario,inputs,ticks,build,null,options.GetValueOrDefault("--west-preset") ?? "none",eastPreset ?? "none",aiDelay,aiProfile.Name);
                Console.WriteLine("Recorded S0..S"+result.LastTick); return result.IsFault?4:0;
            }
            if(args[0]=="replay")return Replay(Required("--in"),Required("--hash-out"),options.GetValueOrDefault("--dump-dir"),build,options.ContainsKey("--allow-build-mismatch"));
            return Compare(Required("--left"),Required("--right"),Required("--replay"),build,options.ContainsKey("--allow-build-mismatch"));
        }
        catch(Exception e) when(e is InvalidDataException || e is EndOfStreamException || e is JsonException || e is ArgumentException || e is FormatException || e is OverflowException)
        { Console.Error.WriteLine("Format/version: "+e.Message); return 3; }
        catch(Exception e) { Console.Error.WriteLine("Fault: "+e.Message); return 4; }
    }
    private static string FileHash(string path)
    { using var s=File.OpenRead(path); return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(s)); }
    private static int Replay(string input,string hashPath,string dump,BuildIdentity build,bool allow)
    {
        if(Path.GetFullPath(input)==Path.GetFullPath(hashPath))throw new InvalidDataException("Input and output must differ.");
        var header=new HashHeader { ReplayHash=FileHash(input),ReplayPath=Path.GetFullPath(input),Build=build };
        using var hashes=new StreamWriter(hashPath,false,new UTF8Encoding(false)); hashes.WriteLine(JsonSerializer.Serialize(header,Json));
        using var states=new BinaryWriter(File.Create(hashPath+".states"));
        if(dump!=null)Directory.CreateDirectory(dump);
        using var source=File.OpenRead(input);
        var result=ReplayRunner.Replay(source,build,(state,hash,events)=>
        {
            hashes.WriteLine(JsonSerializer.Serialize(new HashRow { Tick=state.Tick,StateHash=ReplayBinary.Hex(hash),EventHash=ReplayBinary.Hex(events) },Json));
            states.Write(state.Tick); states.Write((uint)state.CanonicalState.Count); states.Write(state.CanonicalState.ToArray());
            if(dump!=null && state.Tick%100==0)Dump(dump,"tick-"+state.Tick,state);
        },allow);
        Console.WriteLine(result.FirstMismatchTick.HasValue?"Mismatch at tick "+result.FirstMismatchTick:"Replay matched S0..S"+result.LastTick);
        return result.IsFault?4:result.FirstMismatchTick.HasValue?2:0;
    }
    private static void Dump(string dir,string name,DiagnosticState state)
    {
        Directory.CreateDirectory(dir); File.WriteAllBytes(Path.Combine(dir,name+".state"),state.CanonicalState.ToArray());
        File.WriteAllLines(Path.Combine(dir,name+".txt"),DiagnosticComparison.Fields(state).Select(p=>p.Key+"="+p.Value));
    }
    private static HashHeader Header(StreamReader r)=>JsonSerializer.Deserialize<HashHeader>(r.ReadLine() ?? throw new InvalidDataException("Missing hashes header."),Json) ?? throw new InvalidDataException("Null header.");
    private static HashRow Row(StreamReader r,long expected)
    {
        string line=r.ReadLine(); if(line==null)return null;
        var row=JsonSerializer.Deserialize<HashRow>(line,Json) ?? throw new InvalidDataException("Null hash row.");
        if(row.Tick<expected || row.StateHash.Length!=64 || row.EventHash.Length!=64 || !row.StateHash.All(Uri.IsHexDigit) || !row.EventHash.All(Uri.IsHexDigit))throw new InvalidDataException("Invalid hashes row.");
        return row;
    }
    private static int Compare(string left,string right,string replay,BuildIdentity build,bool allow)
    {
        using var a=new StreamReader(left); using var b=new StreamReader(right); var ah=Header(a); var bh=Header(b);
        if(ah.SchemaVersion!=1 || bh.SchemaVersion!=1)throw new InvalidDataException("Hashes schema mismatch.");
        string replayHash=FileHash(replay);
        if(ah.ReplayHash!=replayHash && bh.ReplayHash!=replayHash)throw new InvalidDataException("Neither run belongs to --replay.");
        long tick=0; HashRow ar,br,previousA=null,previousB=null;
        while(true)
        {
            ar=Row(a,tick); br=Row(b,tick);
            if(ar==null && br==null)
            {
                if(tick==0)throw new InvalidDataException("No tick hashes.");
                using var stream=File.OpenRead(replay);
                var result=ReplayRunner.Replay(stream,build,null,allow);
                if(result.IsFault)return 4;
                if(result.LastTick+1!=tick) { Console.WriteLine("First mismatch tick="+Math.Min(tick,result.LastTick+1)+" (missing/extra tick)"); return 2; }
                if(result.FirstMismatchTick.HasValue) { Console.WriteLine("Replay mismatch tick="+result.FirstMismatchTick); return 2; }
                Console.WriteLine("All "+tick+" tick hashes and event hashes match."); return 0;
            }
            if(ar==null || br==null || ar.Tick!=tick || br.Tick!=tick || ar.StateHash!=br.StateHash || ar.EventHash!=br.EventHash)break;
            previousA=ar; previousB=br; tick++;
        }
        Console.WriteLine("First mismatch tick="+tick+"\nBuild A: "+ah.Build+"\nBuild B: "+bh.Build);
        string dir=left+".diff";
        Rerun(ah,"left",replay,replayHash,tick,dir,build,allow);
        Rerun(bh,"right",replay,replayHash,tick,dir,build,allow);
        var priorLeft=ReadState(left,previousA,tick-1); var priorRight=ReadState(right,previousB,tick-1);
        if(priorLeft!=null)Dump(dir,"left-"+(tick-1),priorLeft);
        if(priorRight!=null)Dump(dir,"right-"+(tick-1),priorRight);
        var ls=ReadState(left,ar,tick); var rs=ReadState(right,br,tick);
        if(ls!=null)Dump(dir,"left-"+tick,ls); if(rs!=null)Dump(dir,"right-"+tick,rs);
        if(ls!=null && rs!=null)
        {
            var difference=DiagnosticComparison.First(ls,rs);
            Console.WriteLine(difference?.ToString() ?? "EventHash: left="+ar.EventHash+" right="+br.EventHash);
            foreach(var pair in DiagnosticComparison.Fields(ls).Where(p=>p.Key=="Inputs.Cursor" || p.Key.StartsWith("CombatRandom.") || p.Key.StartsWith("AiRandom.")))Console.WriteLine("A "+pair.Key+"="+pair.Value);
            foreach(var pair in DiagnosticComparison.Fields(rs).Where(p=>p.Key=="Inputs.Cursor" || p.Key.StartsWith("CombatRandom.") || p.Key.StartsWith("AiRandom.")))Console.WriteLine("B "+pair.Key+"="+pair.Value);
        }
        else Console.WriteLine("tick は判明、当tickの期待状態は不足（欠落tickまたは .states 不在）。元の実行で診断を再収集してください。");
        return 2;
    }
    private sealed class CapturedTick:Exception { }
    private static void Rerun(HashHeader header,string side,string supplied,string suppliedHash,long tick,string dir,BuildIdentity build,bool allow)
    {
        string path=header.ReplayHash==suppliedHash?supplied:header.ReplayPath;
        if(!File.Exists(path) || FileHash(path)!=header.ReplayHash)
        { Console.WriteLine(side+": 元の再生ファイルがないため、保存済み診断状態を使用します。"); return; }
        if(header.Build.SourceHash!=build.SourceHash && !allow)
        { Console.WriteLine(side+": 元のビルドを再実行できないため、保存済み診断状態を使用します。"); return; }
        try
        {
            using var stream=File.OpenRead(path);
            ReplayRunner.Replay(stream,build,(s,h,e)=>
            {
                if(s.Tick==tick-1 || s.Tick==tick)Dump(dir,side+"-rerun-"+s.Tick,s);
                if(s.Tick==tick)throw new CapturedTick();
            },allow);
        }
        catch(CapturedTick) { }
    }
    private static DiagnosticState ReadState(string hashes,HashRow row,long tick)
    {
        if(row==null || row.Tick!=tick || !File.Exists(hashes+".states"))return null;
        using var r=new BinaryReader(File.OpenRead(hashes+".states")); long expected=0;
        while(r.BaseStream.Position<r.BaseStream.Length)
        {
            long t=r.ReadInt64(); if(t!=expected++)throw new InvalidDataException("Diagnostic tick sequence.");
            var bytes=ReplayBinary.Bytes(r,ReplayBinary.Count(r,ReplayBinary.MaxRecord));
            if(t==tick) { if(ReplayBinary.Hex(ReplayBinary.Hash(bytes))!=row.StateHash)throw new InvalidDataException("Diagnostic does not match advertised hash."); return new DiagnosticState(t,bytes); }
        }
        return null;
    }
}
