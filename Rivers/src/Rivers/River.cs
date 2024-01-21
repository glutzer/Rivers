using System.Collections.Generic;
using Vintagestory.API.MathTools;

public class River
{
    public int radius = 0;

    // Target zone, the farthest zone from the ocean.
    public TectonicZone riverTarget;
    public Vec2d startPos;

    public List<RiverNode> nodes = new();
}