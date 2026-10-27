using DropPod.Client.Behaviors;
using DropPod.Client.Net;
using DropPod.Client.Recording;
using DropPod.Client.Web;

namespace DropPod.Client;

public enum PilotState
{
    Waiting,
    LoggingIn,
    Active,
    Done,
}

/// <summary>
/// Flies one pod account: logs in through RIN, drops the pod and hands it to its behavior once it's in the world.
/// <see cref="RunAsync"/> doesn't throw, how it ended is in <see cref="Failure"/>.
/// </summary>
public sealed class Pilot(string name, Red5Credentials account, ClientProtocol protocol, Func<IBehavior> behavior)
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);

    // The client takes about 20 s from the ticket to the second PlayerReady
    private static readonly TimeSpan WorldTimeout = TimeSpan.FromSeconds(60);

    public string Name { get; } = name;

    public Red5Credentials Account { get; } = account;

    public PilotState State { get; private set; }

    /// <summary>The current pod, or the last one once the pilot is done.</summary>
    public Pod? Pod { get; private set; }

    /// <summary>Set before the run, every connection of this pilot goes into the same recording.</summary>
    public PcapngWriter? Recorder { get; init; }

    /// <summary>Set when the pod is in the world and its behavior started.</summary>
    public DateTime? ArrivedAt { get; private set; }

    /// <summary>What the pod had reached when the pilot stopped, disposing it closes it either way.</summary>
    public PodState LastState { get; private set; }

    public string? Failure { get; private set; }

    public event Action<Pilot, string>? Log;

    public async Task RunAsync(RinClient rin, CancellationToken ct)
    {
        Pod? pod = null;
        try
        {
            State = PilotState.LoggingIn;
            await rin.LoginAsync(Account, ct);
            var characters = await rin.ListCharactersAsync(Account, ct);
            var character = characters.FirstOrDefault(c => c.IsActive)
                ?? throw new InvalidOperationException($"{Account.Email} has no character, run seed first");

            pod = new Pod(Name, Account, character.CharacterGuid, protocol) { Recorder = Recorder };
            pod.Log += (_, message) => Log?.Invoke(this, message);
            Pod = pod;
            await pod.DropAsync(rin, ct);

            if (!await WaitAsync(pod, () => pod.State == PodState.InWorld && pod.Position != null, WorldTimeout, ct))
            {
                Failure = ClosedReason(pod) ?? "didn't make it into the world";
                return;
            }

            ArrivedAt = DateTime.UtcNow;
            State = PilotState.Active;
            pod.Behavior = behavior();

            await WaitAsync(pod, () => false, Timeout.InfiniteTimeSpan, ct);
            Failure = ClosedReason(pod);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception e) when (e is RinException or HttpRequestException or MatrixHandshakeException or InvalidOperationException)
        {
            Failure = e.Message;
            Log?.Invoke(this, $"failed: {e.Message}");
        }
        finally
        {
            if (pod != null)
            {
                LastState = pod.State;
                await pod.DisposeAsync();
            }

            State = PilotState.Done;
        }
    }

    private static string? ClosedReason(Pod pod) =>
        pod.Failure ?? (pod.ServerShutdownCode is { } code ? $"closed by the server ({code})" : null);

    /// <summary>False when the pod closed or the time ran out first.</summary>
    private static async Task<bool> WaitAsync(Pod pod, Func<bool> condition, TimeSpan timeout, CancellationToken ct)
    {
        var until = timeout == Timeout.InfiniteTimeSpan ? DateTime.MaxValue : DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (pod.State == PodState.Closed || DateTime.UtcNow >= until)
            {
                return false;
            }

            await Task.Delay(Poll, ct);
        }

        return true;
    }
}
