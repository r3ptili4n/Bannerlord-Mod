using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ComponentInterfaces;

namespace SoldierBehaviorTweaks
{
    /// <summary>
    /// 扩展原生旗帜搜索逻辑：当编队旗帜掉得太远，原版短距离搜索无法覆盖时，
    /// 指派附近同编队士兵前往回收旗帜。
    /// </summary>
    public class BannerPickupBehavior : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        private const float CheckInterval = 0.75f;
        private const float SearchRadius = 35f;
        private const float PickupRadius = 2.2f;
        private const float SearcherReassignRadius = 6f;

        private readonly Dictionary<UIntPtr, int> _assignedAgentByBanner = new();
        private float _elapsed;
        private bool? _conflictModDetected;

        public override void OnMissionTick(float dt)
        {
            var mission = Mission;
            if (mission == null) return;
            if (mission.MissionEnded)
            {
                ReleaseAllSearchers(mission);
                return;
            }
            if (!mission.IsLoadingFinished) return;

            SoldierBehaviorTweaksSettings? settings = SoldierBehaviorTweaksSettings.Instance;
            if (settings != null && !settings.EnableBannerPickup)
            {
                ReleaseAllSearchers(mission);
                return;
            }

            _elapsed += dt;
            if (_elapsed < CheckInterval) return;
            _elapsed = 0f;

            if (settings != null && settings.ConflictStrategy == ConflictStrategy.Yield)
            {
                if (!_conflictModDetected.HasValue)
                    _conflictModDetected = MissionBehaviorHelper.IsConflictModActive(mission);
                if (_conflictModDetected.Value)
                {
                    ReleaseAllSearchers(mission);
                    return;
                }
            }

            BannerBearerLogic? bannerLogic = mission.GetMissionBehavior<BannerBearerLogic>();
            if (bannerLogic == null)
            {
                ReleaseAllSearchers(mission);
                return;
            }

            AgentMarkingSystem? marking = mission.GetMissionBehavior<AgentMarkingSystem>();
            if (marking == null)
            {
                ReleaseAllSearchers(mission);
                return;
            }

            try
            {
                PruneAssignments(mission, marking);

                // 原版拾取旗帜会同步移除地面对象，先复制候选列表再执行拾取。
                var banners = new List<SpawnedItemEntity>();
                foreach (MissionObject missionObject in mission.ActiveMissionObjects)
                {
                    if (missionObject is not SpawnedItemEntity spawnedItem) continue;
                    if (spawnedItem.IsRemoved || spawnedItem.IsDisabled || spawnedItem.IsDeactivated) continue;
                    if (!spawnedItem.IsBanner()) continue;
                    banners.Add(spawnedItem);
                }

                var activeBannerKeys = new HashSet<UIntPtr>();
                foreach (SpawnedItemEntity spawnedItem in banners)
                {
                    if (mission.MissionEnded || spawnedItem == null
                        || spawnedItem.IsRemoved || spawnedItem.IsDisabled || spawnedItem.IsDeactivated)
                    {
                        continue;
                    }

                    if (!TryGetBannerEntity(spawnedItem, out WeakGameEntity bannerEntity))
                        continue;

                    UIntPtr bannerKey = bannerEntity.Pointer;
                    activeBannerKeys.Add(bannerKey);
                    Vec2 bannerPosition = bannerEntity.GlobalPosition.AsVec2;

                    Formation formation = bannerLogic.GetFormationFromBanner(spawnedItem);
                    if (!IsEligibleFormation(formation, settings)
                        || !bannerLogic.IsFormationBanner(formation, spawnedItem))
                    {
                        ReleaseAssignedSearcher(mission, bannerKey);
                        continue;
                    }

                    Agent? searcher = GetAssignedSearcher(bannerKey, formation, mission, marking);
                    if (searcher == null)
                    {
                        searcher = FindBestSearcher(formation, bannerPosition, marking);
                        if (searcher == null) continue;
                        _assignedAgentByBanner[bannerKey] = searcher.Index;
                    }

                    float distance = searcher.Position.AsVec2.Distance(bannerPosition);
                    if (distance <= PickupRadius)
                    {
                        TryPickUpBanner(searcher, mission, spawnedItem, bannerKey);
                    }
                    else if (distance <= SearchRadius + SearcherReassignRadius)
                    {
                        SendSearcherToBanner(searcher, mission, spawnedItem);
                    }
                    else
                    {
                        ReleaseSearcher(searcher, bannerKey);
                    }
                }

                // 当前帧已经不存在的旗帜，其搜索者不能继续保留脚本移动。
                ReleaseAssignmentsForMissingBanners(mission, activeBannerKeys);
            }
            catch (Exception ex)
            {
                Debug.Print($"[SoldierBehaviorTweaks] BannerPickupBehavior tick error: {ex.Message}");
            }
        }

        private static bool IsEligibleFormation(Formation? formation, SoldierBehaviorTweaksSettings? settings)
        {
            if (formation == null || formation.CountOfUnits <= 0) return false;

            Team? team = formation.Team;
            if (team == null) return false;

            bool friendOnly = settings == null || settings.FriendOnlyBannerPickup;
            if (friendOnly && !team.IsPlayerTeam && !team.IsPlayerAlly) return false;

            // F6 委派后的玩家侧编队是此前队长分配崩溃的来源。
            // 这里避免脚本补捡，原版仍可自行拾取近处旗帜。
            if ((team.IsPlayerTeam || team.IsPlayerAlly) && formation.IsAIControlled) return false;

            return true;
        }

        private void PruneAssignments(Mission mission, AgentMarkingSystem marking)
        {
            var stale = new List<KeyValuePair<UIntPtr, int>>();
            foreach (var kv in _assignedAgentByBanner)
            {
                if (!marking.ActiveAgentsByIndex.TryGetValue(kv.Value, out Agent? agent)
                    || agent == null
                    || !agent.IsActive())
                {
                    stale.Add(kv);
                }
            }

            foreach (var kv in stale)
            {
                Agent? agent = mission.FindAgentWithIndex(kv.Value);
                ReleaseSearcher(agent, kv.Key);
            }
        }

        private Agent? GetAssignedSearcher(UIntPtr bannerKey, Formation formation, Mission mission,
            AgentMarkingSystem marking)
        {
            if (!_assignedAgentByBanner.TryGetValue(bannerKey, out int agentIndex))
                return null;

            if (!marking.ActiveAgentsByIndex.TryGetValue(agentIndex, out Agent? agent))
            {
                ReleaseSearcher(mission.FindAgentWithIndex(agentIndex), bannerKey);
                return null;
            }

            if (!IsEligibleSearcher(agent, formation))
            {
                ReleaseSearcher(agent, bannerKey);
                return null;
            }

            return agent;
        }

        private static bool TryGetBannerEntity(SpawnedItemEntity spawnedItem, out WeakGameEntity bannerEntity)
        {
            bannerEntity = spawnedItem.GameEntity;
            if (!bannerEntity.IsValid)
                bannerEntity = spawnedItem.GameEntityWithWorldPosition.GameEntity;
            return bannerEntity.IsValid;
        }

        private void ReleaseAssignedSearcher(Mission? mission, UIntPtr bannerKey)
        {
            if (!_assignedAgentByBanner.TryGetValue(bannerKey, out int agentIndex))
                return;

            Agent? searcher = mission == null ? null : mission.FindAgentWithIndex(agentIndex);
            ReleaseSearcher(searcher, bannerKey);
        }

        private void ReleaseAssignmentsForMissingBanners(Mission mission, HashSet<UIntPtr> activeBannerKeys)
        {
            var missing = new List<UIntPtr>();
            foreach (UIntPtr key in _assignedAgentByBanner.Keys)
            {
                if (!activeBannerKeys.Contains(key))
                    missing.Add(key);
            }

            foreach (UIntPtr key in missing)
                ReleaseAssignedSearcher(mission, key);
        }

        private static Agent? FindBestSearcher(Formation formation, Vec2 bannerPosition, AgentMarkingSystem marking)
        {
            Agent? best = null;
            int bestPriority = 0;
            float bestDistance = SearchRadius;
            BattleBannerBearersModel model = MissionGameModels.Current.BattleBannerBearersModel;

            foreach (Agent agent in marking.ActiveAgentsByIndex.Values)
            {
                if (!IsEligibleSearcher(agent, formation)) continue;

                float distance = agent.Position.AsVec2.Distance(bannerPosition);
                if (distance > SearchRadius) continue;

                int priority = model.GetAgentBannerBearingPriority(agent);
                if (priority <= 0) continue;

                if (best == null || priority > bestPriority || priority == bestPriority && distance < bestDistance)
                {
                    best = agent;
                    bestPriority = priority;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static bool IsEligibleSearcher(Agent? agent, Formation formation)
        {
            if (formation == null || formation.Team == null || formation.Team.Mission == null) return false;
            if (agent == null || !agent.IsActive()) return false;
            if (agent.Mission != formation.Team.Mission) return false;
            if (agent.Formation != formation) return false;
            if (!agent.IsAIControlled || agent.IsMainAgent) return false;
            if (agent.Banner != null) return false;

            BattleBannerBearersModel model = MissionGameModels.Current.BattleBannerBearersModel;
            return model.CanAgentPickUpAnyBanner(agent) && model.CanAgentBecomeBannerBearer(agent);
        }

        private static void SendSearcherToBanner(Agent searcher, Mission mission, SpawnedItemEntity spawnedItem)
        {
            if (mission.MissionEnded || !searcher.IsActive() || searcher.Mission != mission
                || spawnedItem.IsRemoved || spawnedItem.IsDisabled || spawnedItem.IsDeactivated)
                return;

            WeakGameEntity bannerEntity = spawnedItem.GameEntity;
            if (!bannerEntity.IsValid)
                bannerEntity = spawnedItem.GameEntityWithWorldPosition.GameEntity;
            if (!bannerEntity.IsValid)
                return;

            Vec3 target = bannerEntity.GlobalPosition;
            WorldPosition targetPosition = new WorldPosition(mission.Scene, target);
            searcher.SetScriptedPosition(ref targetPosition, false, Agent.AIScriptedFrameFlags.GoToPosition);
        }

        private void TryPickUpBanner(Agent searcher, Mission mission, SpawnedItemEntity spawnedItem, UIntPtr bannerKey)
        {
            try
            {
                if (mission.MissionEnded || !searcher.IsActive() || searcher.Mission != mission
                    || searcher.Formation == null || spawnedItem.IsRemoved
                    || spawnedItem.IsDisabled || spawnedItem.IsDeactivated)
                {
                    ReleaseSearcher(searcher, bannerKey);
                    return;
                }

                if (!searcher.CanQuickPickUp(spawnedItem)) return;

                try
                {
                    // OnItemPickup 会立即改变原版地面物品集合，之后不再读取旗帜对象。
                    searcher.OnItemPickup(spawnedItem, EquipmentIndex.ExtraWeaponSlot, out bool _);
                    mission.GetMissionBehavior<AgentMarkingSystem>()?.QueueBannerRefresh(searcher.Formation);
                }
                finally
                {
                    ReleaseSearcher(searcher, bannerKey);
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[SoldierBehaviorTweaks] Banner pickup error: {ex.Message}");
            }
        }
        private void ReleaseSearcher(Agent? searcher, UIntPtr bannerKey)
        {
            try
            {
                if (searcher != null && searcher.IsActive())
                    searcher.DisableScriptedMovement();
            }
            catch { }

            _assignedAgentByBanner.Remove(bannerKey);
        }

        private void ReleaseAllSearchers(Mission? mission)
        {
            var agentIndices = new HashSet<int>(_assignedAgentByBanner.Values);
            try
            {
                foreach (int agentIndex in agentIndices)
                {
                    try
                    {
                        Agent? searcher = mission == null ? null : mission.FindAgentWithIndex(agentIndex);
                        if (searcher != null && searcher.IsActive())
                            searcher.DisableScriptedMovement();
                    }
                    catch (Exception ex)
                    {
                        Debug.Print($"[SoldierBehaviorTweaks] Searcher release error: {ex.Message}");
                    }
                }
            }
            finally
            {
                _assignedAgentByBanner.Clear();
            }
        }

        public override void OnRemoveBehavior()
        {
            ReleaseAllSearchers(base.Mission);
            _conflictModDetected = null;
            base.OnRemoveBehavior();
        }

        protected override void OnEndMission()
        {
            ReleaseAllSearchers(base.Mission);
            _conflictModDetected = null;
            base.OnEndMission();
        }
    }
}
