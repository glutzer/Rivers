using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

/// <summary>
/// Uses in things like deposits. Does not generate for an entire chunk column.
/// </summary>
public abstract class WorldGenPartial : WorldGenBase
{
    public ICoreServerAPI sapi;
    public LCGRandom chunkRand;

    public abstract int chunkRange { get; }

    public virtual void ChunkColumnGeneration(IChunkColumnGenerateRequest request)
    {
        IServerChunk[] chunks = request.Chunks;
        int chunkX = request.ChunkX;
        int chunkZ = request.ChunkZ;
        for (int i = -chunkRange; i <= chunkRange; i++)
        {
            for (int j = -chunkRange; j <= chunkRange; j++)
            {
                GeneratePartial(chunks, chunkX, chunkZ, chunkX + i, chunkZ + j);
            }
        }
    }

    public virtual void GeneratePartial(IServerChunk[] chunks, int mainChunkX, int mainChunkZ, int generatingChunkX, int generatingChunkZ)
    {
    }
}