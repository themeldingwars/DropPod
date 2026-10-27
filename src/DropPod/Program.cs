using System.Globalization;
using DropPod;
using DropPod.Client;
using DropPod.Client.Behaviors;
using DropPod.Client.Invasions;
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

// drop is an invasion with a single group from the options
Invasion invasion;
try
{
    invasion = options.Command == "invade"
        ? Invasion.Load(options.Scenario!)
        : new Invasion(null, null, null, [new InvasionGroup(options.Pods, options.Behavior, options.Zone)]);
}
catch (Exception e) when (e is InvalidDataException or IOException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

var operatorHost = options.Operator ?? invasion.Operator ?? Options.DefaultOperator;

// The first Ctrl+C ramps the pods down, the second closes them all at once
using var stopping = new CancellationTokenSource();
using var abort = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    if (stopping.IsCancellationRequested)
    {
        abort.Cancel();
    }
    else
    {
        Console.WriteLine("Stopping, press Ctrl+C again to close every pod at once");
        stopping.Cancel();
    }
};

using var http = RinClient.CreateHttpClient(options.Insecure ?? operatorHost.IsLoopback);
var rin = new RinClient(http, operatorHost);

try
{
    var hosts = await rin.CheckAsync(stopping.Token);
    Console.WriteLine($"Operator {operatorHost}, client API {hosts.ClientapiHost}");

    return options.Command switch
    {
        "seed" => await SeedAsync(rin, options, stopping.Token),
        "drop" or "invade" => await InvadeAsync(rin, options, invasion, stopping.Token, abort.Token),
        _ => Unknown(options.Command),
    };
}
catch (Exception e) when (e is RinException or HttpRequestException or MatrixHandshakeException or InvalidOperationException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
catch (OperationCanceledException) when (stopping.IsCancellationRequested)
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

static async Task<int> InvadeAsync(RinClient rin, Options options, Invasion invasion, CancellationToken stopping, CancellationToken abort)
{
    var folder = options.Record ? RecordingFolder(DateTime.UtcNow) : null;
    var pilots = new List<Pilot>();
    var number = options.First;
    foreach (var group in invasion.Groups)
    {
        for (var i = 0; i < group.Pods; i++, number++)
        {
            var name = PodAccounts.CharacterName(number);
            pilots.Add(new Pilot(name, PodAccounts.Account(number), ClientProtocol.Build1962, () => PodBehaviors.Create(group.Behavior))
            {
                Recorder = folder != null ? new PcapngWriter(Path.Combine(folder, $"{name}.pcapng")) : null,
                Zone = group.Zone,
            });
        }
    }

    var ramp = options.Ramp ?? invasion.Ramp ?? Ramp.Default;
    var hold = options.Hold ?? invasion.Hold ?? Options.DefaultHold;
    try
    {
        return await Runner.RunAsync(rin, pilots, ramp, hold, stopping, abort) ? 0 : 1;
    }
    finally
    {
        // After the pilots, so their CloseConnection is in the recording too
        foreach (var pilot in pilots)
        {
            pilot.Recorder?.Dispose();
        }

        if (folder != null)
        {
            Console.WriteLine($"Recordings in {folder}");
        }
    }
}

static string RecordingFolder(DateTime started)
{
    var folder = Path.Combine("runs", started.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
    Directory.CreateDirectory(folder);
    return folder;
}

