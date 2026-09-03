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

        // F6 濮旀淳 / AI 鎺ョ闃叉姢銆?
        // F6 濮旀淳瑙﹀彂 SetMovementOrder 鏃讹紝OnBeforeMovementOrderApplied 浜嬩欢浼氭渶鍏堣Е鍙戙€?
        // 姝ゆ椂娓呴櫎鏃楁墜娈嬬暀鐨勮剼鏈Щ鍔ㄧ姸鎬侊紝閬垮厤鍘熺敓浜哄伐鏅鸿兘鎺ョ璺緞澶勭悊鏃ц剼鏈Щ鍔ㄦ椂宕╂簝銆?
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
            if (mission == null || mission.MissionEnded || !mission.IsLoadingFinished) return;

            _elapsed += dt;
            if (_elapsed < CheckInterval) return;
            _elapsed = 0f;

            var settings = SoldierBehaviorTweaksSettings.Instance;
            if (settings != null && !settings.EnableBannerBearerDefense) return;

            // 鍐茬獊绛栫暐淇濇姢銆?
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
                    // 蹇€熻矾寰勶細浣跨敤棰勫厛缁存姢濂界殑缂栭槦闆嗗悎銆?
                    foreach (Formation formation in marking.BannerBearerFormations)
                    {
                        if (formation == null || formation.CountOfUnits <= 0) continue;
                        EnsureOrderGuard(formation);
                        ProtectBearersInFormation(formation, bannerLogic);
                    }
                }
                else
                {
                    // 鍏滃簳璺緞锛氭壂鎻忔墍鏈夐槦浼嶃€?
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
            // F6 濮旀淳鍚庣紪闃熺敱浜哄伐鏅鸿兘鎺ョ锛屾湰妯＄粍瀹屽叏璁╀綅銆?
            // 閬垮厤鑴氭湰骞查涓庡師鐢熶汉宸ユ櫤鑳芥帴绠¤矾寰勫啿绐併€?
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

            // 鍐查攱鏃惰缂栭槦涓績鐣ュ井鍓嶆帹锛屾ā鎷熸寔鐩惧叺椤跺湪鍓嶆柟銆?
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
                    if (bearer == null || !bearer.IsActive() || !bearer.IsAIControlled || bearer.HasMount) continue;
                    bearer.DisableScriptedMovement();
                    if (bearer.IsActive())
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
            if (formation == null || formation.Team == null) return;
            Mission? mission = formation.Team.Mission;
            if (mission == null || mission.MissionEnded || formation.CountOfUnits <= 0
                || bearer == null || !bearer.IsActive() || !bearer.IsAIControlled
                || bearer.HasMount || bearer.Formation != formation)
                return;

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

            // 宸茬粡闈犺繎鐩爣浣嶇疆鏃惰烦杩囷紝閬垮厤鎶栧姩銆?
            Vec2 bearerVec = bearer.Position.AsVec2;
            if ((bearerVec - rearVec).Length < 0.5f) return;

            WorldPosition rearPos = formationPos;
            rearPos.SetVec2(rearVec);
            if (mission.MissionEnded || !bearer.IsActive() || bearer.Formation != formation) return;
            bearer.SetScriptedPosition(ref rearPos, false, Agent.AIScriptedFrameFlags.GoToPosition);
            if (mission.MissionEnded || !bearer.IsActive() || bearer.Formation != formation) return;
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
            if (formation == null || formation.Team == null) return;
            Mission? mission = formation.Team.Mission;
            if (mission == null || mission.MissionEnded || formation.CountOfUnits <= 0)
                return;

            if (!IsFormationCharging(formation)) return;

            Vec2? enemyDir = GetNearestEnemyDirection(formation, ChargeEnemyMaxDist);
            if (enemyDir == null) return;

            WorldPosition formPos = formation.CachedMedianPosition;
            if (!formPos.IsValid) return;

            Vec2 newCenter = formPos.AsVec2 + enemyDir.Value * ChargePushDistance;
            formPos.SetVec2(newCenter);

            if (mission.MissionEnded || formation.CountOfUnits <= 0) return;
            formation.SetPositioning(formPos, formation.Direction, 1);
        }

        public override void OnRemoveBehavior()
        {
            _conflictModDetected = null;

            // 閫€璁㈡墍鏈夊懡浠ゅ畧鍗簨浠讹紝閬垮厤璺ㄦ垬鍦轰换鍔℃偓鎸傚紩鐢ㄣ€?
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
