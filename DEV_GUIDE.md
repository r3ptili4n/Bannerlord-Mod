# 部队行为调整（SoldierBehaviorTweaks）— 团队协作开发指南

> 面向有 C# 基础但不熟悉 Bannerlord Mod 开发的协作者。读完本文档即可上手修改和新增功能。

---

## 1. 项目概述

### 1.1 Mod 功能清单

| 功能 | 快捷键 | 说明 |
|------|--------|------|
| 旗手保护 | MCM 开关 | 旗手留在编队后排，保持防御姿态；冲锋时盾牌兵前推 |
| 投掷武器禁用近战 | MCM 开关 | AI 士兵无法将投掷武器（斧/标枪/飞刀）切到近战模式 |
| 双手武器优先 | MCM 开关 | 附近有敌人或骑兵时，AI 优先使用双手武器 |
| 纯戳刺长杆降权 | MCM 开关 | 步兵尽量避免纯戳刺长杆，优先切换为可挥砍武器 |
| 士兵蹲下控制 | Ctrl+B / Ctrl+1~8 | 弹窗或快捷键控制编队蹲下/站立 |

### 1.2 技术栈

| 项目 | 版本/说明 |
|------|-----------|
| 语言 | C# 10.0 |
| 框架 | .NET Framework 4.7.2（net472） |
| 平台 | x64 |
| 构建工具 | dotnet SDK（`dotnet build`） |
| UI 框架 | GauntletUI（TaleWorlds 自研） |
| Harmony | 2.2.2（本项目未直接使用，但依赖库可能用到） |
| MCM | v5.11.4（Mod Configuration Menu） |
| 游戏版本 | Bannerlord 1.4.8 |

### 1.3 依赖关系

```
Native → SandBoxCore → Sandbox → StoryMode
                              ↑
Bannerlord.MBOptionScreen (MCM v5) ← SoldierBehaviorTweaks
```

SubModule.xml 中声明的依赖：
```xml
<DependedModules>
  <DependedModule Id="Native" />
  <DependedModule Id="SandBoxCore" />
  <DependedModule Id="Sandbox" />
  <DependedModule Id="Bannerlord.MBOptionScreen" />
</DependedModules>

<DependedModuleMetadatas>
  <DependedModuleMetadata id="Native" order="LoadBeforeThis" />
  <DependedModuleMetadata id="SandBoxCore" order="LoadBeforeThis" />
  <DependedModuleMetadata id="Sandbox" order="LoadBeforeThis" />
  <DependedModuleMetadata id="Bannerlord.MBOptionScreen" order="LoadBeforeThis" />
</DependedModuleMetadatas>
```

> `DependedModules` 声明依赖存在，`DependedModuleMetadatas` 控制加载顺序（`LoadBeforeThis` = 依赖模块先加载）。两者必须同时声明。

---

## 2. 环境搭建

### 2.1 安装 .NET SDK

1. 前往 https://dotnet.microsoft.com/download 下载 **.NET SDK 6.0+**（用于构建 net472 项目）
2. 安装后验证：
   ```powershell
   dotnet --version
   ```

### 2.2 项目结构

```
SoldierBehaviorTweaks/
├── SubModule.xml                    # Mod 描述符（游戏加载入口）
├── src/
│   ├── AIBannerFix.csproj           # 项目文件（编译配置）
│   ├── SubModule.cs                 # 模块入口（注册 MissionBehavior）
│   ├── AIBannerFixSettings.cs       # MCM 设置类（8 个选项）
│   ├── MissionBehaviorHelper.cs     # 共享工具（冲突检测帧缓存）
│   ├── AgentMarkingSystem.cs        # 士兵预标记系统（性能核心）
│   ├── BannerBearerDefenseGuard.cs  # 旗手保护行为 + F6 委派防护
│   ├── AutoMeleeWeaponSwitch.cs     # 武器自动切换行为（493 行）
│   ├── CrouchToggleBehavior.cs      # 蹲下控制行为（461 行）
│   └── FormationCrouchPopupVM.cs    # 蹲下弹窗 ViewModel
├── GUI/Prefabs/
│   └── FormationCrouchPopup.xml     # 蹲下弹窗 UI 布局
└── bin/Win64_Shipping_Client/
    └── SoldierBehaviorTweaks.dll    # 编译产物
```

### 2.3 编译命令

```powershell
cd "E:\steam\steamapps\common\Mount & Blade II Bannerlord\Modules\SoldierBehaviorTweaks\src"
dotnet build
```

编译产物输出到 `..\bin\Win64_Shipping_Client\SoldierBehaviorTweaks.dll`。

### 2.4 引用库路径配置

在 `.csproj` 中通过 `GameFolder` 变量引用游戏 DLL：

```xml
<PropertyGroup>
  <GameFolder>E:\steam\steamapps\common\Mount &amp; Blade II Bannerlord</GameFolder>
</PropertyGroup>

<ItemGroup>
  <!-- 通配引用所有 TaleWorlds DLL（排除 Native） -->
  <Reference Include="$(GameFolder)\bin\Win64_Shipping_Client\TaleWorlds.*.dll"
             Exclude="$(GameFolder)\bin\Win64_Shipping_Client\TaleWorlds.Native.dll">
    <HintPath>%(Identity)</HintPath>
    <Private>False</Private>
  </Reference>
  <!-- MCM v5 -->
  <Reference Include="$(GameFolder)\Modules\Bannerlord.MBOptionScreen\bin\Win64_Shipping_Client\MCMv5.dll">
    <HintPath>$(GameFolder)\Modules\Bannerlord.MBOptionScreen\bin\Win64_Shipping_Client\MCMv5.dll</HintPath>
    <Private>False</Private>
  </Reference>
</ItemGroup>
```

> **注意**：`GameFolder` 路径中的 `&` 必须写成 `&amp;`（XML 转义）。

---

## 3. 核心模块说明

### 3.1 模块入口 — SubModule.cs

```
src/SubModule.cs
```

职责：游戏加载时打印日志，进入战斗场景时注册 4 个 MissionBehavior。

```csharp
public override void OnMissionBehaviorInitialize(Mission mission)
{
    base.OnMissionBehaviorInitialize(mission);
    mission.AddMissionBehavior(new AgentMarkingSystem());
    mission.AddMissionBehavior(new BannerBearerDefenseGuard());
    mission.AddMissionBehavior(new AutoMeleeWeaponSwitch());
    mission.AddMissionBehavior(new CrouchToggleBehavior());
}
```

**关键规则**：`OnMissionBehaviorInitialize` 必须用 `public override`，不能用 `protected override`。

### 3.2 MCM 设置 — AIBannerFixSettings.cs

```
src/AIBannerFixSettings.cs
```

继承 `AttributeGlobalSettings<SoldierBehaviorTweaksSettings>`，通过特性声明 MCM 界面：

```csharp
[SettingPropertyGroup("部队行为调整")]
[SettingPropertyBool("旗手保护", Order = 1, RequireRestart = false,
    HintText = "旗手会待在编队后排...")]
public bool EnableBannerBearerDefense { get; set; }
```

- `Order` 控制排序（1.4.8 兼容方案：所有设置放同一组，用 Order 排序）
- `HintText` 是鼠标悬停提示
- `RequireRestart = false` 表示修改后无需重启游戏

### 3.3 共享工具 — MissionBehaviorHelper.cs

```
src/MissionBehaviorHelper.cs
```

提供帧级缓存的冲突 Mod 检测，避免多个 Behavior 每帧重复扫描：

```csharp
public static bool IsConflictModActive(Mission mission)
{
    if (!_conflictCheckedThisFrame)  // 用 Environment.TickCount 判断同帧
    {
        _lastConflictResult = ScanForConflictMods(mission);
        _lastConflictCheckFrame = Environment.TickCount;
    }
    return _lastConflictResult;
}
```

### 3.4 士兵预标记 — AgentMarkingSystem.cs

```
src/AgentMarkingSystem.cs
```

**性能核心**。战斗开始 2 秒后扫描所有士兵，预标记需要干预的索引，后续 Behavior 只遍历标记集合而非全部 Agent。

```csharp
public HashSet<int> WeaponRelevantAgentIndices { get; } = new();  // 武器相关士兵
public HashSet<int> CavalryAgentIndices { get; } = new();          // 骑兵
public HashSet<Formation> BannerBearerFormations { get; } = new(); // 有旗手的编队
```

- 初始延迟：2 秒（等部署完成）
- 增量刷新：每 12 秒
- 死 Agent 清理：每 2 次增量扫描清理一次

### 3.5 旗手保护 — BannerBearerDefenseGuard.cs

```
src/BannerBearerDefenseGuard.cs
```

职责：
1. **保持旗手在编队后排**：通过 `SetScriptedPosition` 将旗手定位到编队深度后方
2. **防御姿态**：设置 `Defensiveness = 1f`
3. **冲锋前推**：编队冲锋时，将编队中心向最近敌人方向偏移 3 米

**核心算法**（保持旗手在后排）：
```csharp
private static void KeepBearerAtRear(Agent bearer, Formation formation)
{
    WorldPosition formationPos = formation.CachedMedianPosition;
    Vec2 direction = formation.Direction;
    direction.Normalize();

    float rearOffset = (formation.Depth / 2f) + RearDistance;  // RearDistance = 1f
    Vec2 rearVec = formationPos.AsVec2 - direction * rearOffset;

    // 距离目标 < 0.5m 则跳过，避免抖动
    if ((bearer.Position.AsVec2 - rearVec).Length < 0.5f) return;

    WorldPosition rearPos = formationPos;
    rearPos.SetVec2(rearVec);
    bearer.SetScriptedPosition(ref rearPos, false, Agent.AIScriptedFrameFlags.GoToPosition);
    bearer.SetMaximumSpeedLimit(1f, true);
}
```

**F6 委派防护**（关键防崩溃机制）：

监听 `OnBeforeMovementOrderApplied` 事件，在玩家按 F6 委派 AI 时清除旗手的 scripted 状态，避免与原生 AI 接管路径冲突导致崩溃。

```csharp
// 每个编队注册一个 order guard
private void EnsureOrderGuard(Formation formation)
{
    if (formation == null || !_guardedFormations.Add(formation)) return;

    Action<Formation, MovementOrder.MovementOrderEnum> handler = (f, orderEnum) =>
    {
        var bannerLogic = Mission.GetMissionBehavior<BannerBearerLogic>();
        if (bannerLogic == null) return;
        List<Agent> bearers = bannerLogic.GetFormationBannerBearers(f);
        if (bearers == null) return;
        foreach (Agent bearer in bearers)
        {
            if (bearer == null || !bearer.IsActive() || !bearer.IsAIControlled) continue;
            bearer.DisableScriptedMovement();  // 清除 GoToPosition 状态
        }
    };

    _orderGuards[formation] = handler;
    formation.OnBeforeMovementOrderApplied += handler;
}
```

**OnRemoveBehavior 中必须退订**：
```csharp
public override void OnRemoveBehavior()
{
    foreach (var kv in _orderGuards)
    {
        if (kv.Key != null)
            kv.Key.OnBeforeMovementOrderApplied -= kv.Value;
    }
    _orderGuards.Clear();
    _guardedFormations.Clear();
    base.OnRemoveBehavior();
}
```

> **F6 委派后**：`formation.IsAIControlled` 变为 true，此时 `ProtectBearersInFormation` 直接 return，Mod 完全让位给原生 AI。

### 3.6 武器自动切换 — AutoMeleeWeaponSwitch.cs

```
src/AutoMeleeWeaponSwitch.cs
```

职责：
1. **投掷武器禁用近战**：调用 `SetUsageIndexOfWeaponInSlotAsClient` 将投掷武器的近战用法索引设为远程用法索引
2. **双手武器优先**：附近有敌人或骑兵时，从单手武器切换到双手武器
3. **纯戳刺长杆降权**：检测到纯戳刺长杆（无挥砍伤害）时切换到更好的武器

**性能优化**：
- 用 `static HashSet<WeaponClass>` 预定义武器分类，避免每 tick 分配
- 用 `HasAnyUsageClass()` 静态方法替代 LINQ `.Any()`
- 骑兵位置每 tick 缓存一次，避免重复遍历

### 3.7 蹲下控制 — CrouchToggleBehavior.cs

```
src/CrouchToggleBehavior.cs
```

职责：
1. **快捷键**：Ctrl+B 打开弹窗，Ctrl+1~8 快速切换编队
2. **弹窗 UI**：GauntletLayer 加载 FormationCrouchPopup.xml
3. **蹲下应用**：每 1 秒对标记编队的 AI 士兵调用 `SetCrouchMode(true)`

**状态隔离**：用 `Agent.Main.Index` 作为 Dictionary 的 key，避免跨 Mission 的 Team 引用泄漏。

```csharp
// 静态 Dictionary，key 是玩家 Agent Index（非 Team 引用！）
private static readonly Dictionary<int, HashSet<int>> _perPlayerCrouchFormations = new();

private static HashSet<int> GetOrCreateCrouchSet()
{
    int key = Agent.Main?.Index ?? -1;
    if (key < 0) return new HashSet<int>();
    lock (_lockObj)
    {
        if (!_perPlayerCrouchFormations.TryGetValue(key, out var set))
        {
            set = new HashSet<int>();
            _perPlayerCrouchFormations[key] = set;
        }
        return set;
    }
}
```

**快捷键处理**（在 `OnMissionTick` 中调用）：
```csharp
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
            if (Input.IsKeyPressed(InputKey.D1 + i))  // D1~D8 对应数字键 1~8
                ToggleFormationCrouchOrAll(i);
        }
    }
}
```

> **注意**：`InputKey.D1 + i` 利用枚举值连续的特性遍历数字键。如果按下的编队号没有对应编队，则切换全部编队蹲下/站立。

### 3.8 蹲下弹窗 ViewModel — FormationCrouchPopupVM.cs

```
src/FormationCrouchPopupVM.cs
```

职责：为蹲下弹窗 UI 提供数据绑定，支持编队选择（左右翻页）、蹲下/站立单选、确认/取消。

```csharp
public class FormationCrouchPopupVM : ViewModel
{
    // 事件回调，由 CrouchToggleBehavior 订阅
    public event Action? OnConfirmed;
    public event Action? OnCancelled;

    [DataSourceProperty]
    public string Title { get; set; }                    // "编队 X — 蹲下控制"
    [DataSourceProperty]
    public string FormationLabelText => $"编队 {_selectedFormationIndex + 1}";
    [DataSourceProperty]
    public int CrouchMode { get; set; }                  // 0=站立, 1=下蹲
    [DataSourceProperty]
    public bool IsCrouchDown => _crouchMode == 1;        // 单选按钮可见性
    [DataSourceProperty]
    public bool IsCrouchUp => _crouchMode == 0;

    // 按钮方法（Execute 前缀 → XML Command.Click 绑定）
    public void ExecuteScrollLeft()   // ◀ 上一个编队
    public void ExecuteScrollRight()  // ▶ 下一个编队
    public void ExecuteSetCrouchDown() => CrouchMode = 1;
    public void ExecuteSetCrouchUp() => CrouchMode = 0;
    public void ExecuteConfirm() => OnConfirmed?.Invoke();
    public void ExecuteCancel() => OnCancelled?.Invoke();
}
```

**设计要点**：
- 编队索引 0~7，对应 Ctrl+1~8
- `CrouchMode` 变更时触发 `IsCrouchDown` 和 `IsCrouchUp` 的 PropertyChanged
- 构造时自动读取当前编队的蹲下状态：`CrouchToggleBehavior.IsFormationCrouchEnabled(team, index)`

---

## 4. 开发踩坑记录

### 4.1 API 命名空间

| 现象 | 原因 | 解决方案 |
|------|------|----------|
| `CS0246: 未能找到类型 WorldPosition` | 缺少 `using TaleWorlds.Engine;` | 文件头部添加 `using TaleWorlds.Engine;` |
| `CS0246: 未能找到类型 GauntletScreen` | `GauntletScreen` 已废弃，应继承 `ScreenBase` | 改用 `ScreenBase` + 手动创建 `GauntletLayer` |
| `CS0234: 命名空间 TaleWorlds.MountAndBlade 中不存在 View` | `TaleWorlds.MountAndBlade.View` 不在通配引用范围内（被排除或不在游戏 bin 目录） | 检查 DLL 是否存在，或移除该 using |

### 4.2 编译错误

| 现象 | 原因 | 解决方案 |
|------|------|----------|
| `CS0507: 无法更改访问修饰符` | `OnApplicationTick` 在基类中是 `protected internal`，子类重写时用了 `public` | 改为 `protected override` |
| `CS0115: 没有找到适合的方法来重写` | 方法名拼写错误（如 `OnMissionBehaviourInitialize` 用了英式拼写） | 改为 `OnMissionBehaviorInitialize`（美式拼写） |
| `CS0119: "Input"是一个类型` | `Input` 是静态类，不能声明变量 `var input = Input` | 直接用 `Input.IsKeyDown(...)` 静态调用 |
| `CS0234: System.Windows.Forms 不存在` | .NET 4.7.2 项目未引用 `System.Windows.Forms` | 在 csproj 中添加 `<Reference Include="System.Windows.Forms" />` |
| `CS1729: MBBindingList 不包含 1 个参数的构造函数` | `MBBindingList<T>` 无参构造，需逐个 Add | 改为 `new MBBindingList<T>()` 然后循环 Add |

### 4.3 Harmony 补丁守卫

| 现象 | 原因 | 解决方案 |
|------|------|----------|
| 存档界面人物模型扭曲/崩溃 | Harmony Patch 在非 Mission 环境（如存档载入界面）下执行，调用了依赖 Mission 上下文的方法 | 在 Patch 开头加 `if (Mission.Current == null) return;` |
| 三层守卫仍不够 | 仅 `Mission.Current == null` 不够，某些场景 Mission 存在但未就绪 | 采用三层守卫：`IsLoadingFinished` → `IsDeploymentFinished` → 存在敌对阵营 |

**推荐模式**（来自 AutoMeleeWeaponSwitch）：
```csharp
public override void OnMissionTick(float dt)
{
    var mission = Mission;
    if (mission == null || !mission.IsLoadingFinished) return;
    if (!IsCombatMission()) return;  // 检查是否存在敌对阵营
    // ... 业务逻辑
}
```

### 4.4 Mission 生命周期

| 现象 | 原因 | 解决方案 |
|------|------|----------|
| 跨 Mission 崩溃（静态 Dictionary 残留旧 Team/Agent 引用） | 静态 Dictionary 用 `Team` 作 key，Mission 结束后 Team 对象失效 | 改用 `Agent.Main.Index` 作 key，在 `OnRemoveBehavior()` 中清理 |
| F6 委派后崩溃 | Mod 的 `SetScriptedPosition` 与原生 AI 接管路径冲突 | 检测 `formation.IsAIControlled`，为 true 时跳过干预 |
| 事件悬挂引用 | `formation.OnBeforeMovementOrderApplied += handler` 在 Mission 结束后未退订 | 在 `OnRemoveBehavior()` 中退订所有事件 |

**MissionBehavior 生命周期方法**：
```
OnMissionTick(float dt)     → 每帧调用
OnRemoveBehavior()          → Mission 结束时调用（必须清理！）
OnEndMission()              → Mission 结束时调用（可选）
```

### 4.5 UI 绑定

| 现象 | 原因 | 解决方案 |
|------|------|----------|
| XML 中 `@PropertyName` 不显示 | ViewModel 属性没有 `[DataSourceProperty]` 特性 | 添加 `[DataSourceProperty]` |
| 属性变更 UI 不更新 | 只赋值了字段，没调用 `OnPropertyChanged` | 在 setter 中调用 `OnPropertyChanged(nameof(Xxx))` |
| 弹窗打开即崩溃 | `GauntletLayer` 创建时没设 `InputRestrictions` | 调用 `layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All)` |
| 弹窗关闭后输入失效 | 没调用 `TryLoseFocus` 和 `RemoveLayer` | 关闭时先 `IsFocusLayer = false` → `TryLoseFocus` → `RemoveLayer` |

### 4.6 性能陷阱

| 现象 | 原因 | 解决方案 |
|------|------|----------|
| 高兵力战场卡顿 | 每 tick 遍历全部 Agent（几百到几千人） | 用 `AgentMarkingSystem` 预标记，只遍历标记集合 |
| LINQ 在热路径中分配内存 | `.Any()`、`.Where()` 每 tick 创建枚举器 | 用 `foreach` + `static HashSet` 替代 |
| 重复扫描 MissionBehaviors | 多个 Behavior 各自调用 `IsConflictModActive` | 用 `MissionBehaviorHelper` 帧缓存，同帧只扫描一次 |

---

## 5. UI 开发指南

### 5.1 GauntletUI XML 布局规范

XML 文件放在 `GUI/Prefabs/` 目录下，文件名即 UI 名称（如 `FormationCrouchPopup.xml`）。

**基本结构**：
```xml
<Prefab>
  <Window>
    <!-- 背景遮罩 -->
    <Widget WidthSizePolicy="StretchToParent" HeightSizePolicy="StretchToParent"
            Sprite="BlankWhiteSquare_9" Color="#0000007F" AlphaFactor="0.5">
      <Children>
        <!-- 主面板 -->
        <Widget WidthSizePolicy="Fixed" HeightSizePolicy="Fixed"
                SuggestedWidth="460" SuggestedHeight="280"
                HorizontalAlignment="Center" VerticalAlignment="Center"
                Sprite="BlankWhiteSquare_9" Color="#1E140AFF">
          <Children>
            <!-- 子控件... -->
          </Children>
        </Widget>
      </Children>
    </Widget>
  </Window>
</Prefab>
```

**常用属性**：
| 属性 | 说明 | 示例值 |
|------|------|--------|
| `WidthSizePolicy` | 宽度策略 | `StretchToParent` / `Fixed` / `CoverChildren` |
| `HeightSizePolicy` | 高度策略 | 同上 |
| `SuggestedWidth` | 固定宽度（配合 Fixed） | `460` |
| `HorizontalAlignment` | 水平对齐 | `Center` / `Left` / `Right` |
| `VerticalAlignment` | 垂直对齐 | `Center` / `Top` / `Bottom` |
| `Sprite` | 背景图 | `BlankWhiteSquare_9`（纯色矩形） |
| `Color` | 颜色（ARGB 十六进制） | `#1E140AFF` |
| `MarginTop/Left/Right/Bottom` | 外边距 | `20` |
| `Text` | 文本内容 | `@Title`（绑定 VM 属性） |
| `Brush.FontSize` | 字体大小 | `22` |
| `Brush.FontColor` | 字体颜色 | `#FFD599FF` |

**按钮绑定**：
```xml
<ButtonWidget DoNotPassEventsToChildren="true"
              WidthSizePolicy="StretchToParent" HeightSizePolicy="StretchToParent"
              Command.Click="ExecuteConfirm">
  <Children>
    <TextWidget Text="@ConfirmText" ... />
  </Children>
</ButtonWidget>
```

> 按钮必须用 `DoNotPassEventsToChildren="true"`，否则点击事件会穿透。

### 5.2 GauntletLayer 完整生命周期

在 `MissionBehavior` 中创建弹窗的完整流程：

**打开弹窗**：
```csharp
private void OpenPopup()
{
    if (_popupOpen) return;

    var playerFormation = Agent.Main?.Formation;
    int defaultIndex = playerFormation != null ? playerFormation.Index : 0;

    var screen = ScreenManager.TopScreen;
    if (screen == null) return;

    // 1. 创建 ViewModel
    _popupVM = new FormationCrouchPopupVM(defaultIndex);
    _popupVM.OnConfirmed += OnPopupConfirmed;
    _popupVM.OnCancelled += OnPopupCancelled;

    // 2. 创建 GauntletLayer（名称, 排序层级, 是否延迟加载）
    _popupLayer = new GauntletLayer("FormationCrouchPopup", 5000, false);

    // 3. 设置输入限制（必须！否则弹窗无法接收点击）
    _popupLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);

    // 4. 加载 Movie（XML 文件名 = prefab 名）
    _popupLayer.LoadMovie("FormationCrouchPopup", _popupVM);

    // 5. 添加到屏幕（需要强制类型转换）
    screen.AddLayer((ScreenLayer)(object)_popupLayer);

    // 6. 设为焦点层（拦截所有输入）
    _popupLayer.IsFocusLayer = true;
    ScreenManager.TrySetFocus((ScreenLayer)(object)_popupLayer);
    _popupOpen = true;
}
```

**关闭弹窗**（顺序严格！）：
```csharp
private void ClosePopup()
{
    if (!_popupOpen || _popupLayer == null) return;

    var screen = ScreenManager.TopScreen;
    if (screen != null)
    {
        // 1. 取消焦点
        _popupLayer.IsFocusLayer = false;
        ScreenManager.TryLoseFocus((ScreenLayer)(object)_popupLayer);
        // 2. 移除 Layer
        screen.RemoveLayer((ScreenLayer)(object)_popupLayer);
    }

    // 3. 清理 ViewModel
    SafeCleanupPopup();
}

private void SafeCleanupPopup()
{
    if (_popupVM != null)
    {
        _popupVM.OnConfirmed -= OnPopupConfirmed;
        _popupVM.OnCancelled -= OnPopupCancelled;
        _popupVM.OnFinalize();  // 必须调用，释放 Gauntlet 资源
        _popupVM = null;
    }
    _popupLayer = null;
    _popupOpen = false;
}
```

> **类型转换说明**：`GauntletLayer` 不是 `ScreenLayer` 的子类，需要 `(ScreenLayer)(object)` 双重转换。这是 TaleWorlds UI 框架的设计缺陷，但必须这样写。

### 5.3 ViewModel 绑定规则

```csharp
public class FormationCrouchPopupVM : ViewModel
{
    // 1. 必须用 [DataSourceProperty] 特性
    [DataSourceProperty]
    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged(nameof(Title));  // 2. 必须通知变更
            }
        }
    }

    // 3. 按钮方法命名规则：Execute + 方法名
    public void ExecuteConfirm() => OnConfirmed?.Invoke();
    public void ExecuteCancel() => OnCancelled?.Invoke();
}
```

**绑定规则总结**：
1. 属性必须加 `[DataSourceProperty]`
2. setter 中必须调用 `OnPropertyChanged(nameof(PropName))`
3. 按钮方法必须以 `Execute` 开头
4. XML 中用 `@PropertyName` 引用属性

### 5.4 快捷键集成方式

在 `MissionBehavior.OnMissionTick` 中检测按键：

```csharp
private void HandleInput()
{
    bool ctrlHeld = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);

    if (ctrlHeld && Input.IsKeyPressed(InputKey.B))
    {
        OpenPopup();  // 打开 UI
    }
}
```

> **注意**：`Input` 是静态类，不能实例化。直接用 `Input.IsKeyDown(...)` 调用。

---

## 6. MCM 配置规范

### 6.1 分组与排序

1.4.8 兼容方案：**所有设置放同一组，用 Order 排序**。

```csharp
[SettingPropertyGroup("部队行为调整")]
[SettingPropertyBool("旗手保护", Order = 1, RequireRestart = false,
    HintText = "说明文字")]
public bool EnableBannerBearerDefense { get; set; }

[SettingPropertyGroup("部队行为调整")]
[SettingPropertyBool("→ 仅友军：旗手保护", Order = 5, RequireRestart = false,
    HintText = "说明文字")]
public bool FriendOnlyBannerBearer { get; set; }
```

- `Order` 从小到大排列
- 子选项用 `→` 前缀视觉缩进
- 主开关在前（Order 1-4），范围限定在后（Order 5-8）

### 6.2 中文化

- `DisplayName`：MCM 页面标题 → `"部队行为调整"`
- `SettingPropertyBool` 第一个参数：选项名 → 中文
- `HintText`：悬停提示 → 中文说明

### 6.3 冲突策略

用整数 Slider 代替枚举下拉（MCM v5 兼容性更好）：

```csharp
[SettingPropertyInteger("Mod 冲突策略", 0, 2, Order = 0, RequireRestart = false,
    HintText = "0 = 默认  1 = 覆盖  2 = 让步")]
public int ConflictStrategySlider { get; set; }

// 代码中转为枚举
public ConflictStrategy ConflictStrategy => (ConflictStrategy)_conflictStrategyInt;
```

---

## 7. 发布流程

### 7.1 编译

```powershell
cd "E:\steam\steamapps\common\Mount & Blade II Bannerlord\Modules\SoldierBehaviorTweaks\src"
dotnet build
```

确认输出：**0 errors, 0 warnings**。

### 7.2 同步到桌面发布副本

```powershell
$dest = "E:\桌面\部队行为调整\SoldierBehaviorTweaks"
Copy-Item "...\SoldierBehaviorTweaks\bin\Win64_Shipping_Client\SoldierBehaviorTweaks.dll" "$dest\bin\Win64_Shipping_Client\" -Force
Copy-Item "...\SoldierBehaviorTweaks\GUI\Prefabs\FormationCrouchPopup.xml" "$dest\GUI\Prefabs\" -Force
Copy-Item "...\SoldierBehaviorTweaks\SubModule.xml" "$dest\" -Force
```

### 7.3 测试清单

| 测试项 | 操作 | 预期结果 |
|--------|------|----------|
| Mod 加载 | 启动游戏，检查 Mod 列表 | SoldierBehaviorTweaks 出现，无冲突提示 |
| MCM 设置 | 进入 MCM 界面 | 显示"部队行为调整"分组，8 个选项正常 |
| 旗手保护 | 进入战斗，观察旗手位置 | 旗手在编队后排，防御姿态 |
| 投掷禁用近战 | 带投掷武器的士兵接敌 | 士兵不会切投掷武器到近战模式 |
| 双手武器优先 | 步兵带单手+双手武器接敌 | 自动切换到双手武器 |
| 蹲下控制 | 战斗中按 Ctrl+B | 弹出编队选择弹窗 |
| 蹲下快捷键 | 战斗中按 Ctrl+1 | 编队 1 蹲下/站立切换 |
| F6 委派 | 战斗中按 F6 委派 AI | 不崩溃，Mod 功能暂停 |
| 退出战斗 | 结束战斗返回地图 | 不崩溃，无残留状态 |
| 冲突 Mod | 同时启用 RBM | 按冲突策略处理，不崩溃 |

---

## 附录 A：关键 API 速查

| API | 命名空间 | 用途 |
|-----|----------|------|
| `MissionBehavior` | `TaleWorlds.MountAndBlade` | 战斗行为基类 |
| `MBSubModuleBase` | `TaleWorlds.MountAndBlade` | Mod 入口基类 |
| `Agent` | `TaleWorlds.MountAndBlade` | 士兵/角色对象 |
| `Formation` | `TaleWorlds.MountAndBlade` | 编队对象 |
| `Team` | `TaleWorlds.MountAndBlade` | 阵营对象 |
| `MissionWeapon` | `TaleWorlds.MountAndBlade` | 武器对象 |
| `MissionEquipment` | `TaleWorlds.MountAndBlade` | 装备栏 |
| `WorldPosition` | `TaleWorlds.Engine` | 世界坐标（需引用 TaleWorlds.Engine） |
| `GauntletLayer` | `TaleWorlds.Engine.GauntletUI` | UI 层 |
| `ScreenBase` | `TaleWorlds.ScreenSystem` | 屏幕基类 |
| `ViewModel` | `TaleWorlds.Library` | UI 数据绑定基类 |
| `Input` | `TaleWorlds.InputSystem`（静态类） | 输入检测 |
| `InformationManager` | `TaleWorlds.Library` | 左下角消息提示 |

## 附录 B：常用命名空间

```csharp
using TaleWorlds.Core;              // CharacterObject, EquipmentIndex, WeaponClass
using TaleWorlds.Engine;            // WorldPosition, Vec2
using TaleWorlds.Engine.GauntletUI; // GauntletLayer
using TaleWorlds.InputSystem;       // Input, InputKey
using TaleWorlds.Library;           // ViewModel, Debug, InformationManager, MBBindingList, Color
using TaleWorlds.MountAndBlade;     // MissionBehavior, Agent, Formation, Team, MBSubModuleBase
using TaleWorlds.ScreenSystem;      // ScreenBase, ScreenManager, ScreenLayer
using MCM.Abstractions.Attributes.v2; // SettingPropertyBool, SettingPropertyInteger 等
using MCM.Abstractions.Base.Global;   // AttributeGlobalSettings<T>
```

## 附录 C：新增功能检查清单

新增一个 MissionBehavior 时，按此清单逐项确认：

1. **继承** `MissionBehavior`，实现 `BehaviorType`
2. **守卫三连**：`mission == null` → `!IsLoadingFinished` → `!IsCombatMission()`
3. **F6 防护**：检查 `formation.IsAIControlled`，为 true 时跳过干预
4. **OnRemoveBehavior**：清理所有 Dictionary/HashSet，退订所有事件
5. **OnEndMission**：关闭 UI（如果有弹窗）
6. **SubModule.cs**：在 `OnMissionBehaviorInitialize` 中 `AddMissionBehavior`
7. **MCM 开关**：在 `AIBannerFixSettings.cs` 中添加设置项
8. **冲突策略**：检查 `settings.ConflictStrategy == ConflictStrategy.Yield` 时让位
9. **性能**：用 `AgentMarkingSystem` 预标记集合遍历，不在热路径用 LINQ
10. **try-catch**：核心逻辑包在 try-catch 中，错误用 `Debug.Print` 输出
