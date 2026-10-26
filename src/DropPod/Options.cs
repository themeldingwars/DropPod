using System.Globalization;

namespace DropPod;

internal sealed record Options(
    string Command,
    Uri Operator,
    int Pods,
    int First,
    TimeSpan Duration,
    bool AcceptAnyCertificate)
{
    public const string Usage = """
        droppod <command> [options]

        Commands:
          seed               Create the pod accounts and their characters on RIN (safe to run again)
          drop               Drop pods: log in through RIN and stay in the world for a while

        Options:
          --operator <url>   Operator host, the client's OperatorHost (default https://localhost:5001)
          --pods <n>         Number of pods (default 1)
          --first <n>        Number of the first pod, to split pods across runs (default 1)
          --seconds <n>      How long dropped pods stay connected (default 30)
          --insecure         Accept any TLS certificate (on by default for localhost)
        """;

    public static Options? Parse(string[] args, out string? error)
    {
        error = null;
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            return null;
        }

        var command = args[0];
        var operatorHost = new Uri("https://localhost:5001");
        var pods = 1;
        var first = 1;
        var seconds = 30;
        bool? insecure = null;

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
                        seconds = int.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "--insecure":
                        insecure = true;
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

        var acceptAny = insecure ?? operatorHost.IsLoopback;
        return new Options(command, operatorHost, pods, first, TimeSpan.FromSeconds(seconds), acceptAny);
    }
}
