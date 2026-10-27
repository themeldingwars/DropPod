using System.Globalization;
using DropPod.Client.Behaviors;
using DropPod.Client.Invasions;

namespace DropPod;

/// <summary>Null means not given, invade then takes the value from the invasion file.</summary>
internal sealed record Options(
    string Command,
    Uri? Operator,
    int Pods,
    int First,
    TimeSpan? Hold,
    bool? Insecure,
    bool Record,
    string Behavior,
    Ramp? Ramp,
    uint? Zone,
    string? Scenario)
{
    public static readonly Uri DefaultOperator = new("https://localhost:5001");
    public static readonly TimeSpan DefaultHold = TimeSpan.FromSeconds(30);

    public const string Usage = """
        droppod <command> [options]

        Commands:
          seed               Create the pod accounts and their characters on RIN (safe to run again)
          drop               Drop pods: log in through RIN and stay in the world for a while
          invade             Drop the groups of an invasion file, e.g. invade --scenario new-eden-200

        Options:
          --operator <url>   Operator host, the client's OperatorHost (default https://localhost:5001)
          --pods <n>         Number of pods (default 1)
          --first <n>        Number of the first pod, to split pods across runs (default 1)
          --seconds <n>      How long to hold once every pod is in the world (default 30)
          --ramp <n>/s       How fast pods drop and leave again, per second or /m per minute (default 5/s)
          --insecure         Accept any TLS certificate (on by default for localhost)
          --record           Record every pod as a pcapng in runs/<timestamp>
          --zone <id>        drop: move the characters to this zone first, with the zone admin command
          --behavior <name>  drop: what the pods do in the world: idle (default), wander, chat, churn or hop
          --scenario <name>  invade: invasions/<name>.json or a path, the options above override the file
        """;

    public static Options? Parse(string[] args, out string? error)
    {
        error = null;
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            return null;
        }

        var command = args[0];
        Uri? operatorHost = null;
        var pods = 1;
        var first = 1;
        TimeSpan? hold = null;
        bool? insecure = null;
        var record = false;
        var behavior = "idle";
        Ramp? ramp = null;
        uint? zone = null;
        string? scenario = null;

        for (var i = 1; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");

            try
            {
                switch (args[i])
                {
                    case "--operator":
                        operatorHost = new Uri(Next());
                        break;
                    case "--pods":
                        pods = int.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "--first":
                        first = int.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "--seconds":
                        hold = TimeSpan.FromSeconds(int.Parse(Next(), CultureInfo.InvariantCulture));
                        break;
                    case "--insecure":
                        insecure = true;
                        break;
                    case "--record":
                        record = true;
                        break;
                    case "--zone":
                        zone = uint.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "--ramp":
                        var text = Next();
                        if (!Client.Invasions.Ramp.TryParse(text, out var parsed))
                        {
                            error = $"Can't read the ramp {text}, expected e.g. 5/s or 30/m";
                            return null;
                        }

                        ramp = parsed;
                        break;
                    case "--behavior":
                        behavior = Next();
                        if (!PodBehaviors.IsKnown(behavior))
                        {
                            error = $"Unknown behavior {behavior}, expected one of {string.Join(", ", PodBehaviors.Names)}";
                            return null;
                        }

                        break;
                    case "--scenario":
                        scenario = Next();
                        break;
                    default:
                        error = $"Unknown option {args[i]}";
                        return null;
                }
            }
            catch (Exception e) when (e is ArgumentException or FormatException or UriFormatException or OverflowException)
            {
                error = e.Message;
                return null;
            }
        }

        if (command == "invade" && scenario == null)
        {
            error = "invade needs --scenario";
            return null;
        }

        return new Options(command, operatorHost, pods, first, hold, insecure, record, behavior, ramp, zone, scenario);
    }
}
