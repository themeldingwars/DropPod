using FauFau.Net.Web;

namespace DropPod.Client.Web;

/// <summary>
/// Signs requests with the uid and secret derived from email and password. The password itself only goes over
/// the wire to create the account.
/// </summary>
public sealed class Red5Credentials
{
    public Red5Credentials(string email, string password)
    {
        Email = email;
        Password = password;
        Uid = Auth.GenerateUserId(email).ToString();
        Secret = Auth.GenerateSecret(email, password).ToString();
    }

    public string Email { get; }

    /// <summary>Only needed to create the account.</summary>
    public string Password { get; }

    public string Uid { get; }

    public string Secret { get; }

    /// <summary>
    /// The X-Red5-Signature value: an HMAC-SHA1 token over the uid, host, path, a hash of the body and, for the oracle
    /// ticket, the character.
    /// </summary>
    public string CreateSignature(Uri uri, ReadOnlySpan<byte> body, ulong characterGuid = 0)
    {
        var hbody = Red5Sig.HashBody(body);
        var request = Red5Sig.CreateRequestString(Uid, uri.Authority, uri.AbsolutePath, hbody, characterGuid).ToString();

        Span<char> token = stackalloc char[40];
        Auth.GenerateToken(Secret, request, token);

        return $"{Auth.SIG_HEADER_START}{token} {request}";
    }
}
