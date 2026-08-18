using System;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

namespace SoldierBehaviorTweaks
{
    public enum ConflictStrategy
    {
        Default = 0,
        Override = 1,
        Yield = 2
    }

    internal sealed class SoldierBehaviorTweaksSettings : AttributeGlobalSettings<SoldierBehaviorTweaksSettings>
    {
        private bool _enableBannerBearerDefense = true;
        private bool _enableBannerPickup = true;
        private bool _enableThrowNoMelee = true;
        private bool _preferTwoHanded = true;
        private bool _discourageThrustPolearm = true;
        private bool _friendOnlyBannerBearer;
        private bool _friendOnlyBannerPickup = true;
        private bool _friendOnlyThrowNoMelee;
        private bool _friendOnlyPreferTwoHanded;
        private bool _friendOnlyDiscourageThrustPolearm;
        private int _conflictStrategyInt;

        public override string Id => "SoldierBehaviorTweaks";
        public override string DisplayName => "部队行为微调";
        public override string FolderName => "SoldierBehaviorTweaks";
        public override string FormatType => "json";

        [SettingPropertyGroup("部队行为微调")]
        [SettingPropertyInteger("Mod 冲突策略", 0, 2, "0", Order = 0, RequireRestart = false,
            HintText = "0 = 默认（两者均运行）  1 = 覆盖（本 Mod 优先）  2 = 让步（其他 Mod 优先）")]
        public int ConflictStrategySlider
        {
            get => _conflictStrategyInt;
            set
            {
                if (_conflictStrategyInt != value)
                {
                    _conflictStrategyInt = value;
                    OnPropertyChanged("ConflictStrategySlider");
                }
            }
        }

        [SettingPropertyGroup("部队行为微调")]
        [SettingPropertyBool("旗手保护", Order = 1, RequireRestart = false,
            HintText = "旗手会待在编队后排，保持防御姿态。（仅步兵与弓箭手；骑兵除外）")]
        public bool EnableBannerBearerDefense
        {
            get => _enableBannerBearerDefense;
            set
            {
                if (_enableBannerBearerDefense != value)
                {
                    _enableBannerBearerDefense = value;
                    OnPropertyChanged("EnableBannerBearerDefense");
                }
            }
        }

        [SettingPropertyGroup("部队行为微调")]
        [SettingPropertyBool("旗帜拾取", Order = 2, RequireRestart = false,
            HintText = "旗帜死亡且掉落时，附近同编队士兵会前往捡取旗帜。")]
        public bool EnableBannerPickup
        {
            get => _enableBannerPickup;
            set
            {
                if (_enableBannerPickup != value)
                {
                    _enableBannerPickup = value;
                    OnPropertyChanged("EnableBannerPickup");
                }
            }
        }

        [SettingPropertyGroup("部队行为微调")]
        [SettingPropertyBool("投掷武器 — 禁用近战", Order = 3, RequireRestart = false,
            HintText = "士兵无法将投掷武器（斧/标枪/飞刀）切换到近战模式。（玩家不受影响）")]
        public bool EnableThrowNoMelee
        {
            get => _enableThrowNoMelee;
            set
            {
                if (_enableThrowNoMelee != value)
                {
                    _enableThrowNoMelee = value;
                    OnPropertyChanged("EnableThrowNoMelee");
                }
            }
        }

        [SettingPropertyGroup("部队行为微调")]
        [SettingPropertyBool("双手武器优先", Order = 4, RequireRestart = false,
            HintText = "AI 会优先使用双手武器（若可用）。（骑兵除外）")]
        public bool PreferTwoHanded
        {
            get => _preferTwoHanded;
            set
            {
                if (_preferTwoHanded != value)
                {
                    _preferTwoHanded = value;
                    OnPropertyChanged("PreferTwoHanded");
                }
            }
        }

        [SettingPropertyGroup("部队行为微调")]
        [SettingPropertyBool("降低纯戳刺长杆权重", Order = 5, RequireRestart = false,
            HintText = "步兵会尽量避免纯戳刺长杆，优先切换为可挥砍的武器。（仅纯戳刺长杆；骑兵除外）")]
        public bool DiscourageThrustPolearm
        {
            get => _discourageThrustPolearm;
            set
            {
                if (_discourageThrustPolearm != value)
                {
                    _discourageThrustPolearm = value;
                    OnPropertyChanged("DiscourageThrustPolearm");
                }
            }
        }

        [SettingPropertyGroup("部队行为微调")]
        [SettingPropertyBool("→ 仅友军：旗手保护", Order = 6, RequireRestart = false,
            HintText = "旗手保护仅对玩家及友方部队生效。")]
        public bool FriendOnlyBannerBearer
        {
            get => _friendOnlyBannerBearer;
            set
            {
                if (_friendOnlyBannerBearer != value)
                {
                    _friendOnlyBannerBearer = value;
                    OnPropertyChanged("FriendOnlyBannerBearer");
                }
            }
        }

        [SettingPropertyGroup("部队行为微调")]
        [SettingPropertyBool("→ 仅友军：旗帜拾取", Order = 7, RequireRestart = false,
            HintText = "旗帜拾取仅对玩家及友方部队生效。")]
        public bool FriendOnlyBannerPickup
        {
            get => _friendOnlyBannerPickup;
            set
            {
                if (_friendOnlyBannerPickup != value)
                {
                    _friendOnlyBannerPickup = value;
                    OnPropertyChanged("FriendOnlyBannerPickup");
                }
            }
        }

        [SettingPropertyGroup("部队行为微调")]
        [SettingPropertyBool("→ 仅友军：投掷禁用近战", Order = 8, RequireRestart = false,
            HintText = "投掷武器近战限制仅对玩家及友方部队生效。")]
        public bool FriendOnlyThrowNoMelee
        {
            get => _friendOnlyThrowNoMelee;
            set
            {
                if (_friendOnlyThrowNoMelee != value)
                {
                    _friendOnlyThrowNoMelee = value;
                    OnPropertyChanged("FriendOnlyThrowNoMelee");
                }
            }
        }

        [SettingPropertyGroup("部队行为微调")]
        [SettingPropertyBool("→ 仅友军：双手优先", Order = 9, RequireRestart = false,
            HintText = "双手武器优先仅对玩家及友方部队生效。")]
        public bool FriendOnlyPreferTwoHanded
        {
            get => _friendOnlyPreferTwoHanded;
            set
            {
                if (_friendOnlyPreferTwoHanded != value)
                {
                    _friendOnlyPreferTwoHanded = value;
                    OnPropertyChanged("FriendOnlyPreferTwoHanded");
                }
            }
        }

        [SettingPropertyGroup("部队行为微调")]
        [SettingPropertyBool("→ 仅友军：长杆降权", Order = 10, RequireRestart = false,
            HintText = "戳刺长杆降权仅对玩家及友方部队生效。")]
        public bool FriendOnlyDiscourageThrustPolearm
        {
            get => _friendOnlyDiscourageThrustPolearm;
            set
            {
                if (_friendOnlyDiscourageThrustPolearm != value)
                {
                    _friendOnlyDiscourageThrustPolearm = value;
                    OnPropertyChanged("FriendOnlyDiscourageThrustPolearm");
                }
            }
        }

        public ConflictStrategy ConflictStrategy => (ConflictStrategy)_conflictStrategyInt;
    }
}
