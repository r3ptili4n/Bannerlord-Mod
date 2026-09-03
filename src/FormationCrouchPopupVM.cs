using System;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace SoldierBehaviorTweaks
{
    public class FormationCrouchPopupVM : ViewModel
    {
        public event Action? OnConfirmed;
        public event Action? OnCancelled;

        private int _selectedFormationIndex;
        private int _crouchMode;
        private string _title = string.Empty;
        private readonly Func<int, bool> _isFormationCrouchEnabled;

        public FormationCrouchPopupVM(int defaultFormationIndex, Func<int, bool> isFormationCrouchEnabled)
        {
            _isFormationCrouchEnabled = isFormationCrouchEnabled ?? throw new ArgumentNullException(nameof(isFormationCrouchEnabled));
            _selectedFormationIndex = defaultFormationIndex < 0 ? 0 : defaultFormationIndex > 7 ? 7 : defaultFormationIndex;
            UpdateTitle();
            UpdateFormationState();
        }

        [DataSourceProperty]
        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged(nameof(Title));
                }
            }
        }

        [DataSourceProperty]
        public int SelectedFormationIndex
        {
            get => _selectedFormationIndex;
            set
            {
                if (_selectedFormationIndex != value)
                {
                    _selectedFormationIndex = value;
                    OnPropertyChanged(nameof(SelectedFormationIndex));
                    OnPropertyChanged(nameof(FormationLabelText));
                    UpdateTitle();
                    UpdateFormationState();
                }
            }
        }

        [DataSourceProperty]
        public string FormationLabelText => $"编队 {_selectedFormationIndex + 1}";

        [DataSourceProperty]
        public int CrouchMode
        {
            get => _crouchMode;
            set
            {
                if (_crouchMode != value)
                {
                    _crouchMode = value;
                    OnPropertyChanged(nameof(CrouchMode));
                    OnPropertyChanged(nameof(IsCrouchDown));
                    OnPropertyChanged(nameof(IsCrouchUp));
                    OnPropertyChanged(nameof(CurrentStateText));
                }
            }
        }

        [DataSourceProperty] public bool IsCrouchDown => _crouchMode == 1;
        [DataSourceProperty] public bool IsCrouchUp => _crouchMode == 0;
        [DataSourceProperty] public string CurrentStateText => _crouchMode == 1 ? "当前状态：下蹲" : "当前状态：起身";

        [DataSourceProperty] public string ConfirmText => "确认";
        [DataSourceProperty] public string CancelText => "取消";

        private void UpdateTitle() => Title = $"编队 {_selectedFormationIndex + 1} 下蹲设置";

        private void UpdateFormationState()
        {
            CrouchMode = _isFormationCrouchEnabled(_selectedFormationIndex) ? 1 : 0;
        }

        public void ExecuteScrollLeft()
        {
            if (_selectedFormationIndex > 0) SelectedFormationIndex--;
        }

        public void ExecuteScrollRight()
        {
            if (_selectedFormationIndex < 7) SelectedFormationIndex++;
        }

        public void ExecuteSetCrouchDown() => CrouchMode = 1;
        public void ExecuteSetCrouchUp() => CrouchMode = 0;

        public void ExecuteConfirm() => OnConfirmed?.Invoke();
        public void ExecuteCancel() => OnCancelled?.Invoke();
    }
}
