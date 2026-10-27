using System.Globalization;
using System.Text;
using DropPod.Client;
using DropPod.Client.Invasions;
using DropPod.Client.Web;

namespace DropPod;

/// <summary>
/// Ramps the pilots up, waits until every pod is in the world, holds, then ramps down in reverse order. Stopping skips
/// to the ramp down, aborting closes everything at once.
/// </summary>
internal static class Runner
{
    private static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(10);

    public static async Task<bool> RunAsync(RinClient rin, IReadOnlyList<Pilot> pilots, Ramp ramp, TimeSpan hold, CancellationToken stopping, CancellationToken abort)
    {
        var started = DateTime.UtcNow;
        var flights = new List<(Pilot Pilot, Task Task, CancellationTokenSource Stop)>();
        foreach (var pilot in pilots)
        {
            pilot.Log += (p, message) => Console.WriteLine($"[{p.Name}] {message}");
        }

        try
        {
            Console.WriteLine($"Dropping {pilots.Count} pod(s) at {ramp}");
            foreach (var pilot in pilots)
            {
                var stop = CancellationTokenSource.CreateLinkedTokenSource(abort);
                flights.Add((pilot, pilot.RunAsync(rin, stop.Token), stop));
                if (flights.Count < pilots.Count)
                {
                    await Task.Delay(ramp.Gap, stopping);
                }
            }

            await WaitAsync(pilots, started, () => pilots.All(p => p.ArrivedAt != null || p.State == PilotState.Done), stopping);
            Console.WriteLine($"{pilots.Count(p => p.State == PilotState.Active)} pod(s) in the world, holding for {hold.TotalSeconds} s");

            var end = DateTime.UtcNow + hold;
            await WaitAsync(pilots, started, () => DateTime.UtcNow >= end || pilots.All(p => p.State == PilotState.Done), stopping);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
        finally
        {
            Console.WriteLine($"Ramping down {flights.Count} pod(s)");
            await RampDownAsync(flights, ramp, abort);

            Console.WriteLine();
            foreach (var pilot in pilots)
            {
                Console.WriteLine(Summary(pilot));
            }
        }

        return pilots.All(p => p.Failure == null);
    }

    private static async Task RampDownAsync(List<(Pilot Pilot, Task Task, CancellationTokenSource Stop)> flights, Ramp ramp, CancellationToken abort)
    {
        try
        {
            for (var i = flights.Count - 1; i >= 0; i--)
            {
                var (pilot, task, stop) = flights[i];
                if (pilot.State == PilotState.Done)
                {
                    continue;
                }

                await stop.CancelAsync();
                await task;
                if (i > 0)
                {
                    await Task.Delay(ramp.Gap, abort);
                }
            }
        }
        catch (OperationCanceledException) when (abort.IsCancellationRequested)
        {
        }

        // The pilots stop on the abort too, this only waits for their CloseConnection to go out
        await Task.WhenAll(flights.Select(f => f.Task));
        foreach (var flight in flights)
        {
            flight.Stop.Dispose();
        }
    }

    private static async Task WaitAsync(IReadOnlyList<Pilot> pilots, DateTime started, Func<bool> done, CancellationToken ct)
    {
        var nextStatus = DateTime.UtcNow + StatusInterval;
        while (!done())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
            if (DateTime.UtcNow >= nextStatus)
            {
                Console.WriteLine(Status(pilots, DateTime.UtcNow - started));
                nextStatus += StatusInterval;
            }
        }
    }

    private static string Status(IReadOnlyCollection<Pilot> pilots, TimeSpan elapsed)
    {
        var pods = pilots.Select(p => p.Pod).OfType<Pod>().ToList();
        var states = string.Join(", ", pilots.GroupBy(p => p.State).OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key}"));
        var stalled = pods.Count(p => p.Inbound.Values.Any(c => c.GapSince != null));
        var rtt = pods.Where(p => p.RoundTrip != null).Select(p => p.RoundTrip!.Value.TotalMilliseconds).DefaultIfEmpty().Average();
        return $"[{elapsed:hh\\:mm\\:ss}] {states}; {stalled} waiting on a gap; round trip {rtt:F0} ms";
    }

    private static string Summary(Pilot pilot)
    {
        if (pilot.Pod is not { } pod)
        {
            return $"{pilot.Name} never dropped: {pilot.Failure ?? "stopped"}";
        }

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{pod.Name} socket {pod.SocketId:x8} {pilot.LastState}");
        text.Append(CultureInfo.InvariantCulture, $", zone {pod.ZoneId} after {Seconds(pod.TimeToZone)}, in the world after {Seconds(pod.TimeToWorld)}");
        if (pilot.Transferred)
        {
            text.Append(CultureInfo.InvariantCulture, $", transferred, {Seconds(pilot.TimeToArrive)} from the first ticket");
        }

        if (pilot.Connections > 1)
        {
            text.Append(CultureInfo.InvariantCulture, $", {pilot.Connections} connections");
        }

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

        if (pilot.Failure != null)
        {
            text.Append(CultureInfo.InvariantCulture, $", failed: {pilot.Failure}");
        }

        return text.ToString();
    }

    private static string Seconds(TimeSpan? time) => time is { } t ? $"{t.TotalSeconds:F1} s" : "-";
}
