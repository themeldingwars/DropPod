using DropPod.Client.Net;

namespace DropPod.Client.World;

/// <summary>
/// The views the server scoped in, by entity and typecode. An update or checksum for a view without a keyframe ends
/// up in <see cref="TakeKeyframeRequests"/>, that's when the client sends a KeyframeRequest.
/// </summary>
public sealed class EntityTable
{
    private readonly HashSet<(ulong Entity, byte Typecode)> _views = [];
    private readonly HashSet<(ulong Entity, byte Typecode)> _controllers = [];
    private readonly HashSet<(ulong Entity, byte Typecode)> _requested = [];
    private readonly List<(ulong Entity, byte Typecode)> _toRequest = [];

    /// <summary>Distinct entities with at least one view.</summary>
    public int Entities => _views.Select(v => v.Entity).Distinct().Count();

    public int Views => _views.Count;

    public int Controllers => _controllers.Count;

    public long ScopedIn { get; private set; }

    public long ScopedOut { get; private set; }

    public long KeyframesRequested { get; private set; }

    /// <summary>True when the payload was about the state of a view or controller.</summary>
    public bool Apply(GssPayload message)
    {
        var key = (message.EntityId, message.Typecode);
        switch (message.MessageId)
        {
            case GssMessages.ViewKeyframe:
                if (_views.Add(key))
                {
                    ScopedIn++;
                }

                _requested.Remove(key);
                return true;
            case GssMessages.ControllerKeyframe:
                _controllers.Add(key);
                _requested.Remove(key);
                return true;
            case GssMessages.ControllerRemove:
                _controllers.Remove(key);
                return true;
            case GssMessages.ScopeOut:
                if (_views.Remove(key))
                {
                    ScopedOut++;
                }

                _requested.Remove(key);
                return true;
            case GssMessages.Update or GssMessages.Checksum:
                if (message.EntityId != 0 && !_views.Contains(key) && !_controllers.Contains(key) && _requested.Add(key))
                {
                    _toRequest.Add(key);
                }

                return true;
            default:
                return false;
        }
    }

    public bool HasController(ulong entity, byte typecode) => _controllers.Contains((entity & 0xffffffffffffff00, typecode));

    /// <summary>Each view only once until its keyframe arrives.</summary>
    public List<(ulong Entity, byte Typecode)> TakeKeyframeRequests()
    {
        var requests = _toRequest.ToList();
        _toRequest.Clear();
        KeyframesRequested += requests.Count;
        return requests;
    }
}
