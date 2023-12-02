using Vintagestory.Client.NoObf;

public class TCTCacheTwo : TCTCache
{
    public float[] flowVectorsX;
    public float[] flowVectorsZ;

    public TCTCacheTwo(ChunkTesselator tct) : base(tct)
    {
    }
}