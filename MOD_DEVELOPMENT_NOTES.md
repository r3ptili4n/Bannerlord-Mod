# SoldierBehaviorTweaks（部队行为微调）开发版维护说明

> 这是一份给本 Mod 后续开发、排错和版本迁移使用的项目说明。
> 它记录当前目录结构、Mod 身份、BannerlordSage 检查结论、已经确认的设计决策、已完成修复、构建结果和待进行的游戏内验证。
> 本文不是 `SubModule.xml`、MCM 配置文件，也不会被游戏加载。
>
> **记录日期：2026-09-03**  `feature` 分支状态同步：2026-09-03

---

## 1. Mod 身份和实际路径

### 1.1 开发版实际目录

- 外层开发版目录：
  `E:\steam\steamapps\common\Mount & Blade II Bannerlord\Modules\部队行为微调Dev\`
- 实际 Bannerlord 模块目录：
  `E:\steam\steamapps\common\Mount & Blade II Bannerlord\Modules\部队行为微调Dev\SoldierBehaviorTweaks\`
- C# 源码目录：
  `E:\steam\steamapps\common\Mount & Blade II Bannerlord\Modules\部队行为微调Dev\SoldierBehaviorTweaks\src\`
- 编译输出目录：
  `E:\steam\steamapps\common\Mount & Blade II Bannerlord\Modules\部队行为微调Dev\SoldierBehaviorTweaks\bin\Win64_Shipping_Client\`
- UI 文件：
  `E:\steam\steamapps\common\Mount & Blade II Bannerlord\Modules\部队行为微调Dev\SoldierBehaviorTweaks\GUI\Prefabs\FormationCrouchPopup.xml`

### 1.2 不要改变的 Mod 身份

当前 `SubModule.xml` 和项目文件确定了以下身份：

- 模块 ID：`SoldierBehaviorTweaks`
- 模块显示名：`部队行为微调`
- 程序集名：`SoldierBehaviorTweaks`
- DLL：`SoldierBehaviorTweaks.dll`
- SubModule 类：`SoldierBehaviorTweaks.SubModule`
- 模块版本字段：`v1.4.8`
- 当前程序集目标：`.NET Framework 4.7.2`、`x64`

**长期版和开发版不是两个需要重新命名的 Mod。**
开发版只是把同一个实际模块目录放在 `部队行为微调Dev` 外层目录下。这个目录布局是为了配合 Bannerlord 的模块扫描层级，并不是要创建第二套模块 ID 或第二个程序集名。

因此后续不要因为“开发版”而修改：

- `<Id value="SoldierBehaviorTweaks" />`
- `AssemblyName` 的 `SoldierBehaviorTweaks`
- `SoldierBehaviorTweaks.dll`
- `SoldierBehaviorTweaks.SubModule`
- 现有的长期版/开发版外层目录关系

不要通过改 ID、改 DLL 名、复制一套带新程序集名的项目来解决问题。若要测试开发版，应继续使用当前外层目录方案，并注意不要让两个相同 ID 的实际模块同时处于游戏可扫描位置。

### 1.3 当前模块依赖

`SubModule.xml` 当前依赖：

- `Native`
- `SandBoxCore`
- `Sandbox`
- `Bannerlord.MBOptionScreen`

MCM 程序集依赖：

- `MCMv5.dll`
- 项目包版本：`Bannerlord.MCM 5.11.4`
- 编译期目标框架引用包：`Microsoft.NETFramework.ReferenceAssemblies 1.0.3`（`PrivateAssets=All`，仅用于构建，不作为 Mod 运行时依赖）

---

## 2. Git 和分支约定

- Git 仓库根目录：
  `E:\steam\steamapps\common\Mount & Blade II Bannerlord\Modules\部队行为微调Dev\SoldierBehaviorTweaks\`
- 当前已经存在并正在使用的分支：**`feature`**
- 不要创建新的 `feature/...` 分支，也不要切换到一个新建分支。
- 本次修改范围只针对开发版 Mod 源码。
- 不修改长期版，不修改模块 ID、程序集名和安装结构。
- 用户明确希望方案保持简单，只做已经确认的几个逻辑修复，不额外引入复杂架构。

当前基线提交：

```text
358be81 更新旗手/捡旗/武器切换/下蹲逻辑并重编译
```

之前曾尝试用 PowerShell 批量修改源码，但脚本解析失败，随后操作被中断；在本说明创建前源码没有留下那次尝试的修改。后续应直接在现有 `feature` 分支上做小范围、可审查的修改。

本文件位于 Mod 根目录，属于维护文档；除非用户要求，否则不需要自动提交 Git。

---

## 3. 当前 Mod 的功能组成

`src\SubModule.cs` 在每个 Mission 初始化以下五个行为，顺序不要随意改变：

1. `AgentMarkingSystem`
2. `BannerBearerDefenseGuard`
3. `BannerPickupBehavior`
4. `AutoMeleeWeaponSwitch`
5. `CrouchToggleBehavior`

### 3.1 AgentMarkingSystem

文件：`src\AgentMarkingSystem.cs`

职责：

- 维护当前 Mission 中的活动 Agent 索引。
- 标记有投掷武器、双手武器等相关武器的 Agent。
- 标记骑兵 Agent，供武器逻辑排除或判断。
- 维护拥有旗手的 Formation 集合。
- 初始扫描和定时增量扫描，并监听原生旗帜逻辑的更新事件。
- 为旗帜拾取和其他行为提供 Mission 内的 Agent 查询缓存。

它的缓存属于 Mission 行为实例，死亡 Agent 和结束 Mission 时必须清理，不能把 Mission 中的 Agent 引用做成全局静态状态。

### 3.2 BannerBearerDefenseGuard

文件：`src\BannerBearerDefenseGuard.cs`

职责：

- 让步兵或弓箭手旗手尽量保持在编队后方。
- 让旗手保持防御姿态。
- 处理冲锋时编队位置的轻微前推。
- 通过 `Formation.OnBeforeMovementOrderApplied` 观察编队运动命令，处理 F6 委派后的旧脚本移动释放。
- 旗手受保护范围不包括骑乘 Agent。

关键安全点：

- F6 委派后要释放本 Mod 给旗手设置的 `SetScriptedPosition`。
- 同时恢复 `SetMaximumSpeedLimit`，避免旗手继续被旧速度限制影响。
- 委派后的编队应让原生 AI 接管，但不能因为检测到委派就跳过清理动作。
- Mission 结束或行为移除时要退订所有 Formation 事件。

### 3.3 BannerPickupBehavior

文件：`src\BannerPickupBehavior.cs`

职责：

- 扩展原生旗帜搜索范围，在掉落旗帜距离较远时寻找同编队合适的士兵。
- 根据 `BattleBannerBearersModel` 的优先级选择搜索者。
- 指派搜索者前往旗帜。
- 接近旗帜后调用原生拾取流程。
- 维护“旗帜实体 -> Agent 索引”的 Mission 内分配表。

当前主要参数：

- 检查间隔：`0.75s`
- 搜索半径：`35m`
- 拾取半径：`2.2m`
- 搜索者重新分配容差：`6m`

关键安全点：

- 搜索者离开任务、死亡、失效、换编队或不再符合条件时，必须调用 `DisableScriptedMovement()`。
- 拾取成功、旗帜消失、距离过远、功能关闭、冲突让步、行为移除等路径都要释放搜索者。
- 清空分配表不能代替释放脚本移动；必须先释放，再清空记录。
- 不要因为搜索者已经不在活动字典中就完全跳过清理；能通过 `Mission.FindAgentWithIndex` 找到时应尽量释放。
- 地面旗帜对象可能在原生拾取调用中立即消失，因此应先复制候选列表，再执行拾取。

### 3.4 AutoMeleeWeaponSwitch

文件：`src\AutoMeleeWeaponSwitch.cs`

当前功能：

- 禁止 AI 把投掷武器切换为近战模式（玩家不受影响）。
- 在可用时优先双手武器。
- 步兵尽量避免纯戳刺长杆，优先可挥砍武器。
- 根据附近敌人和敌方骑兵判断是否切换武器。
- 骑兵、玩家 Agent 等按现有条件排除。
- 使用冷却时间，避免每帧反复切换。

维护时不要把“武器切换”逻辑和旗手/蹲伏状态混在一起。各行为通过 `AgentMarkingSystem` 获取缓存，但状态仍应保留在各自的 Mission 行为实例中。

### 3.5 CrouchToggleBehavior 和蹲伏弹窗

文件：

- `src\CrouchToggleBehavior.cs`
- `src\FormationCrouchPopupVM.cs`
- `GUI\Prefabs\FormationCrouchPopup.xml`

操作方式：

- `Ctrl+B`：打开编队蹲伏设置窗口。
- `Ctrl+1` 到 `Ctrl+8`：切换对应编队。
- 如果对应编队没有单位，则按现有逻辑切换所有有效编队。
- 弹窗允许选择编队并设置“蹲下/起身”。

蹲伏应用的原则：

- 只处理玩家侧、仍由玩家控制的稳定编队。
- 不处理玩家主 Agent。
- 不处理骑乘 Agent。
- F6 委派后不能继续强制蹲伏。
- Mission 结束、行为移除、Agent 死亡或换队时应恢复并清理追踪状态。

当前真正运行的维护路径是 `MaintainCrouchStates()`。旧的 `ApplyCrouchStates(float dt)` 及其唯一调用方 `CleanDeadCrouchAgents(Mission)` 已在本轮修复中删除。

---

## 4. MCM 设置和默认值

设置类：`src\AIBannerFixSettings.cs`

所有设置当前 `RequireRestart = false`。默认值如下：

| 设置 | 默认值 | 说明 |
|---|---:|---|
| Mod 冲突策略 | `0` | 默认：两者均运行 |
| 旗手保护 | 开 | 旗手保持在编队后排并防御 |
| 旗帜拾取 | 开 | 掉落旗帜由附近合适士兵回收 |
| 投掷武器 — 禁用近战 | 开 | AI 投掷武器不能切换为近战模式 |
| 双手武器优先 | 开 | 可用时优先双手武器 |
| 降低纯戳刺长杆权重 | 开 | 步兵尽量避免纯戳刺长杆 |
| 仅友军：旗手保护 | 关 | 关闭时可作用于符合条件的其他队伍 |
| 仅友军：旗帜拾取 | 开 | 默认只处理玩家及友方部队 |
| 仅友军：投掷禁用近战 | 关 | 默认不限制为友军专用 |
| 仅友军：双手优先 | 关 | 默认不限制为友军专用 |
| 仅友军：长杆降权 | 关 | 默认不限制为友军专用 |

冲突策略含义：

- `0 Default`：本 Mod 和冲突 Mod 都运行。
- `1 Override`：本 Mod 优先。
- `2 Yield`：检测到冲突 Mod 时，本 Mod 让步。

冲突处理应做到“停止本 Mod 的主动控制并清理已设置的状态”，不能只 `return` 而把 Agent 留在脚本移动、速度限制或蹲伏状态中。

---

## 5. BannerlordSage 和版本确认

BannerlordSage 本地目录：

```text
E:\steam\steamapps\common\Mount & Blade II Bannerlord\Modules\BannerlordSage-main\
```

用户的长期要求：**以后所有骑砍 2 Mod 相关问题，优先查询 BannerlordSage，再根据实际源码和索引结果决定是否修改。**

截至本次检查：

- 游戏版本：`v1.4.8.119303`
- 游戏 changeset：`119303`
- 官方 API 文档版本：`1.4.8`
- `api_exact_match = true`
- 官方 API 与当前游戏版本没有版本不匹配警告。
- 开发版源码已经被 BannerlordSage 索引：9 个 C# 文件、10 个类型、104 个成员。
- XML 解析失败数：0。
- 缺失官方 DLL：无。

这里要区分三件事：

1. **Bannerlord 游戏版本**：当前是 `1.4.8.119303`。
2. **Bannerlord 官方 API 文档版本**：当前索引使用 `1.4.8`，并且与游戏精确匹配。
3. **BannerlordSage MCP/索引工具本身**：它不是“游戏版本 1.4.8”，而是读取本机游戏、官方文档和 Mod 源码的工具。不能把工具状态简单称为“BannerlordSage 版本就是 1.4.8”。

已经核对过、目前在 1.4.8 中存在且签名可用的关键 API 包括：

- `Agent.SetCrouchMode`
- `Agent.SetScriptedPosition`
- `Agent.DisableScriptedMovement`
- `Agent.SetMaximumSpeedLimit`
- `Formation.SetPositioning`
- `Formation.OnBeforeMovementOrderApplied`
- `Agent.OnItemPickup`
- `Agent.CanQuickPickUp`
- `Agent.SetWieldedItemIndexAsClient`
- `Agent.SetUsageIndexOfWeaponInSlotAsClient`
- `BannerBearerLogic` 的旗手和旗帜查询接口

结论：当前没有发现因为 1.4.8 删除、改名或改变参数而必须进行的 API 迁移。现在要修的是已确认的逻辑生命周期问题，而不是盲目更换 API。

### 5.1 BannerlordSage 更新和命令行注意事项

曾遇到 PowerShell 禁止执行 `bun.ps1`：

```text
bun : 无法加载文件 ...\bun.ps1，因为在此系统上禁止运行脚本。
```

这不代表 BannerlordSage MCP 断开。PowerShell 下可以优先使用 `bun.cmd`，例如：

```powershell
bun.cmd run index:api-docs -- --version 1.4.8
```

如果以后游戏升级：

1. 先用 BannerlordSage 检查游戏实际完整版本。
2. 刷新或重新建立对应版本的官方 API 索引。
3. 确认 `api_exact_match`。
4. 用 `bannerlord_doctor` 检查模块依赖、重复 DLL、缺失 SubModule DLL 和加载顺序。
5. 再检查本 Mod 源码中的 API，而不是只根据旧文档或旧经验修改。

---

## 6. 本轮 `feature` 分支功能修复（已完成）

以下是已经确认、并在现有 `feature` 分支完成的最小功能范围，未额外扩展需求。以下子节保留原始问题与目标说明，作为改动记录；实际验证清单见第 9 节。

### 6.1 旗手：F6 委派后释放旧脚本移动

问题位置：`BannerBearerDefenseGuard.ReleasePendingBearers()`。

当前问题：

```csharp
if (MissionBehaviorHelper.IsPlayerSideFormationDelegated(formation))
    continue;
```

这个 `continue` 会导致 F6 委派后的编队跳过 `ReleaseBearersInFormation()`，从而可能留下：

- 旗手的 `SetScriptedPosition()`；
- 旗手的速度限制；
- `_pendingBearerRelease` 中的残留编队记录。

目标修复：

- 即使编队已经委派，也要执行 `ReleaseBearersInFormation(formation, bannerLogic)`。
- 执行后把 Formation 从 `_pendingBearerRelease` 移除。
- `ReleaseBearersInFormation()` 继续负责：
  - `DisableScriptedMovement()`；
  - `SetMaximumSpeedLimit(-1f, false)`。
- 保护逻辑本身仍然可以在委派编队上让位；“让位”与“清理旧状态”不能混为一谈。

### 6.2 旗帜搜索者：统一释放

问题位置：`BannerPickupBehavior`。

当前类中已有单个搜索者释放方法 `ReleaseSearcher()`，但不是所有退出路径都使用它。需要增加一个统一释放方法，建议命名：

```csharp
private void ReleaseAllSearchers(Mission? mission)
```

目标行为：

- 遍历 `_assignedAgentByBanner.Values`。
- 按 Agent 索引去重，避免同一搜索者被重复处理。
- 通过 `mission?.FindAgentWithIndex(index)` 找到 Agent。
- Agent 仍存在且有效时调用 `DisableScriptedMovement()`。
- 无论 Agent 是否还能找到，最后都清空 `_assignedAgentByBanner`。
- 单个搜索者释放失败不能阻止其他搜索者释放。

至少以下路径必须调用统一释放：

- `EnableBannerPickup == false`；
- `ConflictStrategy.Yield` 且发现冲突 Mod；
- `BannerBearerLogic` 不存在；
- `AgentMarkingSystem` 不存在；
- Mission 行为移除 `OnRemoveBehavior()`；
- Mission 结束或其他总清理路径（若当前 API 生命周期适合覆盖）。

此外，搜索者不再符合条件、旗帜消失、距离过远、拾取完成时，也要释放对应搜索者的脚本移动。

### 6.3 蹲伏状态改为 Mission 实例状态

问题位置：`CrouchToggleBehavior` 顶部的静态字典。

当前实现使用：

```csharp
private static readonly Dictionary<int, HashSet<int>> _perPlayerCrouchFormations = new();
private static readonly object _lockObj = new();
```

并通过 `Agent.Main.Index` 区分任务。这个设计没有必要，而且可能让不同 Mission 共享状态或依赖主 Agent 索引，形成跨战场残留风险。

目标实现：

```csharp
private readonly HashSet<int> _crouchFormations = new();
```

要求：

- `CrouchToggleBehavior` 每个 Mission 使用自己的实例集合。
- 删除 `GetOrCreateCrouchSet()`。
- 删除 `_lockObj` 和不再需要的锁。
- 删除 `CleanStaleEntries()`。
- 将 `IsFormationCrouchEnabled(int formationIndex)` 改为实例方法。
- 将 `SetFormationCrouch(int formationIndex, bool enabled)` 改为实例方法。
- `MaintainCrouchStates()` 和 `BootstrapCrouchState()` 直接复制或读取当前实例的 `_crouchFormations`。
- `OnRemoveBehavior()` 和 `OnEndMission()` 清空 `_crouchFormations`。
- 不要新增全局静态 Mission 状态。

### 6.4 蹲伏弹窗改为使用当前行为实例

问题位置：`FormationCrouchPopupVM`。

当前 VM 直接调用静态蹲伏查询：

```csharp
CrouchToggleBehavior.IsFormationCrouchEnabled(playerTeam, _selectedFormationIndex)
```

蹲伏状态改为 Mission 实例状态后，VM 不能再依赖静态方法。推荐让构造函数接收一个回调：

```csharp
private readonly Func<int, bool> _isFormationCrouchEnabled;
```

然后由 `CrouchToggleBehavior.OpenPopup()` 传入当前实例的 `IsFormationCrouchEnabled` 方法。

这样弹窗只读取当前 Mission 的状态，不需要重新引入静态状态。

### 6.5 删除未调用的旧实现

删除 `CrouchToggleBehavior.cs` 中：

- `private void ApplyCrouchStates(float dt)`；
- 只被它使用的 `CleanDeadCrouchAgents(Mission mission)`。

保留并继续使用：

- `MaintainCrouchStates(float dt)`；
- `CleanTrackedCrouchAgents(AgentMarkingSystem marking)`；
- `StandDownTrackedAgents(Mission mission)`；
- Agent 生命周期同步方法。

删除后要确认：

- 没有任何 `ApplyCrouchStates` 搜索结果；
- 没有任何 `CleanDeadCrouchAgents` 搜索结果；
- 没有任何 `GetOrCreateCrouchSet` 或 `_perPlayerCrouchFormations` 搜索结果；
- `FormationCrouchPopupVM` 不再调用静态蹲伏接口。

---

## 7. 1.4.8 适配结论

基于当前游戏 `v1.4.8.119303`、官方 API `1.4.8` 和 BannerlordSage 的源码检查：

- 没有发现必须进行的 API 名称迁移。
- 没有发现 `SetCrouchMode`、脚本移动、旗帜拾取、Formation 运动事件等关键 API 在 1.4.8 中不可用。
- 当前应优先修复对象生命周期和状态清理，而不是重写现有功能。
- 修改应尽量局限于：
  - `BannerBearerDefenseGuard.cs`
  - `BannerPickupBehavior.cs`
  - `CrouchToggleBehavior.cs`
  - `FormationCrouchPopupVM.cs`
- 暂不修改：
  - `SubModule.xml` 的模块 ID 和依赖；
  - `AIBannerFix.csproj` 的 `AssemblyName`、目标框架和输出目录结构（本轮仅在项目文件中新增 `Microsoft.NETFramework.ReferenceAssemblies` 编译期引用包，未改变 Mod 身份）；
  - 长期版目录；
  - DLL 名称；
  - 与本次四项修复无关的武器算法。

---

## 8. 编译和部署注意事项

### 8.1 项目配置

`src\AIBannerFix.csproj` 当前关键配置：

```xml
<TargetFramework>net472</TargetFramework>
<Platforms>x64</Platforms>
<LangVersion>10.0</LangVersion>
<Nullable>enable</Nullable>
<AssemblyName>SoldierBehaviorTweaks</AssemblyName>
<OutputPath>..\bin\Win64_Shipping_Client\</OutputPath>
```

项目引用当前游戏目录中的 TaleWorlds DLL，并引用 MCMv5。不要把 `obj` 目录里的 DLL 当作最终安装输出；最终 DLL 应位于模块的 `bin\Win64_Shipping_Client`。

### 8.2 当前构建环境和本次构建结果

2026-09-03 已重新检查并完成一次 Release 构建：

- 系统 PATH 中没有可直接调用的 `dotnet` SDK 或 `msbuild`。
- 本机实际可用 SDK 位于：`C:\Users\r3ptili4n\AppData\Local\Microsoft\dotnet\sdk\8.0.424\`。
- 项目通过 `Microsoft.NETFramework.ReferenceAssemblies 1.0.3` 提供 `.NET Framework 4.7.2` 编译期引用程序集。
- 构建命令：`C:\Users\r3ptili4n\AppData\Local\Microsoft\dotnet\dotnet.exe build .\src\AIBannerFix.csproj -c Release --nologo /p:Platform=x64`
- 构建结果：成功，`0` 个警告，`0` 个错误。
- 输出文件：`bin\Win64_Shipping_Client\SoldierBehaviorTweaks.dll` 和对应 `SoldierBehaviorTweaks.pdb`。

以后若换机器或 SDK 路径失效，优先安装/恢复 .NET SDK 与 .NET Framework 4.7.2 参考程序集；不要为了绕过构建错误修改项目目标框架、程序集名称或 Mod 结构。

### 8.3 源码修改后的基本检查

在源码目录执行：

```powershell
git status --short --branch
git diff --check
git diff --stat
```

可用 SDK 和 MSBuild 后再执行：

```powershell
dotnet build .\AIBannerFix.csproj -c Release /p:Platform=x64
```

构建成功后确认：

```text
..\bin\Win64_Shipping_Client\SoldierBehaviorTweaks.dll
```

不要在游戏运行时覆盖正在加载的 DLL。修改前后建议保留旧 DLL 备份，并通过日志确认：

```text
[SoldierBehaviorTweaks] loaded
```

---

## 9. 当前实现状态和功能验证清单

### 9.1 本阶段源码和构建状态（2026-09-03）

已完成：

- [x] 旗手 F6 委派后的旧脚本移动和速度限制清理。
- [x] 旗帜搜索者的统一释放路径，包括功能关闭、冲突让步、依赖行为缺失、旗帜消失、搜索者失效、距离过远、拾取完成、行为移除和 Mission 结束。
- [x] 蹲伏编队状态改为 `CrouchToggleBehavior` 的 Mission 实例字段，不再使用跨 Mission 静态字典。
- [x] 蹲伏弹窗通过当前行为实例回调读取状态。
- [x] 删除未调用的 `ApplyCrouchStates` 和 `CleanDeadCrouchAgents` 旧路径。
- [x] 源码搜索确认旧符号在 `src` 中无残留。
- [x] `git diff --check` 通过。
- [x] Release 构建成功并生成新的 `SoldierBehaviorTweaks.dll` / `SoldierBehaviorTweaks.pdb`。

当前工作树仍有未提交修改；本次没有自动执行 Git commit。

仍待完成：

- [ ] 进入游戏验证 F6 后旗手确实恢复原生控制。
- [ ] 进入游戏验证掉旗、搜索者释放、冲突让步和功能关闭。
- [ ] 进入游戏验证蹲伏的跨 Mission 隔离、F6 让位、死亡/换队/上下马同步。
- [ ] 确认游戏日志出现 `[SoldierBehaviorTweaks] loaded`，并检查无相关异常。

### 9.2 旗手保护和 F6

- 普通玩家控制编队：旗手会移动到编队后方并保持防御。
- 对旗手编队执行 F6：旗手停止本 Mod 的脚本移动。
- F6 后不再保留旧速度限制。
- 随后原生 AI 可以接管编队。
- 多次下达移动命令不会重复订阅事件或留下 `_pendingBearerRelease`。
- Mission 结束后事件已退订。

### 9.3 掉落旗帜

- 近处掉落旗帜能正常拾取。
- 搜索者被指派后，旗帜消失时会停止脚本移动。
- 搜索者死亡或换编队时会释放。
- 距离过远时会释放并允许后续重新分配。
- 关闭功能后，已经在路上的搜索者也会释放。
- 冲突策略选择让步并检测到冲突 Mod 时，不会残留搜索者脚本移动。
- Mission 结束/移除行为后没有残留 Agent 控制。

### 9.4 蹲伏

- `Ctrl+1` 到 `Ctrl+8` 能切换对应编队。
- `Ctrl+B` 弹窗显示的是当前 Mission 的状态。
- 在 Mission A 蹲下编队后结束战斗，Mission B 不会继承状态。
- F6 委派后不会继续强制蹲伏。
- Agent 死亡、换队、上下马时状态能正确同步。
- 关闭或让步冲突后，已蹲下 Agent 能恢复站立。
- Mission 结束和行为移除不会抛出异常。

### 9.5 其他行为回归

- 投掷武器禁用近战逻辑仍然有效。
- 双手武器优先逻辑仍然有效。
- 纯戳刺长杆降权逻辑仍然有效。
- 玩家 Agent 和骑兵的既有排除条件没有被本次修改破坏。
- MCM 设置能实时生效，不需要强制重启。

---

## 10. 维护原则（后续修改必须遵守）

1. **先查 BannerlordSage，再改代码。** 尤其是 Bannerlord 升级后，先确认实际版本和 API 签名。
2. **状态必须绑定 Mission 生命周期。** 不要为 Agent、Formation、旗帜或蹲伏状态新增跨 Mission 的静态容器。
3. **让步不是清理。** `return`、禁用功能或检测到冲突前，先释放本 Mod 已经设置的脚本移动、速度限制和蹲伏状态。
4. **事件一定要成对订阅/退订。** 任何 Formation 或 BannerBearerLogic 事件都要在行为移除时解除订阅。
5. **先复制原生可能修改的集合，再执行拾取或删除。** 旗帜拾取尤其要遵守这一点。
6. **保留现有过滤条件。** 不要无意中把玩家、骑兵、非友军或 F6 委派编队纳入不应处理的逻辑。
7. **小范围修改。** 当前阶段只处理已经确认的四项修复，不引入新的模块、Harmony 补丁或额外线程。
8. **不改变长期版和开发版的模块身份。** 外层目录是开发流程安排，不是程序集隔离方案。
9. **不把编译失败误判为游戏 API 不兼容。** 先区分 SDK/MSBuild 环境问题、引用问题和真正的 1.4.8 API 问题。
10. **完成后必须做源码搜索和 Git diff 检查。** 尤其确认旧静态蹲伏实现已彻底移除。

---

## 11. 本项目已经确定的对话结论

以下决定来自本 Mod 的开发讨论，后续不要反复推翻：

- 这是开发版 `SoldierBehaviorTweaks` 的源码维护任务。
- 源码位置是 `部队行为微调Dev\SoldierBehaviorTweaks\src`。
- 长期版和开发版实际使用同一个模块身份；开发版只通过外层 `部队行为微调Dev` 目录区分。
- 不修改模块 ID、程序集名、DLL 结构、长期版目录或安装层级。
- 当前 Git 分支已经是 `feature`，后续直接在这个分支工作。
- 用户不希望把简单修复做得过于复杂。
- 当前优先处理四项：
  1. 旗手 F6 释放旧脚本移动；
  2. 旗帜搜索者统一释放；
  3. 蹲伏状态改为 Mission 实例状态；
  4. 删除未调用的 `ApplyCrouchStates` 旧实现。
- 四项源码修复已经完成并通过本地 Release 编译；仍不能把它等同于完成游戏内实战验证。
- 以后遇到骑砍 2 Mod、Bannerlord API 或 1.4.8 兼容问题，优先使用本机 BannerlordSage 查询，并以当前安装版本的源码/API 结果为准。

---

## 12. 完成标准

本阶段只有同时满足以下条件，才算完成：

- 所有修改都在现有 `feature` 分支。
- 只修改开发版源码和必要的本说明文档。
- 旗手 F6 委派会释放旧脚本移动和速度限制。
- 所有旗帜搜索者退出路径都会统一释放。
- 蹲伏状态不再使用跨 Mission 静态字典。
- 弹窗读取当前 Mission 实例状态。
- `ApplyCrouchStates`、`CleanDeadCrouchAgents` 等未调用旧路径已删除。
- `git diff --check` 通过。
- 在可用构建环境中成功生成 `SoldierBehaviorTweaks.dll`，或明确记录构建环境阻塞原因。
- 完成至少一次 F6、掉旗、冲突让步、蹲伏跨 Mission 的实战验证。
