using SwiftlyS2.Shared.Natives;

namespace CS2HanZipLine.Services;

internal static class ZiplineMath
{
    public static float DistanceSquared(Vector left, Vector right)
    {
        var x = left.X - right.X;
        var y = left.Y - right.Y;
        var z = left.Z - right.Z;
        return x * x + y * y + z * z;
    }

    public static float LengthSquared(Vector vector) => vector.X * vector.X + vector.Y * vector.Y + vector.Z * vector.Z;

    public static bool TryNormalize(Vector value, out Vector normalized)
    {
        var lengthSquared = LengthSquared(value);
        if (!float.IsFinite(lengthSquared) || lengthSquared <= 0.0001f)
        {
            normalized = Vector.Zero;
            return false;
        }

        var inverseLength = 1.0f / MathF.Sqrt(lengthSquared);
        normalized = value * inverseLength;
        return true;
    }

    public static float Dot(Vector left, Vector right) => left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    public static Vector ClampLength(Vector value, float maxLength)
    {
        var lengthSquared = LengthSquared(value);
        if (lengthSquared <= maxLength * maxLength || lengthSquared <= 0.0001f)
        {
            return value;
        }

        return value * (maxLength / MathF.Sqrt(lengthSquared));
    }

    public static bool IsFinite(Vector value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
