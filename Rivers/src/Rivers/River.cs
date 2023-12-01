using System;
using Vintagestory.API.MathTools;

/// <summary>
/// Represents a start and end point and a size of a river.
/// </summary>
public class River : IEquatable<River>
{
    //World coordinates
    public Vec2d startPoint;
    public Vec2d endPoint;

    public int endSize = 0;
    public int startSize = 1;

    public River parentRiver;

    public bool end = false;

    public RiverSegment[] segments;
    public float speed = 10;

    public River(Vec2d startPoint, Vec2d endPoint)
    {
        this.startPoint = startPoint;
        this.endPoint = endPoint;
    }

    public bool Equals(River other)
    {
        if (other == null) return false;
        return startPoint.X == other.startPoint.X && startPoint.Y == other.startPoint.Y && endPoint.X == other.endPoint.X && endPoint.Y == other.endPoint.Y;
    }

    public override int GetHashCode()
    {
        return startPoint.GetHashCode() + endPoint.GetHashCode();
    }
}