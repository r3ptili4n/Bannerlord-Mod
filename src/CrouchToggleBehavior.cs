using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace SoldierBehaviorTweaks
{
    public class CrouchToggleBehavior : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        private readonly HashSet<int> _crouchFormations = new();

        public bool IsFormationCrouchEnabled(int formationIndex)
        {
            return _crouchFormations.Contains(formationIndex);
        }

        private void SetFormationCrouch(int formationIndex, bool enabled)
        {
            if (enabled)
                _crouchFormations.Add(formationIndex);
            else
                _crouchFormations.Remove(formationIndex);
        }

        private GauntletLayer? _popupLayer;
        private FormationCrouchPopupVM? _popupVM;
        private bool _popupOpen;
        private bool _pauseStateCaptured;
        private bool _wasPausedBeforePopup;
        private bool? _isCombatMission;

        private bool IsCombatMission()
        {
            if (_isCombatMission.HasValue) return _isCombatMission.Value;

            _isCombatMission = false;
            var mission = base.Mission;
            if (mission == null || mission.Teams == null) return false;

            Team? playerTeam = null;
            foreach (Team team in mission.Teams)
            {
                if (team == null) continue;
                if (team.IsPlayerTeam) { playerTeam = team; break; }
            }
            if (playerTeam == null) return false;

            foreach (Team team in mission.Teams)
            {
                if (team == null || team == playerTeam) continue;
                if (team.IsEnemyOf(playerTeam)) { _isCombatMission = true; break; }
            }
            return _isCombatMission.Value;
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (Mission == null || Mission.MissionEnded)
                return;

            HandleInput();
            MaintainCrouchStates(dt);
        }

        private void HandleInput()
        {
            if (_popupOpen) return;
            if (!IsCombatMission()) return;

            bool ctrlHeld = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);

            if (ctrlHeld && Input.IsKeyPressed(InputKey.B))
            {
                OpenPopup();
                return;
            }

            if (ctrlHeld)
            {
                for (int i = 0; i < 8; i++)
                {
                    if (Input.IsKeyPressed(InputKey.D1 + i))
                        ToggleFormationCrouchOrAll(i);
                }
            }
        }

        private void ToggleFormationCrouchOrAll(int formationIndex)
        {
            Agent? player = Agent.Main;
            if (player == null) return;
            Team? team = player.Team;
            if (team == null) return;

            bool hasUnits = false;
            foreach (Formation f in team.FormationsIncludingSpecialAndEmpty)
            {
                if (f != null && f.Index == formationIndex && f.CountOfUnits > 0)
                {
                    hasUnits = true;
                    break;
                }
            }

            if (hasUnits)
                ToggleFormationCrouch(formationIndex);
            else
                ToggleAllFormationsCrouch();
        }

        private void ToggleAllFormationsCrouch()
        {
            Agent? player = Agent.Main;
            if (player == null) return;
            Team? team = player.Team;
            if (team == null) return;

            var valid = new List<int>();
            foreach (Formation f in team.FormationsIncludingSpecialAndEmpty)
            {
                if (f != null && f.CountOfUnits > 0 && f.Index >= 0 && f.Index < 8)
                    valid.Add(f.Index);
            }
            if (valid.Count == 0) return;

            bool anyCrouching = false;
            foreach (int idx in valid)
            {
                if (IsFormationCrouchEnabled(idx))
                {
                    anyCrouching = true;
                    break;
                }
            }

            bool newState = !anyCrouching;
            foreach (int idx in valid)
            {
                SetFormationCrouch(idx, newState);
                ApplyFormationCrouchState(team, idx, newState);
            }

            InformationManager.DisplayMessage(new InformationMessage(
                newState ? "所有编队已蹲下" : "所有编队已站立",
                Color.ConvertStringToColor("#FFD599FF")));
        }

        private void ToggleFormationCrouch(int formationIndex)
        {
            Agent? player = Agent.Main;
            if (player == null) return;
            Team? team = player.Team;
            if (team == null) return;

            bool wasEnabled = IsFormationCrouchEnabled(formationIndex);
            SetFormationCrouch(formationIndex, !wasEnabled);
            ApplyFormationCrouchState(team, formationIndex, !wasEnabled);

            InformationManager.DisplayMessage(new InformationMessage(
                !wasEnabled ? $"编队{formationIndex + 1}已蹲下" : $"编队{formationIndex + 1}已站立",
                Color.ConvertStringToColor("#FFD599FF")));
        }

        private void OpenPopup()
        {
            if (_popupOpen) return;

            try
            {
                Agent? player = Agent.Main;
                if (player == null) return;
                var playerFormation = player.Formation;
                int defaultIndex = playerFormation != null ? playerFormation.Index : 0;

                var screen = ScreenManager.TopScreen;
                if (screen == null) return;

                _popupVM = new FormationCrouchPopupVM(defaultIndex, IsFormationCrouchEnabled);
                _popupVM.OnConfirmed += OnPopupConfirmed;
                _popupVM.OnCancelled += OnPopupCancelled;

                _popupLayer = new GauntletLayer("FormationCrouchPopup", 5000, false);
                _popupLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                _popupLayer.LoadMovie("FormationCrouchPopup", _popupVM);

                screen.AddLayer((ScreenLayer)(object)_popupLayer);
                _popupLayer.IsFocusLayer = true;
                ScreenManager.TrySetFocus((ScreenLayer)(object)_popupLayer);
                PauseMissionForPopup();
                _popupOpen = true;
            }
            catch (Exception ex)
            {
                Debug.Print($"[SoldierBehaviorTweaks] OpenPopup failed: {ex.Message}");
                // 尽力清理弹窗资源。
                SafeCleanupPopup();
            }
        }

        private void ClosePopup()
        {
            try
            {
                if (!_popupOpen || _popupLayer == null) return;

                var screen = ScreenManager.TopScreen;
                if (screen != null)
                {
                    _popupLayer.IsFocusLayer = false;
                    ScreenManager.TryLoseFocus((ScreenLayer)(object)_popupLayer);
                    screen.RemoveLayer((ScreenLayer)(object)_popupLayer);
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[SoldierBehaviorTweaks] ClosePopup failed: {ex.Message}");
            }
            finally
            {
                SafeCleanupPopup();
            }
        }

        private void SafeCleanupPopup()
        {
            try
            {
                if (_popupVM != null)
                {
                    _popupVM.OnConfirmed -= OnPopupConfirmed;
                    _popupVM.OnCancelled -= OnPopupCancelled;
                    try
                    {
                        _popupVM.OnFinalize();
                    }
                    catch (Exception ex)
                    {
                        Debug.Print($"[SoldierBehaviorTweaks] Popup finalize failed: {ex.Message}");
                    }
                }
            }
            finally
            {
                _popupVM = null;
                _popupLayer = null;
                _popupOpen = false;
                RestoreMissionPauseAfterPopup();
            }
        }

        private void PauseMissionForPopup()
        {
            try
            {
                if (Mission == null || Mission.MissionEnded || !Mission.IsLoadingFinished
                    || MissionState.Current == null)
                    return;

                _wasPausedBeforePopup = MissionState.Current.Paused;
                _pauseStateCaptured = true;
                MissionState.Current.Paused = true;
            }
            catch (Exception ex)
            {
                Debug.Print($"[SoldierBehaviorTweaks] PauseMissionForPopup failed: {ex.Message}");
            }
        }

        private void RestoreMissionPauseAfterPopup()
        {
            try
            {
                if (!_pauseStateCaptured || Mission == null || Mission.MissionEnded
                    || MissionState.Current == null)
                    return;

                MissionState.Current.Paused = _wasPausedBeforePopup;
            }
            catch (Exception ex)
            {
                Debug.Print($"[SoldierBehaviorTweaks] RestoreMissionPauseAfterPopup failed: {ex.Message}");
            }
            finally
            {
                _pauseStateCaptured = false;
                _wasPausedBeforePopup = false;
            }
        }

        private void OnPopupConfirmed()
        {
            if (_popupVM == null) { ClosePopup(); return; }

            Agent? player = Agent.Main;
            if (player == null) { ClosePopup(); return; }
            Team? team = player.Team;
            if (team == null) { ClosePopup(); return; }

            int formationIndex = _popupVM.SelectedFormationIndex;

            bool crouch = _popupVM.CrouchMode == 1;
            SetFormationCrouch(formationIndex, crouch);
            ApplyFormationCrouchState(team, formationIndex, crouch);

            InformationManager.DisplayMessage(new InformationMessage(
                crouch ? $"编队{formationIndex + 1}已蹲下" : $"编队{formationIndex + 1}已站立",
                Color.ConvertStringToColor("#FFD599FF")));

            ClosePopup();
        }

        private void OnPopupCancelled() => ClosePopup();

        // 蹲下状态应用。

        private readonly Dictionary<int, bool> _agentCrouchState = new();
        private int _crouchCleanupCounter;
        private bool? _conflictModDetected;
        private bool _crouchBootstrapDone;
        private float _crouchMaintenanceTimer;

        public override void OnAgentCreated(Agent agent) => SyncAgentCrouchState(agent);
        public override void OnAgentBuild(Agent agent, Banner banner) => SyncAgentCrouchState(agent);
        public override void OnAgentMount(Agent agent) => SyncAgentCrouchState(agent);
        public override void OnAgentDismount(Agent agent) => SyncAgentCrouchState(agent);
        public override void OnAgentTeamChanged(Team prevTeam, Team newTeam, Agent agent) => SyncAgentCrouchState(agent);
        protected override void OnAgentControllerChanged(Agent agent, AgentControllerType oldController) => SyncAgentCrouchState(agent);
        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow) => RemoveTrackedCrouchAgent(affectedAgent);
        public override void OnAgentDeleted(Agent affectedAgent) => RemoveTrackedCrouchAgent(affectedAgent);

        private void MaintainCrouchStates(float dt)
        {
            var mission = base.Mission;
            if (mission == null || !mission.IsLoadingFinished) return;
            if (!mission.IsDeploymentFinished) return;
            if (!IsCombatMission()) return;

            var settings = SoldierBehaviorTweaksSettings.Instance;
            if (settings != null && settings.ConflictStrategy == ConflictStrategy.Yield)
            {
                if (!_conflictModDetected.HasValue)
                    _conflictModDetected = MissionBehaviorHelper.IsConflictModActive(mission);
                if (_conflictModDetected.Value)
                {
                    StandDownTrackedAgents(mission);
                    return;
                }
            }

            if (!_crouchBootstrapDone)
            {
                BootstrapCrouchState();
                _crouchBootstrapDone = true;
                _crouchMaintenanceTimer = 0f;
                return;
            }

            _crouchMaintenanceTimer -= dt;
            if (_crouchMaintenanceTimer > 0f) return;
            _crouchMaintenanceTimer = 2f;

            if (_agentCrouchState.Count == 0)
                return;

            if (_crouchCleanupCounter++ % 5 != 0)
                return;

            var marking = mission.GetMissionBehavior<AgentMarkingSystem>();
            if (marking != null)
                CleanTrackedCrouchAgents(marking);
        }

        private void BootstrapCrouchState()
        {
            Team? playerTeam = Agent.Main?.Team;
            if (playerTeam == null)
                return;

            var crouchFormations = new HashSet<int>(_crouchFormations);

            if (crouchFormations.Count == 0)
            {
                _agentCrouchState.Clear();
                return;
            }

            try
            {
                foreach (Formation formation in playerTeam.FormationsIncludingSpecialAndEmpty)
                {
                    if (formation == null || formation.CountOfUnits <= 0) continue;
                    if (!crouchFormations.Contains(formation.Index)) continue;
                    ApplyFormationCrouchState(formation, true);
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[SoldierBehaviorTweaks] BootstrapCrouchState error: {ex.Message}");
            }
        }

        private void ApplyFormationCrouchState(Team team, int formationIndex, bool crouch)
        {
            foreach (Formation formation in team.FormationsIncludingSpecialAndEmpty)
            {
                if (formation == null || formation.Index != formationIndex) continue;
                ApplyFormationCrouchState(formation, crouch);
                return;
            }
        }

        private void ApplyFormationCrouchState(Formation formation, bool crouch)
        {
            if (formation == null || formation.CountOfUnits <= 0)
                return;

            if (MissionBehaviorHelper.IsPlayerSideFormationDelegated(formation))
                return;

            formation.ApplyActionOnEachUnit(agent => SetAgentCrouchState(agent, crouch));
        }

        private void SyncAgentCrouchState(Agent agent)
        {
            if (agent == null)
                return;

            Formation? formation = agent.Formation;
            if (formation == null || formation.Team == null)
            {
                SetAgentCrouchState(agent, false);
                return;
            }

            Team? playerTeam = Agent.Main?.Team;
            if (playerTeam == null || formation.Team != playerTeam)
            {
                SetAgentCrouchState(agent, false);
                return;
            }

            bool shouldCrouch = IsFormationCrouchEnabled(formation.Index)
                && agent.IsAIControlled
                && !agent.HasMount
                && !MissionBehaviorHelper.IsPlayerSideFormationDelegated(formation);

            SetAgentCrouchState(agent, shouldCrouch);
        }

        private void SetAgentCrouchState(Agent agent, bool crouch)
        {
            if (agent == null || !agent.IsActive())
                return;

            if (crouch)
            {
                if (!agent.IsAIControlled) return;
                if (agent.HasMount) return;
                if (agent.Team != Agent.Main?.Team) return;
                if (MissionBehaviorHelper.IsPlayerSideFormationDelegated(agent.Formation)) return;

                if (_agentCrouchState.TryGetValue(agent.Index, out bool active) && active)
                    return;

                agent.SetCrouchMode(true);
                _agentCrouchState[agent.Index] = true;
                return;
            }

            if (_agentCrouchState.TryGetValue(agent.Index, out bool wasCrouching) && wasCrouching)
                agent.SetCrouchMode(false);
            _agentCrouchState.Remove(agent.Index);
        }

        private void RemoveTrackedCrouchAgent(Agent agent)
        {
            if (agent == null)
                return;

            if (_agentCrouchState.TryGetValue(agent.Index, out bool wasCrouching) && wasCrouching
                && agent.IsActive())
                agent.SetCrouchMode(false);

            _agentCrouchState.Remove(agent.Index);
        }

        private void StandDownTrackedAgents(Mission mission)
        {
            try
            {
                var marking = mission.GetMissionBehavior<AgentMarkingSystem>();
                if (marking == null)
                {
                    _agentCrouchState.Clear();
                    return;
                }

                foreach (var kv in _agentCrouchState)
                {
                    if (!kv.Value) continue;
                    if (!marking.ActiveAgentsByIndex.TryGetValue(kv.Key, out Agent? agent) || agent == null)
                        continue;
                    if (agent.IsActive() && agent.IsAIControlled)
                        agent.SetCrouchMode(false);
                }

                _agentCrouchState.Clear();
            }
            catch (Exception ex)
            {
                Debug.Print($"[SoldierBehaviorTweaks] Yield-stand-up error: {ex.Message}");
            }
        }

        private void CleanTrackedCrouchAgents(AgentMarkingSystem marking)
        {
            try
            {
                var deadKeys = new List<int>();
                foreach (var kv in _agentCrouchState)
                {
                    if (!kv.Value || !marking.ActiveAgentsByIndex.TryGetValue(kv.Key, out Agent? agent)
                        || agent == null || !agent.IsActive())
                    {
                        deadKeys.Add(kv.Key);
                    }
                }

                foreach (int key in deadKeys)
                    _agentCrouchState.Remove(key);
            }
            catch (Exception ex)
            {
                Debug.Print($"[SoldierBehaviorTweaks] CleanTrackedCrouchAgents error: {ex.Message}");
            }
        }

        public override void OnRemoveBehavior()
        {
            var mission = base.Mission;
            if (mission != null)
                StandDownTrackedAgents(mission);
            _agentCrouchState.Clear();
            _crouchBootstrapDone = false;
            ClosePopup();
            _crouchFormations.Clear();
            base.OnRemoveBehavior();
        }

        protected override void OnEndMission()
        {
            var mission = base.Mission;
            if (mission != null)
                StandDownTrackedAgents(mission);
            _agentCrouchState.Clear();
            _crouchBootstrapDone = false;
            ClosePopup();
            _crouchFormations.Clear();
            base.OnEndMission();
        }
    }
}
