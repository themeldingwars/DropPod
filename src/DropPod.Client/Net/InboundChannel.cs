namespace DropPod.Client.Net;

/// <summary>
/// Reliable channels hand messages on in sequence order: a frame past a gap waits for the missing one, a frame
/// handled already is dropped. Split messages come out in one piece.
/// </summary>
/// <remarks>
/// A lost frame holds back everything after it until the server resends it, <see cref="GapSince"/> shows since when.
/// </remarks>
public sealed class InboundChannel
{
    private readonly Dictionary<ushort, Frame> _held = [];
    private readonly List<byte[]> _splitParts = [];
    private ushort _expected;

    public InboundChannel(Channel channel, ushort firstSequence)
    {
        Channel = channel;
        _expected = firstSequence;
    }

    public Channel Channel { get; }

    public bool IsReliable => Channel is Channel.Matrix or Channel.ReliableGss;

    /// <summary>Resends of frames that were handled already.</summary>
    public long Duplicates { get; private set; }

    /// <summary>Unreliable frames that arrived after a newer one.</summary>
    public long Late { get; private set; }

    /// <summary>Frames waiting behind a missing one.</summary>
    public int Held => _held.Count;

    /// <summary>The next frame in order.</summary>
    public ushort Expected => _expected;

    /// <summary>The frames waiting behind a missing one, in order.</summary>
    public List<ushort> HeldSequences => _held.Keys.OrderBy(s => unchecked((ushort)(s - _expected))).ToList();

    /// <summary>Since when frames are waiting behind a missing one, null while nothing is.</summary>
    public DateTime? GapSince { get; private set; }

    /// <summary>The messages a frame completes, in order. Empty when it waits, was a duplicate or is a split part.</summary>
    public List<ReadOnlyMemory<byte>> Accept(Frame frame, DateTime now)
    {
        var messages = new List<ReadOnlyMemory<byte>>();
        var sequence = frame.Sequence ?? 0;
        var delta = unchecked((short)(sequence - _expected));

        if (!IsReliable)
        {
            if (delta < 0)
            {
                Late++;
            }
            else
            {
                _expected = unchecked((ushort)(sequence + 1));
            }

            Deliver(frame, messages);
            return messages;
        }

        if (delta < 0)
        {
            Duplicates++;
            return messages;
        }

        if (delta > 0)
        {
            if (!_held.TryAdd(sequence, frame))
            {
                Duplicates++;
            }

            GapSince ??= now;
            return messages;
        }

        Deliver(frame, messages);
        while (_held.Remove(_expected, out var next))
        {
            Deliver(next, messages);
        }

        GapSince = _held.Count > 0 ? now : null;
        return messages;
    }

    private void Deliver(Frame frame, List<ReadOnlyMemory<byte>> messages)
    {
        if (IsReliable)
        {
            _expected = unchecked((ushort)(_expected + 1));
        }

        if (frame.IsSplit)
        {
            _splitParts.Add(frame.Payload.ToArray());
            return;
        }

        if (_splitParts.Count == 0)
        {
            messages.Add(frame.Payload);
            return;
        }

        var whole = new byte[_splitParts.Sum(p => p.Length) + frame.Payload.Length];
        var offset = 0;
        foreach (var part in _splitParts)
        {
            part.CopyTo(whole, offset);
            offset += part.Length;
        }

        frame.Payload.CopyTo(whole.AsMemory(offset));
        _splitParts.Clear();
        messages.Add(whole);
    }
}
