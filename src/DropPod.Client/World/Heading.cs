using System.Numerics;

namespace DropPod.Client.World;

/// <summary>
/// The client's yaw turns +Y towards +X, a character with yaw θ faces (sin θ, cos θ). Its pose carries the yaw as a
/// rotation around Z, MovementInput also as a half float in radians.
/// </summary>
public static class Heading
{
    public static float Yaw(Vector2 direction) => MathF.Atan2(direction.X, direction.Y);

    public static Vector3 Forward(float yaw) => new(MathF.Sin(yaw), MathF.Cos(yaw), 0);

    public static Quaternion Rotation(float yaw) => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw);

    // Not AeroMessages' HalfFloat conversion, that one goes through a static buffer and pods run on several threads
    public static ushort Half(float value) => BitConverter.HalfToUInt16Bits((Half)value);
}
