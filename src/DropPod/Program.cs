using System.Globalization;
using System.Text;
using DropPod;
using DropPod.Client;
using DropPod.Client.Net;
using DropPod.Client.Recording;
using DropPod.Client.Web;

var options = Options.Parse(args, out var error);
if (options == null)
{
    if (error != null)
    {
        Console.Error.WriteLine(error);
    }

    Console.WriteLine(Options.Usage);
    return error == null ? 0 : 1;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

using var http = RinClient.CreateHttpClient(options.AcceptAnyCertificate);
var rin = new RinClient(http, options.Operator);

try
{
    var hosts = await rin.CheckAsync(cts.Token);
    Console.WriteLine($"Operator {options.Operator}, client API {hosts.ClientapiHost}");

    return options.Command switch
    {
        "seed" => await SeedAsync(rin, options, cts.Token),
        "drop" => await DropAsync(rin, options, cts.Token),
        _ => Unknown(options.Command),
    };
}
catch (Exception e) when (e is RinException or HttpRequestException or MatrixHandshakeException or InvalidOperationException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    return 130;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"Unknown command {command}");
    Console.WriteLine(Options.Usage);
    return 1;
}

static async Task<int> SeedAsync(RinClient rin, Options options, CancellationToken ct)
{
    for (var number = options.First; number < options.First + options.Pods; number++)
    {
        var account = PodAccounts.Account(number);
        var created = await rin.CreateAccountAsync(account, "CH", ct);
        await rin.LoginAsync(account, ct);

        var characters = await rin.ListCharactersAsync(account, ct);
        var name = PodAccounts.CharacterName(number);
        if (characters.Count == 0)
        {
            await rin.CreateCharacterAsync(account, new CreateCharacterRequest(name), ct);
        }

        Console.WriteLine($"{account.Email}: {(created ? "created" : "exists")}, {(characters.Count == 0 ? $"character {name} created" : $"{characters.Count} character(s)")}");
    }

    return 0;
}

static async Task<int> DropAsync(RinClient rin, Options options, CancellationToken ct)
{
    var started = DateTime.UtcNow;
    var folder = options.Record ? RecordingFolder(started) : null;
    var pods = new List<Pod>();
    var recorders = new List<PcapngWriter>();
    try
    {
        for (var number = options.First; number < options.First + options.Pods; number++)
        {
            var account = PodAccounts.Account(number);
            await rin.LoginAsync(account, ct);

            var characters = await rin.ListCharactersAsync(account, ct);
            var character = characters.FirstOrDefault(c => c.IsActive)
                ?? throw new InvalidOperationException($"{account.Email} has no character, run seed first");

            var name = PodAccounts.CharacterName(number);
            PcapngWriter? recorder = null;
            if (folder != null)
            {
                recorder = new PcapngWriter(Path.Combine(folder, $"{name}.pcapng"));
                recorders.Add(recorder);
            }

            var pod = new Pod(name, account, character.CharacterGuid, ClientProtocol.Build1962) { Recorder = recorder };
            pod.Log += (p, message) => Console.WriteLine($"[{p.Name}] {message}");
            pods.Add(pod);

            await pod.DropAsync(rin, ct);
        }

        Console.WriteLine($"{pods.Count} pod(s) dropped, staying for {options.Duration.TotalSeconds} s");
        var end = started + options.Duration;
        while (DateTime.UtcNow < end && pods.Any(p => p.State != PodState.Closed))
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Max(1, (end - DateTime.UtcNow).TotalSeconds))), ct);
            Console.WriteLine(Status(pods, DateTime.UtcNow - started));
        }
    }
    finally
    {
        Console.WriteLine();
        foreach (var pod in pods)
        {
            Console.WriteLine(Summary(pod));
        }

        foreach (var pod in pods)
        {
            await pod.DisposeAsync();
        }

        // After the pods, so their CloseConnection is in the recording too
        foreach (var recorder in recorders)
        {
            recorder.Dispose();
        }

        if (folder != null)
        {
            Console.WriteLine($"Recordings in {folder}");
        }
    }

    return pods.All(p => p.Failure == null && p.ServerShutdownCode == null) ? 0 : 1;
}

static string Status(IReadOnlyCollection<Pod> pods, TimeSpan elapsed)
{
    var states = string.Join(", ", pods.GroupBy(p => p.State).OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key}"));
    var stalled = pods.Count(p => p.Inbound.Values.Any(c => c.GapSince != null));
    var rtt = pods.Where(p => p.RoundTrip != null).Select(p => p.RoundTrip!.Value.TotalMilliseconds).DefaultIfEmpty().Average();
    return $"[{elapsed:hh\\:mm\\:ss}] {states}; {stalled} waiting on a gap; round trip {rtt:F0} ms";
}

static string Summary(Pod pod)
{
    var text = new StringBuilder();
    text.Append(CultureInfo.InvariantCulture, $"{pod.Name} socket {pod.SocketId:x8} {pod.State}");
    text.Append(CultureInfo.InvariantCulture, $", zone {pod.ZoneId} after {Seconds(pod.TimeToZone)}, in the world after {Seconds(pod.TimeToWorld)}");
    text.Append(CultureInfo.InvariantCulture, $", position {(pod.Position is { } p ? $"{p.X:F1} {p.Y:F1} {p.Z:F1}" : "unknown")}");
    text.Append(CultureInfo.InvariantCulture, $", round trip {(pod.RoundTrip is { } rtt ? $"{rtt.TotalMilliseconds:F0} ms" : "-")}");
    text.Append(CultureInfo.InvariantCulture, $", {pod.DatagramsSent} datagrams sent, {pod.DatagramsReceived} received");
    text.Append(CultureInfo.InvariantCulture, $", {pod.World.Entities} entities ({pod.World.Views} views, {pod.World.Controllers} controllers, {pod.World.ScopedOut} scoped out, {pod.World.KeyframesRequested} keyframes requested)");

    foreach (var (channel, inbound) in pod.Inbound)
    {
        text.Append(CultureInfo.InvariantCulture, $", {channel} in: {inbound.Duplicates} duplicates {inbound.Late} late {inbound.Held} held");
        if (inbound.GapSince is { } since)
        {
            text.Append(CultureInfo.InvariantCulture, $" (gap since {since:HH:mm:ss})");
        }
    }

    foreach (var (channel, outbound) in pod.Outbound.Where(o => o.Value.IsReliable))
    {
        text.Append(CultureInfo.InvariantCulture, $", {channel} out: {outbound.Resent} resent {outbound.Lost} lost {outbound.Unacked} unacked");
    }

    if (pod.ServerShutdownCode is { } code)
    {
        text.Append(CultureInfo.InvariantCulture, $", closed by the server ({code})");
    }

    if (pod.Failure != null)
    {
        text.Append(CultureInfo.InvariantCulture, $", failed: {pod.Failure}");
    }

    return text.ToString();
}

static string RecordingFolder(DateTime started)
{
    var folder = Path.Combine("runs", started.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
    Directory.CreateDirectory(folder);
    return folder;
}

static string Seconds(TimeSpan? time) => time is { } t ? $"{t.TotalSeconds:F1} s" : "-";
