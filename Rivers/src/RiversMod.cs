using HarmonyLib;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

public class RiversMod : ModSystem
{
    public Harmony harmony;
    public static bool patched = false;
    public bool patchedLocal = false;
    public bool devEnvironment = false;

    public override double ExecuteOrder()
    {
        return 0;
    }

    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockClass("BlockRiverWaterWheel", typeof(BlockWaterWheel));
        api.RegisterBlockEntityClass("BERiverWaterWheel", typeof(BEWaterWheel));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorRiverWaterWheel", typeof(BEBehaviorWaterWheel));

        api.RegisterBlockBehaviorClass("riverblock", typeof(RiverBlockBehavior));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.RegisterCommand(new RiverDebugCommand(api));
    }

    public override void StartPre(ICoreAPI api)
    {
        if (!patched)
        {
            harmony = new Harmony("rivers");
            harmony.PatchAll();
            patched = true;
            patchedLocal = true;
        }

        string cfgFileName = "rivers.json";
        try
        {
            RiverConfig fromDisk;
            if ((fromDisk = api.LoadModConfig<RiverConfig>(cfgFileName)) == null || devEnvironment)
            {
                api.StoreModConfig(RiverConfig.Loaded, cfgFileName);
            }
            else
            {
                RiverConfig.Loaded = fromDisk;
            }
        }
        catch
        {
            api.StoreModConfig(RiverConfig.Loaded, cfgFileName);
        }
    }

    public override void Dispose()
    {
        ChunkTesselatorManagerPatch.bottomChunk = null;
        BlockLayersPatches.flowVectorsX = null;
        BlockLayersPatches.flowVectorsZ = null;
        if (patchedLocal)
        {
            harmony.UnpatchAll("rivers");
            patched = false;
            patchedLocal = false;
        }
    }
}

public class RiverDebugCommand : ServerChatCommand
{
    public ICoreServerAPI sapi;

    public RiverDebugCommand(ICoreServerAPI sapi)
    {
        this.sapi = sapi;

        Command = "riverdebug";
        Description = "Debug command for rivers";
        Syntax = "/riverdebug";

        RequiredPrivilege = Privilege.ban;
    }

    public static void AddWaypoint(WaypointMapLayer waypointMapLayer, string type, Vec3d worldPos, string playerUid, IPlayer player, int r, int g, int b, string name)
    {
        waypointMapLayer.AddWaypoint(new Waypoint
        {
            Color = ColorUtil.ColorFromRgba(r, g, b, 255),
            Icon = type,
            Pinned = true,
            Position = worldPos,
            OwningPlayerUid = playerUid,
            Title = name
        }, (IServerPlayer)player);
    }

    public override void CallHandler(IPlayer player, int groupId, CmdArgs args)
    {
        try
        {
            WaypointMapLayer wp = sapi.ModLoader.GetModSystem<WorldMapManager>().MapLayers.FirstOrDefault((MapLayer ml) => ml is WaypointMapLayer) as WaypointMapLayer;

            int worldX = (int)player.Entity.Pos.X;
            int worldZ = (int)player.Entity.Pos.Z;
            int chunkX = worldX / 32;
            int chunkZ = worldZ / 32;

            int chunksInPlate = RiverConfig.Loaded.zonesInPlate * RiverConfig.Loaded.zoneSize / 32;

            int plateX = chunkX / chunksInPlate;
            int plateZ = chunkZ / chunksInPlate;

            TectonicPlate plate = ObjectCacheUtil.GetOrCreate(sapi, plateX.ToString() + "+" + plateZ.ToString(), () =>
            {
                return new TectonicPlate(sapi, plateX, plateZ);
            });

            Vec2d plateStart = plate.globalPlateStart;

            foreach (RiverSegment segment in plate.riverStarts)
            {
                int r = sapi.World.Rand.Next(255);
                int g = sapi.World.Rand.Next(255);
                int b = sapi.World.Rand.Next(255);
                MapRiver(wp, segment, r, g, b, player, plateStart);
            }

            sapi.SendMessage(player, 0, $"{riversMapped} rivers, {biggestRiver} biggest. {biggestX}, {biggestZ}.", EnumChatType.Notification);
            riversMapped = 0;
            biggestRiver = 0;
            biggestX = 0;
            biggestZ = 0;
        }
        catch
        {

        }
    }

    public static int riversMapped = 0;
    public static int biggestRiver = 0;
    public static int biggestX = 0;
    public static int biggestZ = 0;

    public static void MapRiver(WaypointMapLayer wp, RiverSegment segment, int r, int g, int b, IPlayer player, Vec2d plateStart)
    {
        AddWaypoint(wp, "x", new Vec3d(segment.startPoint.X + plateStart.X, 0, segment.startPoint.Y + plateStart.Y), player.PlayerUID, player, r, g, b, $"{segment.river.startSize}");

        if (segment.river.startSize > biggestRiver)
        {
            biggestRiver = (int)segment.river.startSize;
            biggestX = (int)(segment.startPoint.X + plateStart.X);
            biggestZ = (int)(segment.startPoint.Y + plateStart.Y);
        }

        riversMapped++;
    }
}