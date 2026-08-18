using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace SoldierBehaviorTweaks
{
    /// <summary>
    /// 战场代理标记器。沿用旧版定时扫描逻辑，避免武器系统被事件热更新持续放大。
    /// </summary>
    public class AgentMarkingSystem : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public Dictionary<int, Agent> ActiveAgentsByIndex { get; } = new();
        public HashSet<int> WeaponRelevantAgentIndices { get; } = new();
        public HashSet<int> CavalryAgentIndices { get; } = new();
        public HashSet<Formation> BannerBearerFormations { get; } = new();

        private float _scanTimer;
        private bool _initialized;
        private int _cleanupCounter;
        private BannerBearerLogic? _subscribedBannerLogic;

        private const float InitialDelay = 2f;
        private const float RefreshInterval = 12f;

        private static readonly HashSet<WeaponClass> ThrowClasses = new()
        {
            WeaponClass.ThrowingAxe, WeaponClass.Javelin, WeaponClass.ThrowingKnife,
        };

        private static readonly HashSet<WeaponClass> TwoHandedClasses = new()
        {
            WeaponClass.TwoHandedSword, WeaponClass.TwoHandedAxe,
        };

        public override void OnMissionTick(float dt)
        {
            Mission mission = Mission;
            if (mission == null || !mission.IsLoadingFinished || !mission.IsDeploymentFinished)
                return;

            if (!_initialized)
            {
                _scanTimer += dt;
                if (_scanTimer < InitialDelay)
                    return;

                FullScan(mission);
                EnsureBannerLogicEvents(mission);
                _initialized = true;
                _scanTimer = 0f;
                return;
            }

            EnsureBannerLogicEvents(mission);

            _scanTimer -= dt;
            if (_scanTimer > 0f)
                return;

            _scanTimer = RefreshInterval;
            IncrementalScan(mission);
        }

        private void FullScan(Mission mission)
        {
            try
            {
                ActiveAgentsByIndex.Clear();
                WeaponRelevantAgentIndices.Clear();
                CavalryAgentIndices.Clear();
                BannerBearerFormations.Clear();

                foreach (Agent agent in mission.Agents)
                {
                    if (!agent.IsActive())
                        continue;

                    ActiveAgentsByIndex[agent.Index] = agent;

                    if (!agent.IsAIControlled)
                        continue;

                    if (agent.HasMount)
                        CavalryAgentIndices.Add(agent.Index);

                    if (HasRelevantWeapons(agent))
                        WeaponRelevantAgentIndices.Add(agent.Index);
                }

                RefreshBannerBearerFormations(mission);
            }
            catch (Exception ex)
            {
                Debug.Print("[SoldierBehaviorTweaks] FullScan error: " + ex.Message);
            }
        }

        private void IncrementalScan(Mission mission)
        {
            try
            {
                foreach (Agent agent in mission.Agents)
                {
                    if (!agent.IsActive())
                        continue;

                    ActiveAgentsByIndex[agent.Index] = agent;

                    if (!agent.IsAIControlled)
                        continue;

                    if (!WeaponRelevantAgentIndices.Contains(agent.Index) && HasRelevantWeapons(agent))
                        WeaponRelevantAgentIndices.Add(agent.Index);

                    if (!CavalryAgentIndices.Contains(agent.Index) && agent.HasMount)
                        CavalryAgentIndices.Add(agent.Index);
                }

                RefreshBannerBearerFormations(mission);

                _cleanupCounter++;
                if (_cleanupCounter % 2 == 0)
                    CleanDeadAgents(mission);
            }
            catch (Exception ex)
            {
                Debug.Print("[SoldierBehaviorTweaks] IncrementalScan error: " + ex.Message);
            }
        }

        private void CleanDeadAgents(Mission mission)
        {
            try
            {
                WeaponRelevantAgentIndices.RemoveWhere(idx =>
                {
                    Agent agent = mission.FindAgentWithIndex(idx);
                    return agent == null || !agent.IsActive();
                });

                CavalryAgentIndices.RemoveWhere(idx =>
                {
                    Agent agent = mission.FindAgentWithIndex(idx);
                    return agent == null || !agent.IsActive() || !agent.HasMount;
                });

                List<int> deadAgentKeys = new();
                foreach (KeyValuePair<int, Agent> pair in ActiveAgentsByIndex)
                {
                    Agent agent = pair.Value;
                    if (agent == null || !agent.IsActive())
                        deadAgentKeys.Add(pair.Key);
                }

                foreach (int key in deadAgentKeys)
                    ActiveAgentsByIndex.Remove(key);
            }
            catch (Exception ex)
            {
                Debug.Print("[SoldierBehaviorTweaks] CleanDeadAgents error: " + ex.Message);
            }
        }

        private void RefreshBannerBearerFormations(Mission mission)
        {
            try
            {
                BannerBearerFormations.Clear();

                SoldierBehaviorTweaksSettings? settings = SoldierBehaviorTweaksSettings.Instance;
                if (settings == null || !settings.EnableBannerBearerDefense)
                    return;

                BannerBearerLogic bannerLogic = mission.GetMissionBehavior<BannerBearerLogic>();
                if (bannerLogic == null)
                    return;

                foreach (Team team in mission.Teams)
                {
                    if (team == null || settings.FriendOnlyBannerBearer && !team.IsPlayerAlly)
                        continue;

                    foreach (Formation formation in team.FormationsIncludingSpecialAndEmpty)
                    {
                        if (formation == null || formation.CountOfUnits <= 0)
                            continue;

                        List<Agent> bearers = bannerLogic.GetFormationBannerBearers(formation);
                        if (bearers != null && bearers.Count > 0)
                            BannerBearerFormations.Add(formation);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print("[SoldierBehaviorTweaks] RefreshBannerBearerFormations error: " + ex.Message);
            }
        }

        public void QueueBannerRefresh(Formation? formation)
        {
            Mission mission = Mission;
            if (mission == null || formation == null)
                return;

            RefreshBannerBearerFormations(mission);
        }

        private void EnsureBannerLogicEvents(Mission mission)
        {
            BannerBearerLogic bannerLogic = mission.GetMissionBehavior<BannerBearerLogic>();
            if (bannerLogic == null || ReferenceEquals(_subscribedBannerLogic, bannerLogic))
                return;

            if (_subscribedBannerLogic != null)
            {
                _subscribedBannerLogic.OnBannerBearersUpdated -= QueueBannerRefresh;
                _subscribedBannerLogic.OnBannerBearerAgentUpdated -= OnBannerBearerAgentUpdated;
            }

            _subscribedBannerLogic = bannerLogic;
            bannerLogic.OnBannerBearersUpdated += QueueBannerRefresh;
            bannerLogic.OnBannerBearerAgentUpdated += OnBannerBearerAgentUpdated;
        }

        private void OnBannerBearerAgentUpdated(Agent agent, bool willBecomeBannerBearer)
        {
            if (agent == null)
                return;

            QueueBannerRefresh(agent.Formation);
        }

        private static bool HasRelevantWeapons(Agent agent)
        {
            try
            {
                MissionEquipment equipment = agent.Equipment;
                if (equipment == null)
                    return false;

                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i <= EquipmentIndex.Weapon3; i++)
                {
                    MissionWeapon weapon = equipment[i];
                    if (weapon.IsEmpty)
                        continue;

                    foreach (WeaponClass weaponClass in ThrowClasses)
                    {
                        if (weapon.HasAnyUsageWithWeaponClass(weaponClass))
                            return true;
                    }

                    foreach (WeaponClass weaponClass in TwoHandedClasses)
                    {
                        if (weapon.HasAnyUsageWithWeaponClass(weaponClass))
                            return true;
                    }

                    if (IsThrustOnlyPolearm(weapon))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool IsThrustOnlyPolearm(MissionWeapon weapon)
        {
            try
            {
                bool hasPolearmClass = false;
                bool hasSwingDamage = false;

                WeaponComponentData firstUsage = weapon.GetWeaponComponentDataForUsage(0);
                if (firstUsage != null
                    && (firstUsage.WeaponClass == WeaponClass.TwoHandedPolearm
                        || firstUsage.WeaponClass == WeaponClass.OneHandedPolearm))
                {
                    hasPolearmClass = true;
                    if (firstUsage.SwingDamage > 0)
                        hasSwingDamage = true;
                }

                try
                {
                    WeaponComponentData secondUsage = weapon.GetWeaponComponentDataForUsage(1);
                    if (secondUsage != null
                        && (secondUsage.WeaponClass == WeaponClass.TwoHandedPolearm
                            || secondUsage.WeaponClass == WeaponClass.OneHandedPolearm))
                    {
                        hasPolearmClass = true;
                        if (secondUsage.SwingDamage > 0)
                            hasSwingDamage = true;
                    }
                }
                catch
                {
                }

                return hasPolearmClass && !hasSwingDamage;
            }
            catch
            {
                return false;
            }
        }

        public override void OnRemoveBehavior()
        {
            if (_subscribedBannerLogic != null)
            {
                _subscribedBannerLogic.OnBannerBearersUpdated -= QueueBannerRefresh;
                _subscribedBannerLogic.OnBannerBearerAgentUpdated -= OnBannerBearerAgentUpdated;
                _subscribedBannerLogic = null;
            }

            ActiveAgentsByIndex.Clear();
            WeaponRelevantAgentIndices.Clear();
            CavalryAgentIndices.Clear();
            BannerBearerFormations.Clear();
            _initialized = false;
            base.OnRemoveBehavior();
        }
    }
}
