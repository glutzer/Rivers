using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

public class RiversOfBlood : Item
{
    public ICoreClientAPI capi;
    public ICoreServerAPI sapi;

    public long resetTime = 1000;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        capi = api as ICoreClientAPI;
        sapi = api as ICoreServerAPI;
    }

    public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
    {
        //base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);

        // Get current ellapsed world time.
        long time;
        if (capi != null)
        {
            time = capi.ElapsedMilliseconds;
        }
        else
        {
            time = sapi.World.ElapsedMilliseconds;
        }

        // Get time of last attack.
        long lastTime = byEntity.Attributes.GetLong("lastAttackTime", 0);

        // Get difference.
        long delta = time - lastTime;

        // If there's been 1 second since last attack, reset to stage 0.
        if (delta > resetTime) byEntity.Attributes.SetInt("attackStage", 0);

        // Set time of last attack.
        byEntity.Attributes.SetLong("lastAttackTime", time);

        capi?.SendChatMessage($"{delta}");

        handling = EnumHandHandling.PreventDefault;
    }

    public override string GetHeldTpHitAnimation(ItemSlot slot, Entity byEntity)
    {
        // Manually play animations instead.
        return null;
    }
}