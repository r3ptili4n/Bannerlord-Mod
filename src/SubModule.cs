using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace SoldierBehaviorTweaks
{
    public class SubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            Debug.Print("[SoldierBehaviorTweaks] loaded");
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            mission.AddMissionBehavior(new AgentMarkingSystem());
            mission.AddMissionBehavior(new BannerBearerDefenseGuard());
            mission.AddMissionBehavior(new BannerPickupBehavior());
            mission.AddMissionBehavior(new AutoMeleeWeaponSwitch());
            mission.AddMissionBehavior(new CrouchToggleBehavior());
        }
    }
}
