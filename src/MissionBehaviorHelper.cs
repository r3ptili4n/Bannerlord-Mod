using System;
using TaleWorlds.MountAndBlade;

namespace SoldierBehaviorTweaks
{
    /// <summary>共享工具方法，用于减少各行为之间的重复逻辑。</summary>
    internal static class MissionBehaviorHelper
    {
        private static int _lastConflictCheckFrame;
        private static bool _lastConflictResult;

        /// <summary>
        /// 检测是否存在处于激活状态的冲突模组。
        /// 检查结果会在当前帧进行缓存，因为同一次逻辑更新中可能会有多个行为调用此方法。
        /// </summary>
        public static bool IsConflictModActive(Mission mission)
        {
            if (!_conflictCheckedThisFrame)
            {
                _lastConflictResult = ScanForConflictMods(mission);
                _lastConflictCheckFrame = Environment.TickCount;
            }
            return _lastConflictResult;
        }

        /// <summary>
        /// 仅当编队仍处于稳定、由玩家控制的状态时返回 true。
        /// 有意避开已经委派给 AI 的编队，
        /// 以及当前没有队长的编队，因为 F6 队长分配路径比较脆弱。
        /// </summary>
        public static bool IsStablePlayerControlledFormation(Formation formation)
        {
            return formation != null
                && formation.CountOfUnits > 0
                && formation.Captain != null
                && !formation.IsAIControlled;
        }

        /// <summary>
        /// 当玩家侧编队已经委派给 AI，
        /// 或者不再拥有队长时返回 true。敌方编队不会在这里被视作委派编队。
        /// </summary>
        public static bool IsPlayerSideFormationDelegated(Formation formation)
        {
            if (formation == null || formation.CountOfUnits <= 0)
                return false;

            Team team = formation.Team;
            if (team == null)
                return false;

            if (!team.IsPlayerTeam && !team.IsPlayerAlly)
                return false;

            return formation.IsAIControlled || formation.Captain == null;
        }

        private static bool _conflictCheckedThisFrame
        {
            get
            {
                // 计时器大约每 49 天回绕一次；用于同帧缓存已经足够。
                int now = Environment.TickCount;
                if (now == _lastConflictCheckFrame) return true;
                _lastConflictCheckFrame = now;
                return false;
            }
        }

        private static bool ScanForConflictMods(Mission mission)
        {
            try
            {
                foreach (MissionBehavior behavior in mission.MissionBehaviors)
                {
                    string name = behavior.GetType().FullName ?? "";
                    if (name.StartsWith("RBM.") || name.StartsWith("RBMAI."))
                        return true;
                }
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print($"[SoldierBehaviorTweaks] IsConflictModActive error: {ex.Message}");
            }
            return false;
        }
    }
}
