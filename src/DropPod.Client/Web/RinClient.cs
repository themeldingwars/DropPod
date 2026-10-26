using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace DropPod.Client.Web;

/// <summary>The web half of a login, in the client's order: operator check, signed login, characters, oracle ticket.</summary>
public sealed class RinClient
{
    public const string ClientBuild = "prod-1962";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly Uri _operatorHost;
    private Uri? _clientApiHost;

    public RinClient(HttpClient http, Uri operatorHost)
    {
        _http = http;
        _operatorHost = operatorHost;
    }

    /// <summary>Local stacks run on a development certificate, <paramref name="acceptAnyCertificate"/> lets it through.</summary>
    public static HttpClient CreateHttpClient(bool acceptAnyCertificate)
    {
        var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        if (acceptAnyCertificate)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }

        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"netlib2/{ClientBuild}");
        return http;
    }

    /// <summary>Has to run first, it finds the client API host the other calls go to.</summary>
    public async Task<OperatorHosts> CheckAsync(CancellationToken ct)
    {
        var uri = new Uri(_operatorHost, "/check?environment=prod&build=1962");
        using var response = await _http.GetAsync(uri, ct);
        var hosts = await ReadAsync<OperatorHosts>(response, ct);
        _clientApiHost = new Uri(hosts.ClientapiHost.TrimEnd('/') + "/");
        return hosts;
    }

    /// <summary>Returns false when the account exists already, so seeding can run again.</summary>
    public async Task<bool> CreateAccountAsync(Red5Credentials account, string country, CancellationToken ct)
    {
        var request = new CreateAccountRequest(account.Email, account.Password, country, "1990-01-01");
        using var response = await SendAsync(HttpMethod.Post, "api/v2/accounts", request, null, 0, ct);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var error = await TryReadErrorAsync(response, ct);
        if (error?.Code == "ERR_ACCOUNT_EXISTS")
        {
            return false;
        }

        throw new RinException("create account", response.StatusCode, error);
    }

    public async Task<LoginResponse> LoginAsync(Red5Credentials account, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/v2/accounts/login", null, account, 0, ct);
        return await ReadAsync<LoginResponse>(response, ct);
    }

    public async Task<IReadOnlyList<CharacterInfo>> ListCharactersAsync(Red5Credentials account, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/v2/characters/list", null, account, 0, ct);
        var list = await ReadAsync<CharacterListResponse>(response, ct);
        return list.Characters ?? [];
    }

    public async Task CreateCharacterAsync(Red5Credentials account, CreateCharacterRequest character, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/v1/characters", character, account, 0, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new RinException("create character", response.StatusCode, await TryReadErrorAsync(response, ct));
        }
    }

    /// <summary>The character goes into the signature (cid), the body only names the build.</summary>
    public async Task<OracleTicket> GetOracleTicketAsync(Red5Credentials account, ulong characterGuid, CancellationToken ct)
    {
        var request = new OracleTicketRequest(2, ClientBuild);
        using var response = await SendAsync(HttpMethod.Post, "api/v1/oracle/ticket", request, account, characterGuid, ct);

        // RIN answers 204 without a body when it isn't set up to hand out game servers (DevServerSettings.EnableLocalDev)
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            throw new RinException("oracle ticket", response.StatusCode, new RinError("NO_SERVER", "RIN has no game server to hand out"));
        }

        var ticket = await ReadAsync<OracleTicketResponse>(response, ct);
        return OracleTicket.Parse(ticket);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, Red5Credentials? signer, ulong characterGuid, CancellationToken ct)
    {
        if (_clientApiHost == null)
        {
            throw new InvalidOperationException("Run CheckAsync first, it finds the client API host");
        }

        var uri = new Uri(_clientApiHost, path);
        var bytes = body == null ? [] : JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), JsonOptions);

        using var request = new HttpRequestMessage(method, uri);
        if (method != HttpMethod.Get)
        {
            // The client sends a JSON content type even with an empty body, RIN's login expects it
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        if (signer != null)
        {
            request.Headers.TryAddWithoutValidation("X-Red5-Signature", signer.CreateSignature(uri, bytes, characterGuid));
        }

        return await _http.SendAsync(request, ct);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new RinException(response.RequestMessage?.RequestUri?.AbsolutePath ?? "request", response.StatusCode, await TryReadErrorAsync(response, ct));
        }

        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return value ?? throw new RinException(response.RequestMessage?.RequestUri?.AbsolutePath ?? "request", response.StatusCode, new RinError("EMPTY", "Empty response"));
    }

    private static async Task<RinError?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<RinError>(text, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record OracleTicket(string MatrixHost, int MatrixPort, byte[] Ticket, string SessionId)
{
    public static OracleTicket Parse(OracleTicketResponse response)
    {
        // matrix_url is host:port, the ticket base64, which the live servers wrapped in lines
        var separator = response.MatrixUrl.LastIndexOf(':');
        if (separator < 0 || !int.TryParse(response.MatrixUrl.AsSpan(separator + 1), out var port))
        {
            throw new FormatException($"matrix_url '{response.MatrixUrl}' isn't host:port");
        }

        var ticket = Convert.FromBase64String(response.Ticket.Replace("\n", string.Empty).Replace("\r", string.Empty));
        return new OracleTicket(response.MatrixUrl[..separator], port, ticket, response.SessionId ?? string.Empty);
    }
}

public sealed class RinException(string operation, HttpStatusCode status, RinError? error)
    : Exception($"{operation} failed with {(int)status}: {error?.Code ?? "no error code"} {error?.Message}".TrimEnd())
{
    public HttpStatusCode Status { get; } = status;

    public RinError? Error { get; } = error;
}
