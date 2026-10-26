using DropPod;
using DropPod.Client;
using DropPod.Client.Net;
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
    var pods = new List<Pod>();
    try
    {
        for (var number = options.First; number < options.First + options.Pods; number++)
        {
            var account = PodAccounts.Account(number);
            await rin.LoginAsync(account, ct);

            var characters = await rin.ListCharactersAsync(account, ct);
            var character = characters.FirstOrDefault(c => c.IsActive)
                ?? throw new InvalidOperationException($"{account.Email} has no character, run seed first");

            var pod = new Pod(PodAccounts.CharacterName(number), account, character.CharacterGuid, ClientProtocol.Build1962);
            pod.Log += (p, message) => Console.WriteLine($"[{p.Name}] {message}");
            pods.Add(pod);

            await pod.DropAsync(rin, ct);
        }

        Console.WriteLine($"{pods.Count} pod(s) dropped, staying for {options.Duration.TotalSeconds} s");
        await Task.Delay(options.Duration, ct);
    }
    finally
    {
        foreach (var pod in pods)
        {
            var closed = pod.ServerShutdownCode is { } code ? $", closed by the server ({code})" : string.Empty;
            Console.WriteLine($"{pod.Name} socket {pod.SocketId:x8}: {pod.State} in zone {pod.ZoneId}, {pod.World.Entities} entities{closed}");
            await pod.DisposeAsync();
        }
    }

    return 0;
}
