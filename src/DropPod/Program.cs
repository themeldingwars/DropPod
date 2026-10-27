using System.Globalization;
using DropPod;
using DropPod.Client;
using DropPod.Client.Behaviors;
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

using var http = RinClient.CreateHttpClient(options.AcceptAnyCertificate);
var rin = new RinClient(http, options.Operator);

try
{
    var hosts = await rin.CheckAsync(stopping.Token);
    Console.WriteLine($"Operator {options.Operator}, client API {hosts.ClientapiHost}");

    return options.Command switch
    {
        "seed" => await SeedAsync(rin, options, stopping.Token),
        "drop" => await DropAsync(rin, options, stopping.Token, abort.Token),
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

static async Task<int> DropAsync(RinClient rin, Options options, CancellationToken stopping, CancellationToken abort)
{
    var folder = options.Record ? RecordingFolder(DateTime.UtcNow) : null;
    var pilots = new List<Pilot>();
    for (var number = options.First; number < options.First + options.Pods; number++)
    {
        var name = PodAccounts.CharacterName(number);
        pilots.Add(new Pilot(name, PodAccounts.Account(number), ClientProtocol.Build1962, () => PodBehaviors.Create(options.Behavior))
        {
            Recorder = folder != null ? new PcapngWriter(Path.Combine(folder, $"{name}.pcapng")) : null,
        });
    }

    try
    {
        return await Runner.RunAsync(rin, pilots, options.Ramp, options.Duration, stopping, abort) ? 0 : 1;
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

