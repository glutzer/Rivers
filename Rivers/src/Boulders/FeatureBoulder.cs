using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

public class FeatureBoulder : PartialFeature
{
    public FeatureBoulder(ICoreServerAPI sapi) : base(sapi)
    {
    }

    public override void Generate(BlockPos blockPos, IServerChunk[] chunkData, LCGRandom rand, Vec2d chunkStart, Vec2d chunkEnd, IBlockAccessor blockAccessor, int rockId)
    {
        double xSize = hSize + (rand.NextFloat() * hSizeVariance);
        double ySize = hSize + (rand.NextFloat() * hSizeVariance);
        double zSize = hSize + (rand.NextFloat() * hSizeVariance);

        if (blockPos.Y < TerraGenConfig.seaLevel) ySize += (TerraGenConfig.seaLevel - blockPos.Y) * 2;

        blockPos.Sub(0, (int)(ySize / 2), 0);

        FeatureBoundingBox box = new(blockPos.ToVec3d().Add(-xSize - 2, -ySize - 2, -zSize - 2), blockPos.ToVec3d().Add(xSize + 2, ySize + 2, zSize + 2));

        if (!box.SetBounds(chunkStart, chunkEnd)) return;

        Vec3d centerPos = blockPos.ToVec3d();

        box.ForEachPosition((x, y, z, cPos) =>
        {
            float value = noise.GetPosNoise(x, y, z);
            if (DistanceToEllipsoid(x, y, z, blockPos.X, blockPos.Y, blockPos.Z, xSize, ySize, zSize) + (value / 2) <= 1)
            {
                blockAccessor.SetBlock(rockId, new BlockPos(x, y, z));
            }
        });
    }

    public override bool CanGenerate(int localX, int posY, int localZ, ushort riverDistance)
    {
        return riverDistance > 10 && posY < TerraGenConfig.seaLevel + 20 && posY > TerraGenConfig.seaLevel;
    }

    public static double DistanceToEllipsoid(double worldX, double worldY, double worldZ, double centerX, double centerY, double centerZ, double xSize, double ySize, double zSize)
    {
        double translatedX = worldX - centerX;
        double translatedY = worldY - centerY;
        double translatedZ = worldZ - centerZ;

        double normX = translatedX / xSize;
        double normY = translatedY / ySize;
        double normZ = translatedZ / zSize;

        return (normX * normX) + (normY * normY) + (normZ * normZ);
    }
}