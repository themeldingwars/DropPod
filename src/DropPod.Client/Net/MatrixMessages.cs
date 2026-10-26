using Aero.Gen;
using Aero.Protocol;
using AeroMessages.Matrix;

namespace DropPod.Client.Net;

/// <summary>One wire id byte from Aero.Protocol's tables for the protocol version, then the Aero-packed message.</summary>
public static class MatrixMessages
{
    public static byte[] Encode(IAero message, MatrixMessage kind, MatrixVersion version)
    {
        var wireId = MatrixTables.GetMessageId(version, kind);
        if (wireId == 0)
        {
            throw new ArgumentException($"{kind} has no wire id in Matrix protocol {version}", nameof(kind));
        }

        var payload = new byte[1 + message.GetPackedSize()];
        payload[0] = wireId;
        message.Pack(payload.AsSpan(1));
        return payload;
    }

    /// <summary>False for a wire id the protocol version doesn't know.</summary>
    public static bool TryDecode(ReadOnlyMemory<byte> payload, MatrixVersion version, out MatrixMessage kind, out ReadOnlyMemory<byte> body)
    {
        kind = default;
        body = default;
        if (payload.IsEmpty)
        {
            return false;
        }

        var ordinal = MatrixTables.FindMessage(version, payload.Span[0]);
        if (ordinal < 0)
        {
            return false;
        }

        kind = (MatrixMessage)ordinal;
        body = payload[1..];
        return true;
    }

    public static T Unpack<T>(ReadOnlyMemory<byte> body)
        where T : IAero, new()
    {
        var message = new T();
        message.Unpack(body.Span);
        return message;
    }

    /// <summary>With the values the 1962 client sent. The ticket is the oracle's, decoded.</summary>
    public static Login Login(ulong characterGuid, byte[] ticket)
    {
        return new Login
        {
            SecurityFlags = 0,
            ClientVersion = 1962,
            Unk2 = string.Empty,
            CharacterGuid = characterGuid,
            ClientPreferences = new ClientPreferencesData
            {
                MaxBytesPerSecond = 32000,
                MinBatchDelay = 0,
                TargetMTU = 1168,
                MatrixDebugFlags = 0,
            },
            Unk7 = 0,
            Locale = 0,
            Red5Sig2 = string.Empty,
            HaveUnk9 = 0,
            Ticket = ticket,
        };
    }
}
