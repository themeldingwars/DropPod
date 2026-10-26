namespace DropPod.Client.Web;

// Only the fields the pods use. The JSON names are snake_case, see RinClient's serializer options.

public sealed record OperatorHosts(string ClientapiHost, string IngameHost);

public sealed record CreateAccountRequest(
    string Email,
    string Password,
    string Country,
    string Birthday,
    bool EmailOptin = false,
    string ReferralKey = "");

public sealed record LoginResponse(long AccountId, bool CanLogin, bool IsDev, int CharacterLimit);

public sealed record CharacterListResponse(List<CharacterInfo> Characters);

public sealed record CharacterInfo(ulong CharacterGuid, string Name, bool IsActive);

/// <summary>The defaults are what the 1962 client sent for a female Accord Assault.</summary>
public sealed record CreateCharacterRequest(
    string Name,
    int StartClassId = 76164,
    string Gender = "female",
    int Head = 10026,
    int HeadAccessoryA = 10113,
    int HeadAccessoryB = 0,
    int EyeColorId = 118980,
    int HairColorId = 77193,
    int SkinColorId = 118969,
    int VoiceSet = 1033,
    bool IsDev = false,
    string Environment = "prod");

public sealed record OracleTicketRequest(int Ver, string Build);

public sealed record OracleTicketResponse(string MatrixUrl, string Ticket, string SessionId);

public sealed record RinError(string Code, string Message);
