# CLAUDE.md — 《规则游戏》项目说明

> 本文件是**团队共享**的项目说明，随 git 提交，所有协作者（及其 Claude）都会读到。
> 个人笔记请放 **`CLAUDE.local.md`**（已在 `.gitignore` 中，队友看不到）。

---

## 项目是什么

**《规则游戏》** —— 2026 年 26TapTap聚光灯 GameJam 参赛作品，活动主题「**涌现（Emergence）**」。
单机、2D 横版、关卡制**平台动作解谜**，单人游玩，单关约 10 分钟。
**开发周期 2026-10-01 ~ 10-21**；硬性约束：禁止联网、禁止广告、必须上架 TapTap。

核心玩法：玩家受与 NPC **相同的物理约束**，唯一特权是「**暂停游戏、用『属性 × 关系』词条拼规则**」。
规则一经拼出，在一定时间内对整个世界（玩家 / 敌人 / 子弹 / 石头）生效——改变的是**世界的物理法则**，
而不是玩家的操作手感。玩家与同样持有规则之力的敌人争夺规则主导权。

> 🔑 核心判据：**乐趣来自「世界变了」，不是「手不听话了」**。

## 引擎与环境

| 项 | 值 |
| --- | --- |
| Unity | **6000.6.4f1**（2D + URP） |
| 语言 | C# |
| 输入 | 新版 Input System（`Assets/_Project/Settings/InputSystem_Actions.inputactions`） |
| 工程位置 | `Project-Rule/`（在仓库根之下） |
| Unity MCP | 仓库根 `.mcp.json`，HTTP → `http://localhost:8080/mcp` |

## 设计文档（权威来源）

| 文档 | 内容 |
| --- | --- |
| `Doc/规则游戏_策划案_v3.md` | 正式策划案（玩法 / 规则系统 / 敌人 / 关卡 / 叙事 / 存档） |
| `Doc/系统模块架构.md` | 系统与模块级架构（9 个系统、四层） |
| `Doc/接口与事件定义.md` | **v3.0 接口冻结文档**（C# 接口签名、事件清单、依赖速查） |
| `Doc/工程规范与目录结构方案.md` | 目录结构、asmdef、命名规范、四人分工 |
| `Doc/玩家动作设计.md` | 玩家动作集（移动 / 跳跃 / 多段跳 / 冲刺 / 贴墙滑 / 蹬墙跳）的参数、状态机、规则交互 |
| `Doc/美术与动画接入方案.md` | **美术资源目录与命名、精灵导入设置（PPU 640 / 轴心表）、动画驱动方式** |
| `Doc/规则矩阵.json` | 30 个规则格位（6 属性 × 5 关系）+ 未定稿（`undecided`） |

## 架构：四层 + 单向依赖

依赖方向 **Gameplay → Core → Infrastructure**，下层不认识上层。

| 程序集 | 目录 | 允许依赖 |
| --- | --- | --- |
| `RuleGame.Infrastructure` | `Scripts/Infrastructure/` | **无** |
| `RuleGame.Core` | `Scripts/Rules/` | Infrastructure |
| `RuleGame.Gameplay` | `Scripts/Gameplay/`（Combat / Enemy / Player / Scene） | Core, Infrastructure, Unity.InputSystem |
| `RuleGame.Persistence` | `Scripts/Persistence/` | Core, Infrastructure |
| `RuleGame.UI` | `Scripts/UI/` | 全部 |
| `RuleGame.Presentation` | `Scripts/Presentation/` | 全部 |
| `RuleGame.Editor` | `Scripts/Editor/` | 全部（仅编辑器） |
| `RuleGame.Development` | `Scripts/Development/` | demo / 白盒代码，与生产代码隔离 |

**铁律**：规则系统**不认识**「玩家 / 敌人」等具体实体；基础设施零依赖；战斗系统住在规则系统**下游，只读不写**。

## 代码规范（详见 `Doc/工程规范与目录结构方案.md`）

- 缩进 4 空格、**不用 Tab**；大括号 Allman（另起一行）
- 一个文件一个主要类型，**文件名 = 类型名**
- 命名空间 `RuleGame.<层>.<模块>`，如 `RuleGame.Core.Rules`
- 类 / 方法 PascalCase；私有字段 `_camelCase`；常量 PascalCase；事件 `On` 开头
- 资源命名用**后缀不用前缀**：`Hero_Run_01.png`、`GravityRevert_Rule.asset`、`Level_01.unity`
- **不用空格、不用中文文件名**；不用 `New` / `Copy` / `Final`
- 每个新文件都要配 `.meta`（Unity 靠 GUID 引用资源，漏了会重新生成 GUID 导致引用丢失）

## 不能踩的实现约束

- 2D 用 `Physics2D.*`，**不是** `Physics.*`（两套独立系统）
- 物理代码写在 `FixedUpdate()`，不是 `Update()`
- Unity 6 的速度 API 是 `Rigidbody2D.linearVelocity`（旧 `velocity` 已弃用）
- **规则系统不做特例判断**——免疫写在实体自己的物理属性里（约束 C4）
- **绝不序列化** Transform / Rigidbody / 物理状态（约束 C5）；只持久化「已解锁词条 + 激活历史」这类小数据
- **单一规则表**，后写的规则覆盖先写的
- **命中判定只认受击箱**（层 10 `Hurtbox`，Trigger）：查询一律 `useTriggers = true` + `Hurtbox.Mask`，
  准入走 `CombatSystem.CanHit`。新写攻击 / 陷阱时**不要**再自己配 LayerMask，也别直接查身体碰撞体
- 暂停**不用** `Time.timeScale = 0`（会瞬移穿碰撞体）

## 四人分工

| | 负责 | 程序集 |
| --- | --- | --- |
| **P1** | 基础设施 + 存档（时间管理器、快照系统、关卡流程、对象注册表、存档读写） | Infrastructure · Persistence |
| **P2** | 规则系统（规则维护表、属性管理器、属性规则类基类 + 6 个属性规则类） | Core |
| **P3** | 玩家 + 战斗（移动、状态含能量、规则释放入口、伤害结算、命中判定） | Gameplay |
| **P4** | 敌人 + 场景叙事 + UI | Gameplay · UI |

## 当前进度（截至 2026-10-20）

- ✅ 规则系统核心（`RuleSystem` / `RuleTable` / `AttributeManager`），重力属性端到端跑通
- ✅ 规则接口层下沉到 `Infrastructure/Shared/Rules/`（v3.0 冻结）
- ✅ P3：`IEntityStatus` / `EntityStatus`、`RuleReleaseService`、`DamageRule`、`DamageInfo` / `IDamageable`、`CombatSystem`、`PlayerController`、`EntityDamageable` / `IKnockbackReceiver`
- ✅ **伤害链路分两块**：`CombatSystem` 只做**命中判定 + 事件宿主**（`PerformAttack(...)` / `ApplyDamage(...)`），
  伤害结算独立成 **`DamageProcessor`** —— **全游戏唯一的伤害出口**，读 `IAttributeManager` 不读 `IRuleSystem`。
  含伤害缩放 / 反转 / 失效、因果（作用对象转换 / 随机倍率 / 随机对象 / 扩散），以及**伤害挂账**的逐实体净额账本。
  → **要打伤害一律传「基础伤害」经 `CombatSystem`**，不要自己先换算（会绕过伤害规则）；
  要另开伤害出口先看 `Doc/接口与事件定义.md` §4.2
- ✅ **玩家动作集**（详见 `Doc/玩家动作设计.md`）：移动（加速度 + 按地面材质减速）、跳跃
  （coyote / buffer / 可变高度）、**多段跳**（`_maxAirJumps`）、**冲刺**（`_dashSpeed` / `_dashDuration` / `_maxDashes`）、
  **贴墙下滑**（`_wallSlideSpeed`）、**蹬墙跳**（`_wallJumpPushSpeed` / `_wallJumpUpSpeed`）。
  每个能力都是**序列化字段 = 实体自己的能力**，不是玩家专属特权（敌人要就有）
- ✅ **地图组件：平移平台**（`Gameplay/Scene/MovingPlatform.cs`）—— 滑板车 / 升降台 / 巡逻踏板都是它。
  参数：起点 / 终点（**世界坐标**）、速度、往返或单程、端点停留、起始进度。白盒用「方块 + 汉字」占位；
  菜单 `Tools/规则游戏/地图组件/① 滑板车（平移平台）` 一键生成，Inspector 上有「起点 / 终点 = 当前位置」按钮。
  **站在上面的人会被带走**：平台实现 `IMovingSurface`（`Gameplay/Common`，唯一的耦合点，
  玩家不认识具体场景组件），玩家侧 `ContactSensor.CarrierVelocity` → `MotionMover` ⑧′ 并进位移。
  实测水平 / 竖直都**零漂移**。两条实现约束（给速度不给位移、绝不写回 `_velocity`）见 `Doc/玩家动作设计.md` §7
- ✅ **测试地形**：`Scenes/Test_RuleSystem.unity` 的 `Test_Geometry` 里是**两面墙的竖井**
  （左壁 x≈26、右壁 x≈31，间距 4.4，y −2~14），加井顶落脚点、一块平台、三级台阶。
  布局表与走法见 `Doc/玩家动作设计.md` 开头
- ⚠️ **冲刺只管水平**：水平被冲刺接管，**竖直方向不积分重力、速度原样保留**
  （空中冲刺是一条平直横线，不是斜向下）；且不受「摩擦 × 反转」影响
- ⚠️ **贴墙下滑 ≠ 吸附**：人照样在往下掉，只是被压到 `_wallSlideSpeed`（6，约正常下落的 1/5）
- ⚠️ **蹬墙跳是两档推力**：朝墙**外**按 → `_wallJumpPushSpeed`（9，跳出去、够跨到对面墙）；
  朝墙按 / 不按 → `_wallClimbPushSpeed`（4，**只弹一点点**）。
  **爬升档的推力必须小** —— 否则按着朝墙的方向也会被推飞，一面墙就爬不上去。
  **想让一次跳得更远，优先调 `_wallJumpUpSpeed`（15）而不是加水平推力** ——
  蹬得高 → 滞空久 → 水平距离白拿，弧线还更好看。
  **起跳优先级：地面/土狼跳 > 蹬墙跳 > 空中跳**（顺序错了会导致「贴墙时二段跳抢先」）。
  规则交互表与处理顺序表见 `Doc/玩家动作设计.md` §2.2 / §3.5 / §6 —— **改玩家动作前先读那份文档**
- ✅ P4：白盒 HUD、规则拼装 demo、五种重力接入（`Scenes/Level_01.unity`）
- ✅ **P3 的组件已接进 `Scenes/Test_RuleSystem.unity`**（`Systems` / `Ground`(层8) / `Player` / `Dummy`(层9)），
  Console 0 错误，可直接双击打开跑冒烟测试。`Level_01.unity` 目前仍只有 P4 的 demo 控制器。
  想验证玩法请用测试场景，接线前请注意下面几条
- ✅ `RuleCatalog.asset` 已接 **30 条**规则 —— **规则矩阵 6 属性 × 5 关系全部可达**。
  6 个属性规则类都已实现（重力 / 时间 / 摩擦 / 质量 / 伤害 / 因果）
- ✅ **受击箱（Hurtbox）已落地**，**所有受攻击判定只看受击箱**：一个独立子物体 + Trigger 碰撞体，
  住在 **层 10 `Hurtbox`**（2D 碰撞矩阵里与所有层都不碰），尺寸约定为身体碰撞体的 **0.75 倍**。
  查询一律 `useTriggers = true` + `Hurtbox.Mask`，**调用方不再自带 mask**；准入规则
  （自己不打自己 / 只打敌对阵营 / 无阵营放行）集中在 `CombatSystem.CanHit`。接线工具：
  `Tools/规则游戏/受击箱/① 场景 ② 预制体`。细节与改造过的 5 个判定入口见 `Doc/接口与事件定义.md` §4.2
- ⚠️ **阵营只有两个值**（`Faction.Player` / `Faction.Enemy`，后者是默认值）：
  玩家独一个阵营、没有友方，其余可攻击实体全是敌人。挂在 `EntityDamageable` 上。
  **场景里需要把玩家对象的 `_faction` 手动设成 `Player`**，其余不用动
- ⚠️ `PlayerController` 按 D1 用 **Kinematic 刚体**：位移走 `Rigidbody2D.Slide` 解算接触，
  重力自己读 `Physics2D.gravity` 积分。`MovePosition` 自身不做碰撞，**不要绕开 Slide 直接移动**
- ⚠️ **暂停不能用 `Time.timeScale = 0`**（策划案 §3.1）。冷却等按帧推进的状态改由
  `EntityStatus.SetPaused` 显式冻结 —— 架构文档 D12 的旧说法「暂停自动冻结」已作废
- ⚠️ 「关系」属性已**彻底删除**：`AttributeId` 6 个成员、`RuleTable.SlotCount = 6`。
  架构 D5 已被 D17 取代，规则矩阵已同步为 **6 × 5 = 30 格**（见下方「已定设计决策」）
- ✅ **可推铁箱**（`Gameplay/Scene/PushableBody.cs` + `Gameplay/Common/IPushable.cs`；
  菜单 `Tools/规则游戏/地图组件/⑧ 铁箱（可推重物）`）—— 关卡里的**重物障碍**。
  **Dynamic 刚体**：重力规则直接生效（`Physics2D.gravity` 一翻，箱子就往天上掉）；
  质量由已有的 `RuleAffectedBody` 按质量规则写进 `Rigidbody2D.mass`，本组件一行都不管。
  玩家侧新增第 **⑪ 推动**（`Gameplay/Player/PushSensor.cs`）：按住方向顶住箱子时把**推力**交给它，
  **加速度 = 力 ÷ 质量**（与 `WindSensor` 的「世界报力、实体自己除以质量」同一条分工）。
  于是三种规则效果**全自动成立**，不需要任何特判：
  实测（play 模式自己步进物理，μ=0.5、推力 60 N）——
  8 kg → a=3.2 m/s²（缓慢推得动）· 2.4 kg（×削弱）→ 22.7 · 0.4 kg（×失效）→ 182（一推就飞）·
  **24 kg（×增强）→ 0（纹丝不动，就是一堵墙）**。可推质量上限 = `F/(μ·g)` ≈ **12.2 kg**
- ⚠️ **铁箱必须放 `Ground` 层**（层 8）：玩家的 `Slide` 与接地探测只认这个层，放错层就是一块穿得过去的空气。
  代价是玩家也能在箱子侧面**贴墙下滑 / 蹬墙跳**（它就是一块会动的墙）—— 这是预期行为
- ⚠️ **推动交的是「力」不是「速度」**：物体那边除以自己的质量才是加速度。
  改成直接设速度的话，「质量 × 增强 → 推不动」会立刻失效（质量规则白配）；
  改 `_pushStrength` 而不是改箱子的速度，是这套设计能被规则撬动的前提
- ⚠️ 推动方向取 `_mover.Facing`（**换算后**的朝向）而不是按键方向：
  「摩擦 × 反转」生效时按右是往左走，用按键方向会**推不动人正顶着的那个箱子**
- ✅ **巨石断剑机关**（`Gameplay/Scene/GravityBoulder.cs` + `BoulderFragment.cs` + `SwordInStone.cs`）——
  **一个预制体**：`Prefabs/BoulderSwordMechanism.prefab`（菜单 `Tools/规则游戏/地图组件/⑫ 巨石断剑（生成预制体）`），
  关卡里直接拖。外观是**方形石料**（`Art/Sprites/Stone/StoneSquare.png`，320px 程序生成：倒角 + 描边 + 裂纹），
  碎块同图 —— **不等美术**。
  **按钮不打包在里面，要两个**：地上一个 `CityButton`（勾 `PressByPlayerProximity`，玩家踩到 → 解锁）、
  天花板一个（+6，巨石顶到 → 震裂）；`CitySwitch` 因此新增实现 `IContactSwitch` 与 `PressByPlayerProximity`。
  ⚠️ **巨石开局是锁死的 Static**（`_startAnchored`）：没解锁之前放多少次「重力 × 反转」都**纹丝不动**、
  也不会提前震碎 —— 解锁配法与 Gate 一样：**把巨石拖到地上那个 CityButton 的 `FollowMotor` 上**
  （`CitySwitch.FollowMotor` 因此从 `CityPathMotor` 放宽到 `MonoBehaviour` + `ISwitchTarget` 接口）；没拖就永远不动。
  玩法：踩解锁按钮 → 重力反转举起 40 kg 巨石 → 穿过天花板 CityButton（按下 + **震裂**）→ 顶在天花板 →
  重力恢复落回地面**碎开**、撒 8 块碎块并**露出剑** → 「摩擦 × 反转」之后走近拔剑。
  细节见 `Doc/巨石断剑机关.md`；edit 判据 **34/34**、Play 冒烟 **29/29**（`Scenes/Test_BoulderSword.unity`
  里的机关就是**预制体实例**）
- ⚠️ **巨石碎不碎只看「落地」**：接触点必须在巨石中心**下方**（`IsSupportedFromBelow`）+ 速度取
  自己在物理步进前那一帧的向下速度 —— **不能读 `Collision2D.relativeVelocity`**，它的符号约定一记反，
  巨石顶到天花板那一刻就会被当成落地、**在半空中就碎**（2026-10-18 修）
- ✅ **门锁：石头碎掉之后才开门**（2026-10-20）—— `Gameplay/Scene/BoulderGateLock.cs`：挂在**门**上、把巨石拖进 `_boulder`，
  只听 `GravityBoulder.OnShattered`（**落地碎开那一下**），不认天花板按钮 —— 那个按钮是巨石**上升途中**顶到的，比碎开早一整段。
  锁着时 `LateUpdate` 把门的目标端扳回关位（别的开关连着也开不了），解锁后默认立刻 `SetOn(true)` 开门；
  解锁状态写 `WorldStateStore`（`#unlocked`）：**巨石不进存档，门要进** —— 否则拿过剑的玩家读档回来会被一扇打不开的门堵住。
  `Level_Start_01` 的 `CityGate (4)` 已配好；天花板 `CityButton (5)` 的 `FollowMotor` 已清空（只负责震裂）。见 `Doc/巨石断剑机关.md` §11
- ✅ **纯重力石头**（`Gameplay/Scene/StoneBody.cs`；菜单 `Tools/规则游戏/地图组件/⑬ 巨石（纯重力石头，撞开关）`）——
  `Prefabs/StoneBoulder.prefab`：Dynamic 18 kg、吃重力、碰到 `CityButton` 就按下去（`IContactSwitch`）、
  收玩家推过来的力（`IPushable`）。**没有碎 / 剑 / 锁** —— 那些是 ⑫ 巨石断剑的事。
  外观与 ⑫ 同一张**方形**石头贴图（`Art/Sprites/Stone/StoneSquare.png`，320px 程序生成：倒角 + 描边 + 裂纹）。
  ⚠️ **「推不动 → 摩擦一减小就推得动」是物理自己算出来的，代码里没有门槛**：玩家推力 60 N，
  静摩擦 = **μ_eff**·m·g —— μ_eff 是**两个接触面组合后**的值，不是石头自己那份
  （地面是没材质的 Tilemap → 0.4）。按最坏情况（取平均）算 18 kg：正常 μ_eff≈0.45 → 79 N **推不动**；
  「摩擦 × 削弱」μ_eff≈0.275 → 49 N **推得动**（净 11 N ≈ 0.6 m/s²）；「失效」→ 一推就滑。
  18 kg 是反推的边界：> 13.6 kg 保证正常推不动，< 22.2 kg 保证削弱推得动。
  `StoneBody.ReceivePush` 只交力（与 `PushableBody` 同一条路），
  所以「质量 × 削弱」（18 → 5.4 kg）也会顺带让它推得动 —— 涌现，不是特判。
  ⚠️ **石头必须用 `Data/Physics/StoneMaterial2D`（摩擦 Combine = `Minimum`）**，不能沿用木箱那块
  （Combine = `Mean`）：与地面（没材质，默认 0.4）取平均后，0.15 会变成 0.275 → 静摩擦 81 N，
  「摩擦 × 削弱」照样推不动（2026-10-18 实测踩到的坑：当时只有「质量 × 削弱」能动）。
  `Minimum` = 谁的摩擦小听谁的，规则改多少就生效多少。排查把 `_logPushes` 勾上。
- ⚠️ **碎块没有碰撞体、也没有刚体**（`BoulderFragment` 自己积分 + 向下射线找地面）：碎块堆在剑周围，
  实心的会把玩家**卡住 / 顶起来**（实测过），顺带还会挤进取剑的探测圈。代价是瓦砾不挡路、也不能站。
  生成碎块前要先关掉石头自己的碰撞体（射线的第一条命中会是自己）。
- ⚠️ **剑取出来 = 玩家才有攻击能力**（策划 2026-10-18，**接线未做**）：
  **解锁状态存在玩家自己的属性里**（同「能力 = 实体自己的序列化字段」），不新开系统、不塞 `IRuleUnlocks`；
  机关只发 `SwordInStone.OnExtracted` / `_onExtracted`，接线点（`PlayerController` 输入那一步）见
  `Doc/巨石断剑机关.md` §9
- ✅ **落点 = 出生点 + 存档点（2026-10-18 合并，主程定）**：`PlayerSpawnPoint` 是**唯一**落点组件。
  开局由 `WorldStreamer` 挑一个点 `Arrive()`（落人 + 记成复活点，不播表现、不写盘）。
  **2026-10-20 改成攻击触发**：与按钮 / 宝箱同一条 `IContactSwitch.ImpactByContact` 路径 ——
  攻击盒 / 飞刀打中才播秋千过场并存档，**走近不再自动记录**；**已经存过的点再被打中只重播过场**，
  不改当前复活点、不重复写盘（「存过」记在 `WorldStateStore` 的 `saved` 槽位，随档走）。
  坐上去后 7 帧坐姿**一直循环荡秋千**，直到玩家**再次按下玩法输入**才黑场下来；
  ESC / 手柄 Start 不算「继续」（归暂停菜单）。入口 `id = start` 不吃攻击存档，避免把档洗回开头。
  死亡后 `PlayerController` 等 `_respawnDelay`（1.2 s）**并且等死亡动画播完**才自己 `Respawn()` ——
  等待是表现层挂的（`PlayerAnimationDriver` 在倒下时 `HoldRespawn()`、`Die` 播完 `ReleaseRespawnHold()`，
  `Hero_Die` 是 2.67 s，只靠 1.2 s 会在人还没倒下时就把人搬走）。复活 → `EntityDamageable.Revive()`
  （清死亡标记 + 回满血 + 至少 1 秒无敌），**同时把动画 `Play("Idle")` 拉回初始态** ——
  `Die` 是终点状态、没有出边，不显式重置会一直停在尸体那一帧。复活位置**存在玩家自己的内存字段**
  （`RespawnId` / `RespawnPosition`），**不进世界快照** —— 所以跨分区卸载不再失效。
  旧 `CityCheckpoint`（靠全量内存快照记位置）已删除，白盒里的点换成 `PlayerSpawnPoint`。
  组件广播 `SaveRequested(id)`，落盘由 `PersistenceDriver` 订阅 `CheckpointBus` 完成
  （`SaveSystem.SaveCheckpoint`）；存档只存 `RespawnId` / `RespawnScene`，位置由场景里的点提供（约束 C5）
- ⚠️ **`WorldStreamer` 会「认领」已经在场的分区**（编辑器多场景开着按 Play 的情况）：
  认领进 `_loaded` 之后 `LoadedCount` 才有值、开局落人才会触发 —— 以前这条路径直接跳过，
  出生点因此**永远不生效**（2026-10-18 修）。开局落人已从 `LoadRoutine` 挪到 `Refresh()`
- ⚠️ **`Boot` 不是世界分区**（2026-10-19 修）：`PartitionCatalog` 里曾残留一条 Boot（旧矩形，Boot 当年装过积木城），
  流式加载会在玩家走出范围时**卸载 Boot** —— 主菜单 / `TrainTravelService` / 黑场 / 线路面板一起消失，
  车站砍不开也没法传送；走回来再**附加加载第二份 Boot**，新主菜单 `_started=false` 在玩到一半弹出来把游戏暂停
  （存档过场也因此冻住）。现已删掉那条数据；代码侧 `WorldStreamer` 认启动场景（`GameRoot` 在
  `DontDestroyOnLoad` 之前写入路径）**永不加载 / 卸载**，`世界分区/②` 也拒绝登记 Boot
- ✅ **回弹块**（`Gameplay/Scene/SpringBackBody.cs`；菜单 `Tools/规则游戏/地图组件/⑭ 回弹块（轨道内受击击退 + 缓慢回正）`）——
  生成 `Prefabs/SpringBackBody.prefab`：被攻击打得**沿自己的轨道**退开（轻击 ≈0.9 格 / 重击顶到轨道尽头），
  停一下再**慢慢回正**。**轨道 = 过锚点的一条直线**（两个相对锚点的偏移，默认水平 ±1.5 格），
  位置被夹在两端之间 —— 打不飞、推不歪；垂直分量被吃掉（竖直轨道靠 `_knockbackLift` 推）。
  **摩擦规则直接当分母**：回正速度 = `_returnSpeed` ÷ 摩擦倍率（×增强 → 慢到 1/3，×削弱 / ×失效 → 快 3 倍）。
  选 **Kinematic + 自己积分**，不是 Dynamic + 引擎摩擦：这类块悬在竖井里没有接触面；而且静摩擦 `μmg`
  随 ×3 涨过回正力时，块会**卡死在偏移位置**。**只吃击退、不掉血**（`IDamageable` 忽略 `Amount`），
  自带受击箱、能承载（`IMovingSurface`）、无白盒文字标签。**站在块上打别人不会把脚下的块弹开**
  （攻击盒下沿会罩到脚下那块，Box2D 重叠含边界；`ApplyDamage` 先用脚底探测 `IsStandingOnMe` 把这种命中整下忽略）。
  **挡路的玩家会被推着走**（2026-10-19 修）：块移动前用 `SafeFraction` 夹短位移（玩家顶着墙就停），
  再把这段位移交给 `PlayerController.CarryOnPlatform` —— 与电梯 `CityPathMotor` 同一套承载；
  悬空 / 上升中的玩家推不动，块停住等他们让开，**不会把人包进碰撞体里**（之前正是「包进去」导致玩家卡死）。
  块自身仍会穿墙、敌人没有承载入口（块对敌人仍是穿过）。
  **根物体可缩放**（关卡里的 8×2 宽板）——
  受击箱随缩放变大（所以它**和身体同尺寸**，不按 0.75 缩：宽板上 0.75 的边距会超出玩家攻击距离，出现「只有一侧打得到」），
  轨道白盒按实际缩放换算。编辑期自检 `规则游戏/验证/回弹块（预制体 + 纯函数）`（32 条）。
  细节见 `Doc/受击回正块.md`
- ✅ **纸飞机飞刀**（玩家投掷原型，2026-10-18）—— `Gameplay/Player/ThrownKnife.cs` + `Prefabs/PaperPlaneKnife.prefab`
  （精灵 `Art/Sprites/Player/PaperPlane_01.png`：320px / PPU 640 = 0.5 m，程序生成，生成脚本 `Tools/Art/gen_paperplane.py`）。
  按 **F / 手柄 RB** 从胸前投出：冷却 0.35 s、初速 16 m/s。**伤害按策划 5.2 公式现算**（不再有固定伤害字段）：
  `D_raw = 5 × 质量 × 速度档位`，档位 **8 / 12 / 16 / 20**（≤8→0、≤12→1、≤16→2、≤20→3，再快仍算 3，**右闭**）。
  这组数是从设计稿的 4 / 6 / 8 / 10 **整体放大一倍**得来的 —— 原表是按 8 m/s 的投掷物配的，而飞刀出手就是 16。
  档位表是 Inspector 上的 `Speed Thresholds` 数组（清空会退回默认），速度取**撞击瞬间**的速度大小
  （含重力带来的竖直分量，所以抛得远、落得快的伤害更高）。
  **全局伤害倍率不在飞刀里乘** —— 那是 `DamageProcessor` 的活（`BaseAmount × DamageScale`），
  飞刀只交 `D_raw` 这个「基础值」。**飞行 = Kinematic + 自己扫掠**
  （`CircleCast`，`useTriggers` 开）：吃 `Physics2D.gravity`（**重力规则自动生效，零特判**）
  **加上风场**（`IWindField`，`OverlapPoint` 查 `Wind` 层 —— 它与玩家 `WindSensor` 是同一套算法：
  风报的是**力**，除以 `Rigidbody2D.mass` 才是加速度，阻力按 `Clamp01(drag·dt)` 扣），
  预制品上因此挂了 `RuleAffectedBody`（**关掉摩擦**，飞刀没有碰撞体）—— 「质量 × 削弱」会让它被吹得更飘，
  **只有 `Ground` 层的实心碰撞体挡得住它**（撞上就**停在原地**，**不消失**）；
  **Trigger 区（风场等）穿过去**（是按钮就顺手按下 —— 与石头 / 铁箱同一条路）；
  **场景道具 / 敌人身体 / 受击箱 → 炸成一把纸屑再消失**（`Gameplay/Player/PaperPlaneBurst.cs`：
  7 片纸屑、运行时生成**不需要预制体**、没有碰撞体也不进快照，0.55 s 淡出后自毁）；
  什么都没撞到就等**寿命到了（默认 6 s）**才消失。
  ⚠️ 2026-10-18 两轮反馈改的：以前掩码 Everything + `useTriggers`，飞进风场这种**没有实体**的
  Trigger 区就没了（误判）；改完又觉得道具/敌人「穿过去」不对，于是补了碎裂。
  承受方（`EntityDamageable`）按同一条公式收尾：**RoundHalfUp → 减 `_flatDefense`（新增，默认 0）→
  夹到当前生命**，最后才吃百分比 `_resistance`。⚠️ 这一步是**全局**的（近战 / 陷阱 / 飞刀都走它）——
  默认 0 时只有「伤害变成整数」这一条行为变化。
  伤害走 `CombatSystem.ApplyDamage` 传**基础值**；敌人**身体碰撞体在受击箱外面**，扫掠常先碰到它，
  所以那条分支按父链取 `IDamageable` 再走 `CanHit`（否则撞在敌人身上一个伤害都不出）；
  **同一把刀对同一目标只结算一次**（两条分支共用一个 `HashSet` 去重）。机头永远朝速度方向。
  ⚠️ 身上**没有碰撞体**（判定全靠扫掠，所以不挡路、不会被推）、**未接世界快照**（回溯不会清掉在飞的刀）、
  弹药数还没接（`IEntityStatus.ThrowableCount` 仍是空字段）。
  ⚠️ **`Launch` 里必须 `_body.position = transform.position`**：工程 `autoSyncTransforms` 关着，
  只写 Transform 的话刚体停在预制体原点，第一次扫掠会**从世界原点出发**（实测隔着半个关卡撞到按钮）。
  自检 `规则游戏/验证/纸飞机飞刀（隔离场景 Play 冒烟）` **20 项断言**（5.2 速度档位 / 原始伤害 / 承受方四舍五入与平防、
  撞地面**停住不消失**、撞道具 / 敌人**炸开消失**且真的炸出纸屑、穿风场不消失、
  **风场里被吹起来**（0.9 s 从 y=8 吹到 14）、按下开关后继续飞、投掷者自身不误判、只有寿命到才消失），
  自己开空场景跑、跑完把原场景开回来；也有批处理入口 `PaperPlaneKnifeValidation.RunBatch`。
  **正式入口已接**（2026-10-19）：`Prefabs/LevelRuntime.prefab` 的 Player 上 `_knifePrefab` 指向它 ——
  `GameRoot.prefab` / `Boot.unity` 都是它的嵌套实例，**自动继承**（场景里不用再单独拖）。
  `Scenes/Test_RuleSystem.unity` 另外接了一个靶子按钮 `PaperPlaneButton`（x=17，按下变绿）。
  ⚠️ `Prefabs/Enemies/EnemyLabPlayer.prefab` 没接（敌人实验室的测试角色，不需要投掷）。
- ✅ **攻击盒 / 纸飞机也能扳开关**（2026-10-19，方案 B）—— `IContactSwitch` 新增 **`ImpactByContact()`**（**撞击型**：打一下就翻转），
  与 `PressByContact()` / `ReleaseByContact()` 的**压住型**（石头 / 敌人 / 巨石压在压板上）并存，互不替换。
  **玩家**侧在 `PlayerController.PerformAttackHit` 里多查一次 `OverlapBox`（`useTriggers = true`、不配 mask、
  按 `IContactSwitch` 认对象）；**纸飞机**侧 `ThrownKnife.PressSwitch` 改调同一个入口；
  两边各自按引用去重 —— 一次挥砍 / 一发投掷对同一个开关只翻一次
  （纸飞机 16 m/s 一帧走 0.32 m，摇杆触发区 0.65 m 宽，不去重会连翻两下 = 看起来没反应）。
  ⚠️ 这条**不进伤害管线**：开关不是 `IDamageable`，所以「伤害 × 失效」不会把机关一起关掉
  （`DamageProcessor` 在 `DamageDisabled` 时直接 `return 0`，连命中都不算）。
  `Toggle = 1` 翻转、`Toggle = 0` 仍按下即锁死；翻转逻辑与按 E 的 `Interact` 同一行。
  验：`规则游戏/验证/纸飞机飞刀（隔离场景 Play 冒烟）` **21 项全过**（含新增「同一发只撞一次」）；
  顺手修了该验证里 `WindZone` 的旧字段名（`_forceAtTop` 已不存在，改 `_forceGradient`），在那之前它一跑就 NRE。
- ✅ **NPC 对话改走攻击命中**（2026-10-20）—— `NpcActor` 实现 `IContactSwitch`，攻击盒命中即开播，
  与按钮 / 宝箱同一条 `ImpactByContact` 路径，不进伤害管线；命中本身即触发条件，不再检查交互半径。
  `PressByContact` / `ReleaseByContact` 对 NPC 是空实现（石头压过来不对话）。
  `DialogueHost.Active` 提供共享 Host 兜底，NPC Prefab 的 `_host` 可以留空；E 键近距离开播保留为兼容入口。
  ⚠️ 飞刀只对 Trigger 调 `IContactSwitch`，NPC 身体是实心碰撞体，要飞刀触发得加 Trigger 子物体。
  镇长资格同批改为直读 `PlayerProgress.HasAllUnlocks`（6 属性 × 5 关系 + 四项能力全解锁）。
- ✅ **镇长资格门（通往魔王城，2026-10-21）** —— `Gameplay/Npcs/MayorQualificationGate.cs`：
  镇长读完 `Talk2` → `PassKey` 后写 `WorldStateStore` 键 `npc.mayor.qualified` 并广播
  `NpcActor.MayorQualified`；门订阅静态事件，**跨分区不依赖场景引用**。
  `Open()` 再查一次 `PlayerProgress.HasAllUnlocks`，白盒把 `_abilityReady` 手动打开也不会提前开门；
  读档 / 分区重载按同一个键恢复开门状态。配法：挂在头顶门上，`_gate` 留空自动取 `CityPathMotor`，
  只靠碰撞体挡路时把碰撞体拖进 `_blocker`。现成实例已放在 `Level_Center` 的 `(-3.65, 31.5)`（预制体 `Prefabs/MayorGate.prefab`，默认宽 4m，`EndOffset (0,4)` 向上滑开）。
- ✅ **楼层电梯 + 随车拉杆**（2026-10-19，独立预制体）—— `Prefabs/BrickCity/CityFloorLift.prefab`，
  生成菜单 `Tools/规则游戏/积木城/⑮ 楼层电梯（拉杆随车走）`。**旧的两站电梯一行没动**：
  `CityPathMotor` / `CityLift` / `CityLever` 原样，新组件是 `CityMultiStopLift` + `CityFloorLever`。
  层级：容器 → `Car`（Ground 层）+ `Lever`（Default 层），**拉杆是兄弟节点不是子物体** ——
  Kinematic 子刚体挂在父级 `MovePosition` 下会晚一个物理步，所以由 `_carriedBodies` 显式给同一位移。
  站点 = 相对摆放位置的偏移数组（2 项 2 站 / 3 项 3 站）；档位 = **打过来的位置**落在横向第几段
  （近战传玩家位置、纸飞机传自己撞击点、E 键也按玩家位置），2 档时正中算右半。
  `IContactSwitch.ImpactByContact` 因此多了 `sourcePoint` 参数（两态开关忽略它）。
  **美术已接交付图**：车厢 = `CityLift_Plane_01/02`（`SpriteFlipbook` 14fps），拉杆 = `CityLever_01..05`
  （`CityFloorLeverSpriteView` 22fps 按档换帧，`_handle` 留空）。对齐按**机舱顶**（精灵局部 +0.75）而不是包围盒顶 ——
  尾翼比机舱高 0.15，按包围盒对齐人会悬空。素材在 `Art/Sprites/Components`（`CityArtSetup` 导入，不另存副本）。
  细节见 `Doc/楼层电梯与多档拉杆.md`。

- ✅ **刺击回落（2026-10-20）** —— `Gameplay/Player/SafeGroundMemory.cs` + `Gameplay/Common/IHazardResponder.cs`。
  玩家**每个接地帧**记一个「无刺的瓦片地面」点（离地即冻结，不需要 Jump/Fall 状态机）；被刺格命中后
  `HazardField` 在伤害结算之后回调，复验 Up / 瓦片还在 / 无刺，通过就取消冲刺并 `TeleportTo` 回去，
  顺带清掉这一下的击退。**白名单**：只认静态 `TilemapCollider2D` —— 移动平台 / 电梯 / 铁箱 / 单向平台
  默认全部不算；是白名单不是黑名单，以后新增平台不用改代码。致死的那一下不回落（交给死亡 / 复活流程）；
  复验失败不传送。参数在 `PlayerController` 的「刺击回落」分组（默认开、0.5 s 冷却、0.18 m 余量）。
  ⚠️ 安全点**不进快照、不落盘**（C5），和 `PlayerSpawnPoint` 的复活点不是一回事；刺格 0.05 s 扫描一次，
  所以「立刻」最多晚 2~3 个物理帧。验证菜单 `规则游戏/验证/刺击回落（隔离场景 Play 冒烟）`
  （`Editor/Verification/SpikeReturnValidation.cs`），**11/11 全过**（2026-10-20，
  `Project-Rule/Logs/SpikeReturnValidation.txt`）。

- ✅ **ESC 暂停菜单（2026-10-20）** —— `Scripts/UI/Pause/PauseMenuPresenter.cs` + `UI/Pause/PauseMenu.uxml/.uss`，
  挂在 `Prefabs/LevelRuntime.prefab` 的 `Pause Menu`（UIDocument sorting 300，GameRoot / Boot 自动继承）。
  三个键：继续 / 回到上一个存档点 / 回到主菜单；后两个走确认态且**默认焦点在「取消」**。
  **ESC 是逐层返回**：本组件执行序 `-500`，比轮盘 / 火车面板 / 对话（默认 0）先跑，看它们开着就让路 —— 跑在后面会出现
  「关掉轮盘的同一次 ESC 又弹出暂停」。
  暂停仍只走 `TimeManager` + `EntityStatus` + `BlockGameplayInput` 租约（照 `MainMenuPresenter.EnterMenu/ReleasePause` 记旧值再恢复）。
  **回档 = 快照回滚**（2026-10-20 改）：存档点落盘时 `PersistenceDriver` 顺手拍
  `SnapshotSystem.CaptureCheckpointSnapshot()`（**独立的槽位，不与时间恒定的 `Pin` 共用** —— 共用会让
  「时间恒定生效时按 ESC 回档」把定点踩掉）；回档 `TryRestoreCheckpointSnapshot()` 一把写回玩家 / 敌人 / 机关 /
  规则表 / 血量，随后 `PlayerController.TeleportTo(body.position)` 收尾（清移动器旧速度 + `SyncTransforms`）。
  **兜底**：存档点分区已卸载或没有快照（从盘续的档）→ 读盘 `Load/Apply` + `ReloadAll` + `ApplyStartSpawn`。
  两种路径都盖黑场（`IScreenFade` / Boot 的 `SwingCheckpointFadeScreen`），面板与遮罩过场期间收起。
  回主菜单由 `GameRoot.StopForMenu()`（Gameplay 侧停世界）+ `MainMenuPresenter.ShowMenu()`（UI 侧）两步组成。
  ⚠️ 已知缺口：手柄方向键导航未接、无音效、跨分区回档退回读盘（快照只覆盖在场分区）、
  快照拍在「打中存档点那一刻」（秋千过场状态不在快照里）、`LoadContext.ContinueGame` 会被置 true。
  设计文档 `Doc/ESC暂停菜单设计.md`，预览图 `Doc/UI预览/暂停菜单_*.png`（占位色块，等美术换皮）。

- ✅ **宝箱的生命 / 能量奖励 = 直接抬上限，并且落盘（2026-10-20）** —— `CityRewardKind.Health / Energy`
  的 `Amount` 走 `PlayerProgress` → `EntityStatus.RaiseMaxHealth / RaiseMaxEnergy`：
  上限 = 预制体设计初值 + 增量，**当前值同步抬高同一段**（不是回血回蓝）。
  增量记在 `PlayerProgress.MaxHealthBonus / MaxEnergyBonus`，与 `ClaimedSourceIds` 一起进存档
  （新接口 `IPlayerProgressStore` + `SaveData` 三个字段）；**读档在 `ApplyData` 之前回灌**，
  否则档里超出的那截会被旧上限削掉。箱子本身不进存档，靠 `IsClaimed` 回填「已开」。
  ⚠️ **战斗力（`Power`）已删除**：枚举 **0 号退役不重编**，其余编号不动，场景 / 预制体零迁移。
  验证：`能力与词条解锁` **40 项**、`RuleGame/P1/运行存档验证` **62 项**（第 10 节）。

- ✅ **解锁演出 = 黑幕 + 暂停 + 等玩家按键（2026-10-20，主程提）** —— 取代原来的「屏幕上方一条 toast」。
  `Scripts/UI/Launch/AbilityUnlockOverlay.cs`，与 `PlayerVitalsPresenter` **共用同一份 UIDocument**
  （`UI/Launch/PlayerVitals.uxml/.uss` 新增 `abilityScreen`；`LevelRuntime.prefab` 的 `Player Vitals` 上挂组件，
  GameRoot / Boot 自动继承）。解锁事件（能力 / 规则属性 / 规则关系）→ 全屏黑底写上**本次获得的东西**
  （小字「获得新能力」+ 大字名 + 一行说明）→ 玩家按下**新的玩法输入**（`PlayerController.AnyGameplayInputThisFrame`，
  不受输入门禁影响）→ 淡出、解除暂停、交回输入。
  暂停照 ESC 菜单的老规矩记旧值再恢复：`TimeManager.SetPaused` + `EntityStatus.SetPaused` + `BlockGameplayInput`
  租约，**绝不碰 `Time.timeScale`**；淡入淡出与等待都走**非缩放时间**。
  ⚠️ **ESC / 手柄 Start 算「继续」**（黑幕上按 ESC 不该再弹暂停菜单）：`PauseMenuPresenter.AnyOverlayOpen()`
  会认 `IsShowing` 让路 —— 这个判断顺序不能反（本组件执行序比菜单晚，菜单不让路就会「先弹菜单再关黑幕」）。
  ⚠️ **读档不播**：`PlayerProgress.RestoreUnlocks` 期间**不发**解锁事件（那不是「刚获得」），
  否则从盘续档会被一整串需要按键的黑幕堵住。一次性拿到多项时**排队**，一条按一下。
  淡入 0.25 s / 淡出 0.2 s / 淡入后 0.15 s 宽限（挡住触发奖励的那一下按键）。
  验证：菜单 `规则游戏/验证/能力与词条解锁` **42 项**（含黑幕元素与预制体接线两项）；
  版式示意图 `Doc/UI预览/解锁黑幕_预览.png`（按 USS 数值画的黑底占位，不是引擎截图）。

## 已定设计决策（2026-10-12，合并 `mortis-sys/main` 时定）

| # | 决策 | 理由 / 代价 |
| --- | --- | --- |
| 1 | **删掉 `AttributeId.Relation`**，不保留枚举位（6 成员 / `SlotCount = 6`） | 枚举值会序列化进 `RuleDef.asset` 的 `Attribute` 字段，保留占位只是为了兼容旧资产；本次伤害资产随伤害系统重写、且没有需要兼容的存档，所以删除是免费的。**以后再删属性要先查引用，并准备重挂受影响的资产** |
| 2 | 规则类的 `IAttributeManager` 用**构造函数注入**，不用 `Bind()` | 漏注入变成**编译错误**而不是运行时 NRE；代价是每个子类多一行转发构造（用不到属性管理器的 `GravityRule` 也要写） |
| 3 | 暂停 = **两层**：`Physics2D.simulationMode = Script`（停物理步进）+ `RuleSystemDriver.IsPaused`（停规则时钟）。`IsPaused` 只是**过渡期闸门**，等 P1 的 `ITimeManager` 落地后改读它的 delta 并删掉这个布尔量 | `Time.timeScale` 保持 1，`Time.deltaTime` 照常推进，不挡就会照常扣规则时长 / 冷却。单一真相（`ITimeManager`）是终点，重复状态是临时债 |
| 4 | **删掉战斗力奖励**（`CityRewardKind.Power`），**0 号退役、其余编号不重编**（2026-10-20） | 重排编号会让场景 / 预制体里已存的 `Kind` 整数发错奖励（`Health` 变成 `Ability` 之类）。空一个编号是免费的，迁移几十个资产不是。以后新增奖励只在**末尾追加** |

## 提交约定

- Commit 信息用 Conventional Commits + 中文：`feat(gameplay): ...`、`docs: ...`、`chore: ...`
- 提交前确认带上 `.meta`
