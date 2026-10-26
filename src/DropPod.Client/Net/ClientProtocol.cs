using Aero.Protocol;

namespace DropPod.Client.Net;

/// <summary>
/// Resolved from Aero.Protocol's patch table like PIN's GameServer does it. Build 1962 is Matrix 47464 and GSS 19551
/// on the wire.
/// </summary>
public sealed record ClientProtocol(MatrixVersion Matrix, GssVersion Gss)
{
    public static readonly ClientProtocol Build1962 = Resolve("1962");

    /// <summary>Matrix protocol number as the POKE carries it.</summary>
    public ushort RawMatrix => ProtocolVersions.MatrixRaw[(int)Matrix];

    /// <summary>GSS protocol number as the KISS carries it.</summary>
    public ushort RawGss => ProtocolVersions.GssRaw[(int)Gss];

    public static ClientProtocol Resolve(string clientVersion, string environment = "production", string branch = "prod")
    {
        foreach (var patch in Patches.All)
        {
            var version = patch.Version.EndsWith(".0", StringComparison.Ordinal) ? patch.Version[..^2] : patch.Version;
            if (version == clientVersion
                && string.Equals(patch.Environment, environment, StringComparison.OrdinalIgnoreCase)
                && string.Equals(patch.Branch, branch, StringComparison.OrdinalIgnoreCase)
                && ProtocolVersions.TryGetMatrixVersion(patch.MatrixProtocolVersion, out var matrix)
                && ProtocolVersions.TryGetGssVersion(patch.GssProtocolVersion, out var gss))
            {
                return new ClientProtocol(matrix, gss);
            }
        }

        throw new ArgumentException($"Aero.Protocol doesn't know client build {clientVersion} ({environment}/{branch})", nameof(clientVersion));
    }
}
