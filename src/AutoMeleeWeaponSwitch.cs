using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace SoldierBehaviorTweaks
{
    public class AutoMeleeWeaponSwitch : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        private float _elapsed;
        private const float CheckInterval = 1f;
        private int _tickCount;
        private const float MeleeRange = 2f;
        private const float CavalryDetectionRange = 10f;

        private bool? _isCombatMission;
        private bool? _conflictModDetected;

        private readonly Dictionary<int, EquipmentIndex> _throwNoMeleeFixed = new();
        private readonly List<(Vec2 Position, Team Team)> _cavalryCache = new();
        private readonly Dictionary<int, float> _lastSwitchTime = new();
        private const float SwitchCooldown = 3f;
        private const float ThrustPolearmFavorMultiplier = 0f;

        private static readonly HashSet<WeaponClass> ThrowingClasses = new()
        {
            WeaponClass.ThrowingAxe, WeaponClass.Javelin, WeaponClass.ThrowingKnife,
        };

        private static readonly HashSet<WeaponClass> TwoHandedClasses = new()
        {
            WeaponClass.TwoHandedSword, WeaponClass.TwoHandedAxe,
        };

        private static readonly HashSet<WeaponClass> OneHandedClasses = new()
        {
            WeaponClass.OneHandedSword, WeaponClass.OneHandedAxe, WeaponClass.OneHandedPolearm,
        };

        private static readonly HashSet<WeaponClass> PreferTwoHandedClasses = new()
        {
            WeaponClass.TwoHandedSword, WeaponClass.TwoHandedAxe,
        };

        private static readonly HashSet<WeaponClass> ShieldClasses = new()
        {
            WeaponClass.SmallShield, WeaponClass.LargeShield,
        };

        private static readonly HashSet<WeaponClass> RangedClasses = new()
        {
            WeaponClass.Bow, WeaponClass.Crossbow,
        };

        private static bool HasAnyUsageClass(MissionWeapon weapon, HashSet<WeaponClass> classes)
        {
            foreach (WeaponClass weaponClass in classes)
            {
                if (weapon.HasAnyUsageWithWeaponClass(weaponClass))
                    return true;
            }

            return false;
        }

        private static bool IsTwoHandedWeapon(MissionWeapon weapon)
            => HasAnyUsageClass(weapon, TwoHandedClasses);

        private static bool IsOneHandedWeapon(MissionWeapon weapon)
            => HasAnyUsageClass(weapon, OneHandedClasses);

        private static bool IsThrowingWeapon(MissionWeapon weapon)
            => HasAnyUsageClass(weapon, ThrowingClasses);

        private static bool IsRangedWeapon(MissionWeapon weapon)
            => HasAnyUsageClass(weapon, RangedClasses);

        private static bool IsShield(MissionWeapon weapon)
            => HasAnyUsageClass(weapon, ShieldClasses);

        public override void OnAgentBuild(Agent agent, Banner banner)
        {
            if (agent == null || !agent.IsAIControlled || agent.IsMainAgent || agent.HasMount)
                return;

            SoldierBehaviorTweaksSettings? settings = SoldierBehaviorTweaksSettings.Instance;
            if (settings == null || !settings.DiscourageThrustPolearm)
                return;

            if (settings.FriendOnlyDiscourageThrustPolearm && agent.Team != null && !agent.Team.IsPlayerAlly)
                return;

            MissionEquipment equipment = agent.Equipment;
            if (equipment != null && HasThrustOnlyPolearm(equipment) && HasBetterThanThrustPolearm(equipment))
                SuppressPolearmSelection(agent);
        }

        public override void OnMissionTick(float dt)
        {
            Mission mission = Mission;
            if (mission == null || !mission.IsLoadingFinished)
                return;

            if (!IsCombatMission())
                return;

            _elapsed += dt;
            if (_elapsed < CheckInterval)
                return;

            _elapsed = 0f;

            SoldierBehaviorTweaksSettings? settings = SoldierBehaviorTweaksSettings.Instance;
            if (settings != null && settings.ConflictStrategy == ConflictStrategy.Yield)
            {
                if (!_conflictModDetected.HasValue)
                    _conflictModDetected = MissionBehaviorHelper.IsConflictModActive(mission);

                if (_conflictModDetected.Value)
                    return;
            }

            AgentMarkingSystem marking = mission.GetMissionBehavior<AgentMarkingSystem>();
            if (marking == null)
                return;

            try
            {
                _cavalryCache.Clear();
                foreach (int agentId in marking.CavalryAgentIndices)
                {
                    Agent agent = mission.FindAgentWithIndex(agentId);
                    if (agent != null && agent.IsActive() && agent.Team != null && agent.HasMount)
                        _cavalryCache.Add((agent.Position.AsVec2, agent.Team));
                }

                bool preferTwoHanded = settings != null && settings.PreferTwoHanded;
                bool discourageThrustPolearm = settings != null && settings.DiscourageThrustPolearm;
                bool enableThrowNoMelee = settings == null || settings.EnableThrowNoMelee;
                bool friendOnlyThrowNoMelee = settings != null && settings.FriendOnlyThrowNoMelee;
                bool friendOnlyPreferTwoHanded = settings != null && settings.FriendOnlyPreferTwoHanded;
                bool friendOnlyDiscourageThrustPolearm = settings != null && settings.FriendOnlyDiscourageThrustPolearm;

                foreach (int agentId in marking.WeaponRelevantAgentIndices)
                {
                    Agent agent = mission.FindAgentWithIndex(agentId);
                    if (agent == null || !agent.IsAIControlled || !agent.IsActive() || agent.IsMainAgent)
                        continue;

                    MissionEquipment equipment = agent.Equipment;
                    if (equipment == null)
                        continue;

                    EquipmentIndex currentSlot = agent.GetPrimaryWieldedItemIndex();
                    if (currentSlot == EquipmentIndex.None)
                        continue;

                    MissionWeapon weapon = equipment[currentSlot];
                    if (weapon.IsEmpty)
                        continue;

                    if (IsThrowingWeapon(weapon))
                    {
                        bool applyThrowNoMelee = enableThrowNoMelee;
                        if (friendOnlyThrowNoMelee && agent.Team != null && !agent.Team.IsPlayerAlly)
                            applyThrowNoMelee = false;

                        if (applyThrowNoMelee
                            && (!_throwNoMeleeFixed.TryGetValue(agent.Index, out EquipmentIndex fixedSlot)
                                || fixedSlot != currentSlot))
                        {
                            int rangedUsageIndex = weapon.GetRangedUsageIndex();
                            agent.SetUsageIndexOfWeaponInSlotAsClient(currentSlot, rangedUsageIndex);
                            _throwNoMeleeFixed[agent.Index] = currentSlot;
                        }

                        Agent target = agent.GetTargetAgent();
                        if (target == null || !target.IsActive() || agent.Position.Distance(target.Position) > MeleeRange)
                            continue;

                        EquipmentIndex meleeSlot = FindBestMeleeSlot(equipment, currentSlot, preferTwoHanded, discourageThrustPolearm);
                        if (meleeSlot == EquipmentIndex.None)
                            continue;

                        SwitchToSlot(agent, meleeSlot);
                        currentSlot = meleeSlot;
                        weapon = equipment[meleeSlot];
                    }
                    else
                    {
                        _throwNoMeleeFixed.Remove(agent.Index);
                    }

                    if (preferTwoHanded && !agent.HasMount
                        && (!friendOnlyPreferTwoHanded || agent.Team == null || agent.Team.IsPlayerAlly))
                    {
                        bool hasNearbyEnemy = agent.Team != null
                            && mission.GetNearbyEnemyAgentCount(agent.Team, agent.Position.AsVec2, 7f) > 0;
                        bool hasNearbyCavalry = !hasNearbyEnemy && agent.Team != null
                            && HasNearbyEnemyCavalry(agent, CavalryDetectionRange);

                        if ((hasNearbyEnemy || hasNearbyCavalry) && !IsTwoHandedWeapon(weapon))
                        {
                            EquipmentIndex twoHandedSlot = FindTwoHandedUpgrade(equipment, currentSlot);
                            if (twoHandedSlot != EquipmentIndex.None)
                            {
                                SwitchToSlot(agent, twoHandedSlot);
                                currentSlot = twoHandedSlot;
                                weapon = equipment[twoHandedSlot];
                            }
                        }
                    }

                    if (discourageThrustPolearm && !agent.HasMount
                        && (!friendOnlyDiscourageThrustPolearm || agent.Team == null || agent.Team.IsPlayerAlly))
                    {
                        bool hasThrustOnlyPolearm = HasThrustOnlyPolearm(equipment);
                        bool hasBetterWeapon = hasThrustOnlyPolearm && HasBetterThanThrustPolearm(equipment);
                        if (hasBetterWeapon)
                            SuppressPolearmSelection(agent);

                        if (IsThrustOnlyPolearm(weapon)
                            && (!_lastSwitchTime.TryGetValue(agent.Index, out float lastSwitch)
                                || mission.CurrentTime - lastSwitch >= SwitchCooldown))
                        {
                            if (hasBetterWeapon)
                            {
                                EquipmentIndex betterSlot = FindBetterThanThrustPolearm(equipment, currentSlot);
                                if (betterSlot != EquipmentIndex.None)
                                {
                                    SwitchToSlot(agent, betterSlot);
                                    SuppressPolearmSelection(agent);
                                    _lastSwitchTime[agent.Index] = mission.CurrentTime;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print("[SoldierBehaviorTweaks] AutoMeleeWeaponSwitch tick error: " + ex.Message);
            }

            _tickCount++;
            if (_tickCount % 5 != 0)
                return;

            try
            {
                List<int> deadKeys = new();
                foreach (KeyValuePair<int, EquipmentIndex> pair in _throwNoMeleeFixed)
                {
                    Agent agent = mission.FindAgentWithIndex(pair.Key);
                    if (agent == null || !agent.IsActive())
                        deadKeys.Add(pair.Key);
                }

                foreach (int key in deadKeys)
                    _throwNoMeleeFixed.Remove(key);

                deadKeys.Clear();
                foreach (KeyValuePair<int, float> pair in _lastSwitchTime)
                {
                    Agent agent = mission.FindAgentWithIndex(pair.Key);
                    if (agent == null || !agent.IsActive())
                        deadKeys.Add(pair.Key);
                }

                foreach (int key in deadKeys)
                    _lastSwitchTime.Remove(key);
            }
            catch (Exception ex)
            {
                Debug.Print("[SoldierBehaviorTweaks] AutoMeleeWeaponSwitch cleanup error: " + ex.Message);
            }
        }

        private static EquipmentIndex FindBestMeleeSlot(MissionEquipment eq, EquipmentIndex excludeSlot,
            bool preferTwoHanded, bool discourageThrust)
        {
            EquipmentIndex result = EquipmentIndex.None;

            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i <= EquipmentIndex.Weapon3; i++)
            {
                if (i == excludeSlot)
                    continue;

                MissionWeapon weapon = eq[i];
                if (!weapon.IsEmpty && !IsThrowingWeapon(weapon) && !IsRangedWeapon(weapon)
                    && !IsShield(weapon)
                    && (!discourageThrust || !IsThrustOnlyPolearm(weapon)))
                {
                    if (preferTwoHanded && HasAnyUsageClass(weapon, PreferTwoHandedClasses))
                        return i;

                    if (result == EquipmentIndex.None)
                        result = i;
                }
            }

            return result;
        }

        private static EquipmentIndex FindTwoHandedUpgrade(MissionEquipment eq, EquipmentIndex currentSlot)
        {
            MissionWeapon currentWeapon = eq[currentSlot];
            if (currentWeapon.IsEmpty)
                return EquipmentIndex.None;

            if (!IsOneHandedWeapon(currentWeapon))
                return EquipmentIndex.None;

            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i <= EquipmentIndex.Weapon3; i++)
            {
                if (i == currentSlot)
                    continue;

                MissionWeapon weapon = eq[i];
                if (!weapon.IsEmpty && HasAnyUsageClass(weapon, PreferTwoHandedClasses))
                    return i;
            }

            return EquipmentIndex.None;
        }

        private static EquipmentIndex FindBetterThanThrustPolearm(MissionEquipment eq, EquipmentIndex currentSlot)
        {
            EquipmentIndex result = EquipmentIndex.None;
            int bestScore = -1;

            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i <= EquipmentIndex.Weapon3; i++)
            {
                if (i == currentSlot)
                    continue;

                MissionWeapon weapon = eq[i];
                if (!weapon.IsEmpty && !IsThrowingWeapon(weapon) && !IsRangedWeapon(weapon)
                    && !IsShield(weapon) && !IsThrustOnlyPolearm(weapon))
                {
                    int score = IsTwoHandedWeapon(weapon) ? 2 : 1;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        result = i;
                    }
                }
            }

            return result;
        }

        private static bool HasBetterThanThrustPolearm(MissionEquipment eq)
        {
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i <= EquipmentIndex.Weapon3; i++)
            {
                MissionWeapon weapon = eq[i];
                if (!weapon.IsEmpty && !IsThrowingWeapon(weapon) && !IsRangedWeapon(weapon)
                    && !IsShield(weapon) && !IsThrustOnlyPolearm(weapon))
                    return true;
            }

            return false;
        }

        private static bool HasThrustOnlyPolearm(MissionEquipment eq)
        {
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i <= EquipmentIndex.Weapon3; i++)
            {
                MissionWeapon weapon = eq[i];
                if (!weapon.IsEmpty && IsThrustOnlyPolearm(weapon))
                    return true;
            }

            return false;
        }

        private static bool IsThrustOnlyPolearm(MissionWeapon weapon)
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

        private static void SwitchToSlot(Agent agent, EquipmentIndex slot)
        {
            agent.SetWieldedItemIndexAsClient(Agent.HandIndex.MainHand, slot, true, false, 0);
        }

        private static void SuppressPolearmSelection(Agent agent)
        {
            AgentDrivenProperties properties = agent.AgentDrivenProperties;
            if (properties == null)
                return;

            if (properties.AiWeaponFavorMultiplierPolearm <= ThrustPolearmFavorMultiplier)
                return;

            properties.AiWeaponFavorMultiplierPolearm = ThrustPolearmFavorMultiplier;
            agent.UpdateCustomDrivenProperties();
            agent.InvalidateAIWeaponSelections();
        }

        private bool HasNearbyEnemyCavalry(Agent agent, float range)
        {
            if (agent.Team == null)
                return false;

            Vec2 center = agent.Position.AsVec2;
            float rangeSq = range * range;

            foreach ((Vec2 position, Team team) in _cavalryCache)
            {
                if (team.IsEnemyOf(agent.Team) && position.DistanceSquared(center) <= rangeSq)
                    return true;
            }

            return false;
        }

        private bool IsCombatMission()
        {
            if (_isCombatMission.HasValue)
                return _isCombatMission.Value;

            _isCombatMission = false;
            Mission mission = Mission;
            if (mission == null || mission.Teams == null)
                return false;

            Team? playerTeam = null;
            foreach (Team team in mission.Teams)
            {
                if (team != null && team.IsPlayerTeam)
                {
                    playerTeam = team;
                    break;
                }
            }

            if (playerTeam == null)
                return false;

            foreach (Team team in mission.Teams)
            {
                if (team != null && team != playerTeam && team.IsEnemyOf(playerTeam))
                {
                    _isCombatMission = true;
                    break;
                }
            }

            return _isCombatMission.Value;
        }

        public override void OnRemoveBehavior()
        {
            _throwNoMeleeFixed.Clear();
            _lastSwitchTime.Clear();
            _cavalryCache.Clear();
            base.OnRemoveBehavior();
        }
    }
}
