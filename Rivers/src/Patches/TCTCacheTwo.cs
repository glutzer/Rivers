using Vintagestory.Client.NoObf;

public class TCTCacheTwo : TCTCache
{
    public float[] flowVectorsX;
    public float[] flowVectorsZ;
    public float riverSpeed;

    public TCTCacheTwo(ChunkTesselator tct) : base(tct)
    {
    }
}