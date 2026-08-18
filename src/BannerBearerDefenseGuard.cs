using System;
using System.Collections.Generic;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace SoldierBehaviorTweaks
{
    public class BannerBearerDefenseGuard : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        private float _elapsed = -0.3f;
        private const float CheckInterval = 1f;
        private const float RearDistance = 1f;
        private const float LooseRearSideOffsetMin = 1.2f;
        private const float ChargePushDistance = 3f;
        private const float ChargeEnemyMaxDist = 10f;

        private bool? _conflictModDetected;

        // F6 委派 / AI 接管防护。
        // F6 委派触发 SetMovementOrder 时，OnBeforeMovementOrderApplied 事件会最先触发。
        // 此时清除旗手残留的脚本移动状态，避免原生人工智能接管路径处理旧脚本移动时崩溃。
        private readonly Dictionary<Formation, Action<Formation, MovementOrder.MovementOrderEnum>> _orderGuards = new();
        private readonly HashSet<Formation> _guardedFormations = new();
        private readonly HashSet<Formation> _pendingBearerRelease = new();

        private void EnsureOrderGuard(Formation formation)
        {
            if (formation == null || !_guardedFormations.Add(formation)) return;

            Action<Formation, MovementOrder.MovementOrderEnum> handler = (f, orderEnum) =>
            {
                try
                {
                    if (f == null || f.CountOfUnits <= 0) return;
                    _pendingBearerRelease.Add(f);
                }
                catch (Exception ex)
                {
                    Debug.Print($"[SoldierBehaviorTweaks] OrderGuard cleanup error: {ex.Message}");
                }
            };

            _orderGuards[formation] = handler;
            formation.OnBeforeMovementOrderApplied += handler;
        }

        public override void OnMissionTick(float dt)
        {
            var mission = Mission;
            if (mission == null || !mission.IsLoadingFinished) return;

            _elapsed += dt;
            if (_elapsed < CheckInterval) return;
            _elapsed = 0f;

            var settings = SoldierBehaviorTweaksSettings.Instance;
            if (settings != null && !settings.EnableBannerBearerDefense) return;

            // 冲突策略保护。
            if (settings != null && settings.ConflictStrategy == ConflictStrategy.Yield)
            {
                if (!_conflictModDetected.HasValue)
                    _conflictModDetected = MissionBehaviorHelper.IsConflictModActive(mission);
                if (_conflictModDetected.Value) return;
            }

            var bannerLogic = mission.GetMissionBehavior<BannerBearerLogic>();
            if (bannerLogic == null) return;

            var marking = mission.GetMissionBehavior<AgentMarkingSystem>();

            try
            {
                ReleasePendingBearers(bannerLogic);

                if (marking != null && marking.BannerBearerFormations.Count > 0)
                {
                    // 快速路径：使用预先维护好的编队集合。
                    foreach (Formation formation in marking.BannerBearerFormations)
                    {
                        if (formation == null || formation.CountOfUnits <= 0) continue;
                        EnsureOrderGuard(formation);
                        ProtectBearersInFormation(formation, bannerLogic);
                    }
                }
                else
                {
                    // 兜底路径：扫描所有队伍。
                    foreach (Team team in mission.Teams)
                    {
                        if (team == null) continue;
                        if (settings != null && settings.FriendOnlyBannerBearer && !team.IsPlayerAlly) continue;

                        foreach (Formation formation in team.FormationsIncludingSpecialAndEmpty)
                        {
                            if (formation == null || formation.CountOfUnits <= 0) continue;
                            EnsureOrderGuard(formation);
                            ProtectBearersInFormation(formation, bannerLogic);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[SoldierBehaviorTweaks] BannerBearerDefenseGuard tick error: {ex.Message}");
            }
        }

        private static void ProtectBearersInFormation(Formation formation, BannerBearerLogic bannerLogic)
        {
            // F6 委派后编队由人工智能接管，本模组完全让位。
            // 避免脚本干预与原生人工智能接管路径冲突。
            if (MissionBehaviorHelper.IsPlayerSideFormationDelegated(formation)) return;

            List<Agent> bearers = bannerLogic.GetFormationBannerBearers(formation);
            if (bearers == null || bearers.Count == 0) return;

            foreach (Agent bearer in bearers)
            {
                if (bearer == null || !bearer.IsActive() || !bearer.IsAIControlled) continue;
                if (bearer.HasMount) continue;

                if (bearer.Defensiveness != 1f)
                    bearer.Defensiveness = 1f;

                KeepBearerAtRear(bearer, formation);
            }

            // 冲锋时让编队中心略微前推，模拟持盾兵顶在前方。
            ShieldBearerForward(formation);
        }

        private void ReleasePendingBearers(BannerBearerLogic bannerLogic)
        {
            if (_pendingBearerRelease.Count == 0) return;

            var finished = new List<Formation>();

            foreach (Formation formation in _pendingBearerRelease)
            {
                if (formation == null)
                    continue;

                if (formation.CountOfUnits <= 0)
                {
                    finished.Add(formation);
                    continue;
                }

                if (MissionBehaviorHelper.IsPlayerSideFormationDelegated(formation))
                    continue;

                ReleaseBearersInFormation(formation, bannerLogic);
                finished.Add(formation);
            }

            foreach (Formation formation in finished)
                _pendingBearerRelease.Remove(formation);
        }

        private static void ReleaseBearersInFormation(Formation formation, BannerBearerLogic bannerLogic)
        {
            try
            {
                List<Agent> bearers = bannerLogic.GetFormationBannerBearers(formation);
                if (bearers == null) return;

                foreach (Agent bearer in bearers)
                {
                    if (bearer == null || !bearer.IsActive() || !bearer.IsAIControlled) continue;
                    bearer.DisableScriptedMovement();
                    bearer.SetMaximumSpeedLimit(-1f, false);
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[SoldierBehaviorTweaks] Bearer release error: {ex.Message}");
            }
        }

        private static void KeepBearerAtRear(Agent bearer, Formation formation)
        {
            WorldPosition formationPos = formation.CachedMedianPosition;
            if (!formationPos.IsValid) return;

            Vec2 direction = formation.Direction;
            direction.Normalize();

            float formationDepth = formation.Depth;
            float rearOffset = (formationDepth / 2f) + RearDistance;
            Vec2 rearVec = formationPos.AsVec2 - direction * rearOffset;
            if (formation.IsLoose)
            {
                float sideOffset = MBMath.ClampFloat(formation.Interval + formation.UnitDiameter, LooseRearSideOffsetMin, 3f);
                rearVec += direction.RightVec() * sideOffset;
            }

            // 已经靠近目标位置时跳过，避免抖动。
            Vec2 bearerVec = bearer.Position.AsVec2;
            if ((bearerVec - rearVec).Length < 0.5f) return;

            WorldPosition rearPos = formationPos;
            rearPos.SetVec2(rearVec);
            bearer.SetScriptedPosition(ref rearPos, false, Agent.AIScriptedFrameFlags.GoToPosition);
            bearer.SetMaximumSpeedLimit(1f, true);
        }

        private static bool IsFormationCharging(Formation formation)
        {
            try
            {
                MovementOrder order = formation.GetReadonlyMovementOrderReference();
                return order.Equals(MovementOrder.MovementOrderCharge);
            }
            catch { return false; }
        }

        private static Vec2? GetNearestEnemyDirection(Formation formation, float maxDist)
        {
            try
            {
                Mission? mission = formation.Team?.Mission;
                if (mission == null) return null;

                Vec2 formPos = formation.CachedMedianPosition.AsVec2;
                Vec2? result = null;
                float nearest = maxDist;

                foreach (Team team in mission.Teams)
                {
                    if (team == null || team == formation.Team) continue;
                    if (!team.IsEnemyOf(formation.Team)) continue;

                    foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                    {
                        if (enemy == null || enemy.CountOfUnits <= 0) continue;
                        Vec2 d = enemy.CachedMedianPosition.AsVec2 - formPos;
                        float dist = d.Length;
                        if (dist < nearest)
                        {
                            nearest = dist;
                            d.Normalize();
                            result = d;
                        }
                    }
                }
                return result;
            }
            catch { return null; }
        }

        private static void ShieldBearerForward(Formation formation)
        {
            if (!IsFormationCharging(formation)) return;

            Vec2? enemyDir = GetNearestEnemyDirection(formation, ChargeEnemyMaxDist);
            if (enemyDir == null) return;

            WorldPosition formPos = formation.CachedMedianPosition;
            if (!formPos.IsValid) return;

            Vec2 newCenter = formPos.AsVec2 + enemyDir.Value * ChargePushDistance;
            formPos.SetVec2(newCenter);

            formation.SetPositioning(formPos, formation.Direction, 1);
        }

        public override void OnRemoveBehavior()
        {
            _conflictModDetected = null;

            // 退订所有命令守卫事件，避免跨战场任务悬挂引用。
            foreach (var kv in _orderGuards)
            {
                if (kv.Key != null)
                    kv.Key.OnBeforeMovementOrderApplied -= kv.Value;
            }
            _orderGuards.Clear();
            _guardedFormations.Clear();
            _pendingBearerRelease.Clear();

            base.OnRemoveBehavior();
        }
    }
}
