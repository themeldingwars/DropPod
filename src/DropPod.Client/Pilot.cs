using DropPod.Client.Behaviors;
using DropPod.Client.Net;
using DropPod.Client.Recording;
using DropPod.Client.Web;
using DropPod.Client.World;

namespace DropPod.Client;

public enum PilotState
{
    Waiting,
    LoggingIn,
    Transferring,
    Active,
    Resting,
    Done,
}

/// <summary>
/// Flies one pod account: logs in through RIN, drops the pod and hands it to its behavior once it's in the world.
/// When the character is in another zone than <see cref="Zone"/>, the pod first sends the zone admin command and logs
/// in again. A behavior that leaves (churn) gets a new connection after its pause. <see cref="RunAsync"/> doesn't
/// throw, how it ended is in <see cref="Failure"/>.
/// </summary>
public sealed class Pilot(string name, Red5Credentials account, ClientProtocol protocol, Func<IBehavior> behavior)
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);

    // The client takes about 20 s from the ticket to the second PlayerReady
    private static readonly TimeSpan WorldTimeout = TimeSpan.FromSeconds(60);

    // PIN saves the zone through RIN before it closes the connection
    private static readonly TimeSpan TransferTimeout = TimeSpan.FromSeconds(20);

    public string Name { get; } = name;

    public Red5Credentials Account { get; } = account;

    /// <summary>The zone the behavior runs in, null for wherever the character is.</summary>
    public uint? Zone { get; init; }

    public PilotState State { get; private set; }

    /// <summary>The current pod, or the last one once the pilot is done.</summary>
    public Pod? Pod { get; private set; }

    /// <summary>Set before the run, every connection of this pilot goes into the same recording.</summary>
    public PcapngWriter? Recorder { get; init; }

    public int Connections { get; private set; }

    public bool Transferred { get; private set; }

    /// <summary>Set when the pod is in the wanted zone and its behavior started.</summary>
    public DateTime? ArrivedAt { get; private set; }

    /// <summary>From the first ticket to <see cref="ArrivedAt"/>, a transfer included.</summary>
    public TimeSpan? TimeToArrive { get; private set; }

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

            var started = DateTime.UtcNow;
            while (true)
            {
                State = PilotState.LoggingIn;
                pod = new Pod(Name, Account, character.CharacterGuid, protocol) { Recorder = Recorder };
                pod.Log += (_, message) => Log?.Invoke(this, message);
                Pod = pod;
                Connections++;
                await pod.DropAsync(rin, ct);

                if (!await WaitAsync(pod, () => pod.State == PodState.InWorld && pod.Position != null, WorldTimeout, ct))
                {
                    Failure = ClosedReason(pod) ?? "didn't make it into the world";
                    return;
                }

                if (Zone is { } zone && pod.ZoneId != zone)
                {
                    if (Transferred)
                    {
                        Failure = $"still in zone {pod.ZoneId} after the transfer to {zone}";
                        return;
                    }

                    if (!await TransferAsync(pod, zone, ct))
                    {
                        return;
                    }

                    LastState = pod.State;
                    await pod.DisposeAsync();
                    pod = null;
                    continue;
                }

                if (ArrivedAt == null)
                {
                    ArrivedAt = DateTime.UtcNow;
                    TimeToArrive = ArrivedAt - started;
                }

                State = PilotState.Active;
                pod.Behavior = behavior();

                if (!await WaitAsync(pod, () => pod.LeaveFor != null, Timeout.InfiniteTimeSpan, ct))
                {
                    Failure = ClosedReason(pod);
                    return;
                }

                State = PilotState.Resting;
                var pause = pod.LeaveFor!.Value;
                LastState = pod.State;
                await pod.DisposeAsync();
                pod = null;
                await Task.Delay(pause, ct);
            }
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

    /// <summary>True when PIN closed the connection for the transfer, a refusal comes back on the Debug channel.</summary>
    private async Task<bool> TransferAsync(Pod pod, uint zone, CancellationToken ct)
    {
        State = PilotState.Transferring;
        Transferred = true;
        Log?.Invoke(this, $"in zone {pod.ZoneId}, transferring to {zone}");

        var sentAt = DateTime.UtcNow;
        pod.Behavior = new AdminCommand($"zone {zone}");
        await WaitAsync(pod, () => false, TransferTimeout, ct);
        if (pod.ServerShutdownCode == 0)
        {
            return true;
        }

        var answer = pod.LastDebugChat is { } chat && chat.At >= sentAt ? chat.Text : null;
        Failure = answer ?? ClosedReason(pod) ?? $"no answer to the zone command within {TransferTimeout.TotalSeconds} s";
        return false;
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

    // Sent from the pod's loop like everything else the pod sends
    private sealed class AdminCommand(string command) : IBehavior
    {
        private bool _sent;

        public void Tick(Pod pod, DateTime now)
        {
            if (!_sent)
            {
                pod.Say(ChatChannel.Admin, command);
                _sent = true;
            }
        }
    }
}
