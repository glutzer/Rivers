using System.Collections.Generic;
using Vintagestory.API.MathTools;

public class TectonicZone
{
    //Center of region
    public Vec2d localZoneCenterPosition = new();

    //Relative height
    public double height;

    //All rivers, only if it starts in that zone
    public List<River> rivers = new();

    //River generation info
    public int xIndex = 0;
    public int zIndex = 0;
    public bool pathedTo = false;

    public bool ocean = false;
    public bool coastal = false;

    public TectonicZone(int centerPositionX, int centerPositionZ)
    {
        localZoneCenterPosition = new Vec2d(centerPositionX, centerPositionZ);
    }
}