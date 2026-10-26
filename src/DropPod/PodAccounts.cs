using DropPod.Client.Web;

namespace DropPod;

/// <summary>
/// droppod001@droppod.local and so on, each with one character DropPod001. Test accounts for local stacks, so they
/// share a password.
/// </summary>
internal static class PodAccounts
{
    private const string Password = "droppod";

    public static Red5Credentials Account(int number) => new($"droppod{number:000}@droppod.local", Password);

    public static string CharacterName(int number) => $"DropPod{number:000}";
}
