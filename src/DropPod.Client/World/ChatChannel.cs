namespace DropPod.Client.World;

/// <summary>The channels a pod uses, the values are the client's and PIN's.</summary>
public enum ChatChannel : byte
{
    Zone = 1,
    Say = 6,
    Debug = 11,
    Admin = 17,
}
