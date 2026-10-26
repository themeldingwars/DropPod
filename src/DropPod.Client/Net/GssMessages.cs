using System.Buffers.Binary;
using Aero.Gen;
using Aero.Protocol;

namespace DropPod.Client.Net;

/// <summary><see cref="EntityId"/> has its low byte cleared, on the wire the typecode goes there.</summary>
public readonly record struct GssPayload(byte Typecode, ulong EntityId, byte MessageId, ReadOnlyMemory<byte> Body);

/// <summary>
/// The header is the entity id as a little-endian uint64 with the typecode in its low byte, then the message id.
/// Wire ids come from Aero.Protocol's tables, the same way PIN resolves them.
/// </summary>
public static class GssMessages
{
    public const int HeaderSize = 9;

    /// <summary>
    /// The client sends root namespace messages (ScheduleUpdateRequest and co.) to the instance with typecode 251, the
    /// server uses 0.
    /// </summary>
    public const byte ClientRootTypecode = 251;

    // Below the real message ids, the server uses these for the state of views and controllers (PIN's Channel)
    public const byte Update = 1;
    public const byte Checksum = 2;
    public const byte ViewKeyframe = 3;
    public const byte ControllerKeyframe = 4;
    public const byte ControllerRemove = 5;
    public const byte ScopeOut = 6;

    public static byte[] Encode(IAero message, byte typecode, ulong entityId, byte messageId)
    {
        var payload = new byte[HeaderSize + message.GetPackedSize()];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, entityId & 0xffffffffffffff00);
        payload[0] = typecode;
        payload[8] = messageId;
        message.Pack(payload.AsSpan(HeaderSize));
        return payload;
    }

    public static byte[] EncodeCommand(IAero message, GssCharacterView view, GssCharacterCommand command, ulong entityId, GssVersion version)
    {
        if (!GssTables.TryGetWireIds(version, view, command, out var typecode, out var messageId))
        {
            throw new ArgumentException($"{view}.{command} has no wire ids in GSS protocol {version}", nameof(command));
        }

        return Encode(message, typecode, entityId, messageId);
    }

    public static byte[] EncodeRoot(IAero message, GssMessage kind, ulong instanceId, GssVersion version)
    {
        var messageId = GssTables.GetMessageId(version, GssTables.Ns.Root, GssTables.Kind.Message, (int)kind);
        if (messageId == 0)
        {
            throw new ArgumentException($"{kind} has no message id in GSS protocol {version}", nameof(kind));
        }

        return Encode(message, ClientRootTypecode, instanceId, messageId);
    }

    public static bool TryParse(ReadOnlyMemory<byte> payload, out GssPayload message)
    {
        message = default;
        if (payload.Length < HeaderSize)
        {
            return false;
        }

        var span = payload.Span;
        var entityId = BinaryPrimitives.ReadUInt64LittleEndian(span) & 0xffffffffffffff00;
        message = new GssPayload(span[0], entityId, span[8], payload[HeaderSize..]);
        return true;
    }
}
