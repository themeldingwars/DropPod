using System.Globalization;
using System.Text.Json;
using DropPod.Client.Behaviors;

namespace DropPod.Client.Invasions;

public sealed record InvasionGroup(int Pods, string Behavior, uint? Zone);

/// <summary>
/// A mixed run from invasions/&lt;name&gt;.json. Groups take consecutive pod numbers in the order they're listed.
/// </summary>
public sealed record Invasion(Uri? Operator, Ramp? Ramp, TimeSpan? Hold, IReadOnlyList<InvasionGroup> Groups)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public int Pods => Groups.Sum(g => g.Pods);

    /// <summary>A name looks in invasions/, anything with a slash or an extension is taken as a path.</summary>
    public static Invasion Load(string nameOrPath)
    {
        var path = nameOrPath.IndexOfAny(['/', '\\', '.']) >= 0 ? nameOrPath : Path.Combine("invasions", $"{nameOrPath}.json");
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"No invasion file at {path}");
        }

        return Parse(File.ReadAllText(path), path);
    }

    /// <exception cref="InvalidDataException">The file isn't valid JSON or doesn't describe an invasion.</exception>
    public static Invasion Parse(string json, string source = "invasion")
    {
        InvasionFile? file;
        try
        {
            file = JsonSerializer.Deserialize<InvasionFile>(json, JsonOptions);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{source}: {e.Message}", e);
        }

        if (file?.Groups is not { Count: > 0 } groups)
        {
            throw new InvalidDataException($"{source}: no groups");
        }

        Uri? operatorHost = null;
        if (file.Operator != null && !Uri.TryCreate(file.Operator, UriKind.Absolute, out operatorHost))
        {
            throw new InvalidDataException($"{source}: can't read the operator {file.Operator}");
        }

        Ramp? ramp = null;
        if (file.Ramp != null)
        {
            ramp = Invasions.Ramp.TryParse(file.Ramp, out var parsed)
                ? parsed
                : throw new InvalidDataException($"{source}: can't read the ramp {file.Ramp}, expected e.g. 5/s or 30/m");
        }

        TimeSpan? hold = null;
        if (file.Hold != null)
        {
            hold = TryParseDuration(file.Hold, out var parsed)
                ? parsed
                : throw new InvalidDataException($"{source}: can't read the hold {file.Hold}, expected e.g. 90s, 10m or 1h");
        }

        var result = new List<InvasionGroup>();
        foreach (var group in groups)
        {
            var behavior = group.Behavior ?? "idle";
            if (group.Pods <= 0)
            {
                throw new InvalidDataException($"{source}: every group needs at least one pod");
            }

            if (!PodBehaviors.IsKnown(behavior))
            {
                throw new InvalidDataException($"{source}: unknown behavior {behavior}, expected one of {string.Join(", ", PodBehaviors.Names)}");
            }

            result.Add(new InvasionGroup(group.Pods, behavior, group.Zone));
        }

        return new Invasion(operatorHost, ramp, hold, result);
    }

    /// <summary>"90s", "10m", "1h", a bare number is seconds.</summary>
    public static bool TryParseDuration(string text, out TimeSpan duration)
    {
        duration = default;
        var unit = 's';
        var number = text;
        if (text.Length > 0 && char.IsLetter(text[^1]))
        {
            unit = text[^1];
            number = text[..^1];
        }

        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return false;
        }

        switch (unit)
        {
            case 's':
                duration = TimeSpan.FromSeconds(value);
                return true;
            case 'm':
                duration = TimeSpan.FromMinutes(value);
                return true;
            case 'h':
                duration = TimeSpan.FromHours(value);
                return true;
            default:
                return false;
        }
    }

    private sealed record InvasionFile(string? Operator, string? Ramp, string? Hold, List<GroupEntry>? Groups);

    private sealed record GroupEntry(int Pods, string? Behavior, uint? Zone);
}
