using System.Collections.Generic;
using Vintagestory.API.MathTools;

public class RiverSegment
{
    public Vec2d startPoint;
    public Vec2d endPoint;

    public Vec2d midPoint; //For bezier

    public River river;

    public RiverSegment parent;
    public List<RiverSegment> children = new();

    public bool parentInvalid = false;

    public RiverSegment(Vec2d startPoint, Vec2d endPoint, Vec2d midPoint)
    {
        this.startPoint = startPoint;
        this.endPoint = endPoint;
        this.midPoint = midPoint;
    }

    public RiverSegment()
    {
    }
}