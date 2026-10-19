# Unity 项目上下文

<!-- unity-onboarding:generated:start -->

2026-10-17 在新分支 feat/minion-pathfinding 接入 EnemyPlatformNavigation：真实 Collider2D 站立点采样、有界 A*、身体扫掠验证的跳跃与安全落差，Motor 仍是位移权威。地面追击与返回出生点使用路线，飞鸟水平巡飞不改。普通默认索敌6米、出生点二维追击半径8米、失去视线后搜索最后位置1.5秒；脱战返回，旧积木/蝙蝠召唤物也遵守范围。中文配置包含开关、1.6米跳高、2.5米落差、0.5米采样和0.65秒重算；暂停/受击/快照接入。每次搜索最多192节点/2400查询，缓存不参与快照，半空运动参加快照。修复继承的 a79c487 合并版本中的敌人端旧战斗接口，攻击统一 Hurtbox，PlayerMask仅保留身体移动用途。Unity6000.6.4f1隔离PlayMode：平台导航16项、索敌脱战9项、受击箱兼容5项通过，合计30项0失败。新增 Test_Minion_Pathfinding 白盒和独立默认范围配置，由EditorAPI生成，日志见 %TEMP%/rule-minion-{navigation,ranges}.log 与 rule-enemy-hurtbox-compat.log。未执行原工程手动白盒、正式地图难度、敌群性能或完整Player构建；移动/单向平台的专用通行和飞行避障未实现。保留同期字体与ProjectSettings修改。
2026-10-17 受伤反馈调优与 PR 预览：普通闪白总时长改为 0.30 秒，保持白色约 0.165 秒后淡出；破韧/致命延长倍率保留。完整副本定向验证 65 项通过，含实际 0.15 秒仍保持白色的 URP 像素断言。`Doc/特效预览/EnemyHitFeedback.gif` 与 `EnemyHitFeedback_Comparison.png` 为固定姿态的真实渲染媒体；`Tools/EnemyAssets/create_hit_preview.py` 从验证截图重新导出，未修改游戏摄像机。用户确认仅核对 p2-p3-merge 基线即可，已 fetch 并确认最新 `2fe1dc1` 是当前功能分支的祖先；不继续扩大摄像机修改范围。

2026-10-17 敌人受伤视觉接入（基线 `a79c487`，分支 `feat/enemy-hit-feedback`）：现有 EnemySpriteAnimator / FieldMinionView 自动登记 EnemyHitFeedback，15 种真实 Prefab 均已验证；原材质和动画不替换，独立覆盖层提供暖白剪影/轮廓余光，四个有界槽提供定向火花和半环，区分普通/破韧/致命及青色弱点。反馈只在实际扣血后发出，携带实际伤害与结果；暂停冻结，回溯/停用清除。Resources/EnemyHitFeedback 两份材质保留 Player shader 引用，无场景/Prefab 重写。先修复基线已有的 PlayerMask 删除残留引用及 AttackArea 旧签名，恢复完整工程编译；旧小怪回归夹具改按 Time.fixedDeltaTime 推进，避免 50 Hz 假设与工程 60 Hz 不一致。原工程 Unity 6000.6.4f1 编译通过；完整副本 `/tmp/rule-enemy-hit-20261017` 的受伤反馈 Play Mode/URP 像素验证 64 项、破韧回归 53 项均通过。日志 `/tmp/rule-enemy-hit-{baseline,final-compile,validation,minion-regression}.log`，截图/结果在副本 `Logs/EnemyHitFeedback/`；仅见既有 Unity Search 索引异常，无新增运行时/Shader 错误。未跑 Player 构建或整关操作验收；保留字体与项目设置的同期用户修改。接入和调参见 `Doc/敌人系统接入.md` 的统一受伤视觉反馈小节。

2026-10-17 小怪受击补查：旧 MinionBrain 的召唤物改为累计破韧；新增中文参数 MinionHurtDuration（默认 0.25 秒上限），所有小怪恢复后再计算完整 HurtCooldown。普通伤害仅在本次进入 Hurt 时给小怪完整击退，防止轻击绕过保护持续推退；质量降低和挂账零伤害纯击退保留，Boss 约束不变。FieldMinionBrain 去掉重复受击 Cue/冷却覆盖。弹簧怪先完成已起跳的运动，再在落地后判断丢失目标。Unity 6000.6.4f1 隔离 Play Mode 的 MinionResilienceValidation.RunBatch：旧逻辑 7 组失败，修改后 53 项通过、0 失败；含实际物理、暂停、快照与中文 Inspector 编译。夹具提高实例血量以观测完整保护期，未改资产数值。隔离工程位于 %TEMP%/rule-minion-resilience-20261017，日志 %TEMP%/rule-minion-resilience-{before,after}.log；未跑正式地图难度、全 Boss 或 Player 构建。保留同期字体和 ProjectSettings 的用户修改。

分析日期：2026-10-15；基线 `9af8f80`（最新角色动画），关闭白盒无敌已提交为 `f927297`。当前工作分支 `feat/better-boss`（fork → 原仓库 `p2-p3-merge` 的 PR #10），已衔接之前的 Boss 距离/破韧与身体阻挡，并新增蝙蝠高机动空战。Unity 工程在仓库下的 `Project-Rule/`，不是仓库根目录。

## 已确认环境

- Unity `6000.6.4f1`，来自 `ProjectSettings/ProjectVersion.txt`。
- 单机 2D 横版项目；URP 17.6.0，QualitySettings 的管线 GUID 指向 `Assets/_Project/Settings/UniversalRP.asset`。
- Input System 1.20.0，PlayerSettings `activeInputHandler = 1`；玩家控制器通过 PlayerInput 使用 Input System。Cinemachine 6.6.0 已用于最新测试场景。
- UnityHFSM 固定 `v2.3.0`，Boss 使用 Alive / Transition / Dead 外层状态机与动作子状态机。
- 已安装 Yarn Spinner 嵌入包；Unity Test Framework 1.8.0 声明在 manifest 中。未发现一方独立 NUnit 测试程序集；项目常用 Editor 验证器。
- 无已确认的联网玩法。TapTap 发行为文档目标，实际目标平台构建未验证。

## 结构与程序集

主要源码根为 `Assets/_Project/Scripts`，数据为 `Data`，表现资源为 `Art`、`UI`、`Shaders`，可复用实体为 `Prefabs`。

| 程序集 | 职责 / 依赖 |
| --- | --- |
| Infrastructure | 时间、注册表、快照、流程与共享契约；基础层 |
| Core | 规则门面与六类属性；依赖 Infrastructure |
| Gameplay | Player、Combat、Enemy、Scene；依赖 Core、Infrastructure、Input System、UnityHFSM |
| Persistence | 存档；依赖 Core、Infrastructure |
| UI / Presentation | 界面与表现，可读 Gameplay 和上游状态 |
| Development / Editor | 白盒、验证器、资源生成；Editor 仅编辑器使用 |

Dialogue 在 Gameplay、UI、Editor、Development 下另有专用程序集。保持现有单向依赖，规则系统不认识具体敌人。

## 启动与敌人链路

Build Settings 当前只启用 `Assets/_Project/Scenes/SampleScene.unity`。敌人六个独立白盒位于 `Scenes/EnemyLabs`，未加入构建。`RuleSystemDriver` 创建共享规则、时间等服务；完整产品关卡启动链未在本次任务验证。

玩家已拆为 PlayerController 编排与 PlayerInput、MotionTuning、ContactSensor、MotionMover 等职能类；身体阻挡必须通过 MotionTuning.BodyBlockLayer 传给 MotionMover，不能只向 PlayerController 加字段。

`EnemyActor` 接线并接收实际伤害 → `EnemyBrain` / 四个 Boss Brain 决策 → `EnemyMotor` 在物理步提交碰撞移动。伤害共用 CombatSystem / DamageProcessor，规则施法共用 RuleReleaseService。EnemyDefinition 是共享配置，运行状态由实例持有。表现层订阅动画、反馈和恢复事件。敌人及投射物通过 ISnapshotParticipant 参加内存快照。

## 约束与风格

- 先读仓库 `CLAUDE.md`，架构与接口文档位于 `Doc/`；四空格、Allman 大括号、命名空间 `RuleGame.<层>.<模块>`。
- 2D 物理使用 Physics2D，位移在 FixedUpdate 中推进；Unity 6 API 为 linearVelocity / Rigidbody2D.Slide。
- 暂停由 TimeManager / EntityStatus 显式控制，不能通过把 timeScale 设为零实现。
- 冷却使用游戏物理时钟，需支持暂停与快照。死亡/过期对象停用以保留回溯能力。
- 每个新 Assets 文件都要配 .meta；修改配置字段保持序列化名称和资源 GUID。
- 动画素材和第三方资源不作为一方代码重构范围。

## 工具与验证

manifest 已有 Coplay Unity MCP，仓库 `.mcp.json` 指向 `localhost:8080/mcp`。本次端口不可用，未暴露可调用的 Unity MCP 工具；未安装或替换提供方。

本机现已安装项目要求的 Unity `6000.6.4f1`。本次在 `/tmp/rule-bat-flight-20261015` 中复制相关一方运行源码、原版 UnityHFSM 2.3.0、真实蝙蝠定义/动画及精灵，使用同一版本运行隔离 Play Mode。输入程序集复制自当前工程 Library，未修改项目包依赖、项目设置或主角动画资源。

- `BatFlightValidation.RunBatch`：21 项通过，检查真实位移、第一阶段俯冲、预警与锁定方向、恢复、撤离、边界、暂停、快照，以及 DashWindup 的四帧配置、实际精灵播放与休息时实际上升、高空往返、停火、边界和暂停。
- `BossCombatValidation.RunBatch`：119 项通过，包含阶段节奏、破韧打断、俯冲方向快照与短展翅动作断言，以及四种 Boss 的休息窗口、暂停、快照、反击受伤和指针停火。
- `BossBodyCollisionValidation.RunBatch`：9 项通过，覆盖新版玩家移动模块的身体阻挡、冲刺、反向脱离、迎面、接触伤害和触发器。
- 当前完整 `Test_Enemy_BatBoss` 白盒已进入 Play 观察到一阶段 Recover/Fly、飞行与攻击特效，主角无敌关闭并正常掉血；测试后退出 Play。Editor.log 未发现 C# 编译错误。未执行 Player 构建或完整难度平衡验收。

已修复一次表现回归：将完整 23 帧展翅压入 0.5 秒会产生约 46 帧/秒的快速拍翼。独立 DashWindup 仅取前四帧，完整 WingSpread 施法序列不改；生成工具也同步生成短序列，避免重建后复发。验证期间出现与此前相同的 UnityEditor.Search.SearchDatabase 索引异常，以及隔离靶标未绑定死亡广播的警告，未改生产逻辑来掩盖它们。

日志位于 `/tmp/rule-bat-windup-validation.log`、`/tmp/rule-bat-windup-brains.log`、`/tmp/rule-bat-body-validation.log`，各验证器的计数结果保存在隔离工程 `Logs/`。`git diff --check` 已通过；未改动同事的 Hero 图集、动画剪辑和 Animator Controller。

2026-10-16 增加统一 `Rest`：一套攻击后不再出招，`BossRestDuration` 地面默认 1.8 秒、蝙蝠默认 3.2 秒，均不随阶段缩短。发条默认两轮指针后休息，由 `ClockBeamVolleysBeforeRest` 调整；指针时钟在休息中冻结。兔子每轮投掷后休息，蝙蝠攻击后升至场地高度的 80%，围绕休息起点左右各 2.5 米往返，速度为基础巡航的 0.85 倍；不追踪玩家、不出招。高度、距离和速度均在 Inspector 配置，巡飞端点与方向参加快照。破韧恢复继续剩余休息；阶段和死亡优先。同时关闭飞行实体的 Slide 贴地吸附，修复落地后有上升速度却无法升空的问题。地面实体的吸附行为不变。生成工具同步配置。新增验证日志为 `/tmp/rule-bat-rest-patrol-combat.log`、`/tmp/rule-bat-rest-patrol-flight.log` 和 `/tmp/rule-bat-rest-patrol-body.log`。

2026-10-16 三种地面 Boss 重做战术：积木新增 Advance 压近与分裂后有限连招、起手锁定投掷；发条改为瞄准/错位/分叉激光，预警站定，响铃锁定短冲撞，受阻或到边界后休息；兔子近身藏身撤退、墙角高抛，快弹/高抛交替，翻转后有限散射。新参数在 EnemyDefinition 对应三个分组。兔子素材仍为空，藏身用半透明占位表现，不加无敌或穿人。遵照本轮要求只运行 `BossCombatValidation.RunTacticsBatch` 做必要的定向验证，28 项通过且编译通过，不重跑前述历史完整套件。日志 `/tmp/rule-three-boss-tactics.log`，计数 `Logs/BossTacticsValidation.txt`。前述 119/21/9 是战术重做前的历史结果。

2026-10-16 蝙蝠新增 Approach：高空休息结束后主动降至玩家侧面，近身优先连斩；近战后轮换远程或阶段招式，不再一直停留在高处，也不再刚接近就先撤离。距离/高度/尝试时长/速度均可调，近战偏好参加快照。EnemyDefinitionEditor 将全部 65 个参数、枚举选项和动作时长列表中文化，保留原字段名、Undo、多选与数值范围。本轮只运行蝙蝠相关的 22 项物理/行为检查并通过编译，未重复运行三 Boss 或全量验证。日志 `/tmp/rule-bat-approach-inspector.log`。用户同期在本地调整了三份 Definition、两份 Boss Prefab、字体和项目设置，未纳入本轮代码提交。

2026-10-16 修复快照异常与震屏后镜头停住：UnityHFSM 2.3.0 的子状态机退出时会清空 activeState，阶段过渡/死亡期间不应读取 Actions.ActiveStateName；EnemyBrain.Capture 现在仅在 Root 为 Alive 时记录动作，恢复逻辑继续按 Root 分支处理。BossCameraImpulse 结束渲染/停用时改为 ResetWorldToCameraMatrix，归还随 Transform 更新的自动视图计算；写回旧矩阵仍会保留手动模式。隔离 Play Mode 先复现四种 Boss 过渡/死亡采集异常，以及震屏收尾/停用后的视图冻结，共 10 个失败用例；同组修复后 36 项断言通过、退出码 0。验证入口 BossCombatValidation.RunSnapshotCameraBatch，日志 /tmp/rule-snapshot-camera-{baseline,fixed}.log，结果 Logs/SnapshotCameraValidation.txt。摄像机通过调用实际渲染回调并检查移动后的视图矩阵验证，未运行完整白盒画面验收或全量战斗套件。

2026-10-16 排查时钟冲撞起点跳变：用户本地 ClockBoss_Definition 已设 Time × Revert，JumpShake 完成后 CastRule 会同步回溯整个世界，包括施法者的位置、Brain 动作与锁定方向；旧回调随后无条件 Go(Charge) 会覆盖恢复结果，甚至使用快照中尚未锁定的零方向。ClockBossBrain 增加不随时间回退的恢复修订计数，施法期间发生恢复就结束旧回调，按恢复后的动作继续决策；未改变时间反转语义或用户资产配置。使用真实 TimeRevert 规则、费用服务和 SnapshotSystem，先复现旧位置 Idle 被强制替换为 Charge，修复后 RunClockRewindBatch 的 8 项检查通过，包含成功回溯/扣费、恢复动作、重新响铃选向，以及缓冲不足时从当时位置正常冲撞/不扣费。日志 /tmp/rule-clock-rewind-{baseline,fixed}.log。仅做这组定向验证，未重跑完整战斗套件。

2026-10-16 时钟按三阶段重整独立指针：一阶段时针，二阶段加分针，击破既有弱点窗口后第三阶段加秒针。旧实现计时被身体动作/其他激光预警阻塞，显示另用全局时钟且先显示秒针占位；现在各针自己倒计时、只在自身出手时重置，解锁时从完整周期起算，显示直接读取 Brain 的对应进度。休息和阶段过渡统一冻结，身体忙时到期只保留一次待发，不积压补射。沿用 ClockHourInterval=12，新增 ClockMinuteInterval=6 与 ClockSecondInterval=3，Inspector 中文化，既有资源槽位顺序保持不变。RunClockHandsBatch 在 Unity 6000.6.4f1 隔离 Play Mode 中 30 项通过（含上一轮真实时间反转的 8 项），覆盖三阶段解锁/实际资源槽位、独立出手与预警不互阻、显示角度/暂停/快照、身体动作计时、休息和弱点失败路线；只运行该定向组，未重跑其他 Boss 或全量战斗。日志 /tmp/rule-clock-hands.log，结果 Logs/ClockHandsValidation.txt。同期其他任务正在修改兔子素材与相关源码，本次只提交时钟相关内容；EnemyLabBuilder 仅取三项周期默认值的独立改动。

2026-10-16 用户再次反馈乱跑瞬移，最终取消时钟 JumpShake 完成时的隐式 CastRule，直接从当前位置进入 Charge。此前恢复修订计数只防止旧回调覆盖恢复结果，并未去掉起跑前的全局回溯，不能满足乱跑起点连续的要求；该计数已删除。保留 Definition 的用户配置和显式施法语义，不修改 TimeRule/SnapshotSystem 或为施法者添加免疫。RunClockRewindBatch 的定向用例改为：有/无历史缓冲时起点一致、不扣施法能量、锁向正确、实际物理步连续移动、缓冲未被消耗，以及显式反转仍回溯/扣费。修复前复现有缓冲时回到历史位置；修复后 Unity 6000.6.4f1 隔离 Play Mode 中 14 项通过，日志 /tmp/rule-clock-charge-fixed.log，计数 Logs/ClockChargeValidation.txt。未重跑独立指针或全量战斗检查。

2026-10-16 接入补交的兔子 111 张 PNG：正式 RabbitBoss Prefab、18 个动作/辅助序列和三类双帧投掷物，原画按 8 fps 播放。固定画布脚底锚点保留跳跃，藏身改为空帽跳跃；Flip 先缩回再反转，二阶段持续显示倒置帽子，Dead 完整播放后停用。投掷间隔延长时，弹体仍在原画第 12 帧附近出手。三类白底投掷物仅生成透明裁切运行时副本，交付原件和身体 PNG 字节不变。新增 import_rabbit.py 与保留参数的兔子单独导入菜单，通用资源/特效重建保留兔子接线。使用真实 Prefab、动画与组件的隔离 Play Mode 定向验证 58 项通过、退出码 0，覆盖播放、出手、自定义间隔、藏身、阶段、暂停、池复用、快照和死亡；原工程兔子白盒也已进入 Play 观察到冒出、施法与翻转起手。日志 /tmp/rule-rabbit-animation-validation.log，结果 Project-Rule/Logs/RabbitAnimationValidation.txt。隔离工程出现既有 UnityEditor.Search.SearchDatabase 索引异常；无新增 C# 编译错误，未执行 Player 构建或全套战斗平衡验证。

2026-10-16 按用户要求将时钟激光完全移出身体状态机：新增纯 C# ClockBeamEmitter，由 EnemyActor.FixedUpdate 单独推进，各针仍按阶段解锁，但在 JumpShake、Charge、Rest、Hurt、Stun、Recover、Rewind 中都持续按周期发射；发射不写移动、不触发休息，也不等待 Idle/Walk。身体冲撞只按自己的冷却与距离选招。全局/单体暂停冻结，死亡停火；目标丢失仍推进周期但不补射。指针显示读取独立计时，敌人快照同时恢复计时。保留周期超时余量防止漂移；旧 ClockBeamVolleysBeforeRest 字段停用并隐藏，未改用户资产参数。RunClockHandsBatch 在 Unity 6000.6.4f1 隔离 Play Mode 中 51 项通过，包含七种身体状态并行、三阶段解锁、预警不互阻、暂停、快照、死亡、真实 FixedUpdate 接线和此前冲撞连续性的 14 项检查；退出码 0，无新增 C# 编译错误，仍有既有 SearchDatabase 索引异常。日志 /tmp/rule-clock-periodic-validation.log，计数 Project-Rule/Logs/ClockPeriodicValidation.txt。未重跑其他 Boss、全量战斗或 Player 构建。

2026-10-16 在兔子交付资源基础上强化 AI：目标超射程时持续追近；投掷先跟踪、出手前 0.35 秒锁定，按距离和高度选弹珠平射/跳棋高抛。藏帽只沿锁定路线向外撤退，墙角无空间就反击；冒出动画结束后保留完整撤退冷却，避免反复藏身。翻转后由实例 Brain.IsFlying 驱动 EnemyMotor 实际升空换侧，不改共享 Definition；用鸭子打两轮三发交错扇形，结束后悬停休息且仍可受伤。飞行路线与弹道选择参加快照，回溯到第一阶段恢复地面模式。新增六项中文 Inspector 参数。用户提供路径的 111 张运行时帧已核对来源哈希，身体原件未改，其它角色清单未改。本轮只运行 RunRabbitTacticsBatch：Unity 6000.6.4f1 独立副本 /tmp/rule-rabbit-ai-20261016 中 23 项通过，含真实物理升空、实际精灵选择、平射/高抛、墙角反击、撤退冷却、扇形角度、休息/暂停/快照；日志 /tmp/rule-rabbit-ai.log。前述 58 项资源播放检查为接入时的历史结果，本轮未重跑。同期时钟发射器等改动不纳入本次提交。

2026-10-16 用户补充微信目录中的“敌人逻辑”，并明确“不必完全参考”。原始文本与八张 drawio 已保存到 Doc/References/EnemyLogic，作为可调整的角色/阶段/招式参考；本轮兔子主动换位、实际飞行、有限散射和休息节奏保留，不回退为原图的机械循环。原文本普通投掷写 0.5 秒、行为图写 2 秒；当前沿用交付 16 帧、8 fps 的 2 秒投掷动作及其原画出手点。该轮仅补充参考与约束说明，没有修改已经通过 23 项检查的运行代码。

2026-10-16 用户澄清时钟激光必须随针尖持续旋转扫射。此前独立计时仍保留了瞄准玩家的 FireHand，且每圈一次会让固定针向总在十二点附近；本轮取消预测瞄准、散射和目标存在门槛，周期参数改为各针旋转一圈的时间，资产原数值未改。ClockBeamEmitter 为每根已解锁指针持有一束持续 EnemyBeam；画面与伤害均读取同一 HandDirection，分针原图朝下仅在表现层补偿。新光束预警一次后持续显示与判定，接触按 ContactInterval 结算；暂停冻结指针/预警/伤害冷却，死亡停用，回溯恢复角度/预警/冷却并清除未来阶段的光束。普通短脉冲仍保留原生命周期，池复用重置扫射绑定。Unity 6000.6.4f1 隔离 Play Mode 的 RunClockHandsBatch 67 项检查通过、退出码 0，覆盖各针四个象限、父节点旋转、真实光束端点与命中/转离停伤、七种身体状态、持续显示、暂停、快照、池复用、死亡和冲撞连续性；仍有既有 SearchDatabase 索引异常。日志 /tmp/rule-clock-sweep-validation.log，计数 Project-Rule/Logs/ClockSweepValidation.txt。未跑全套 Boss 或 Player 构建。

2026-10-16 修复兔子投掷物提前砸地：下落速度外推会把锁定点推入地板；原高抛补偿随后被固定总速度归一化，在增强重力下射程不足。仅兔子改为按目标碰撞体中心瞄准，用身体盒扫掠限制预判点不穿地形；按飞行时间保留完整初速度，近距离高抛保留至少 0.65 秒飞行时间。出手时机、锁定窗口与其他敌人弹道不变。新增 RabbitProjectileValidation，Unity 6000.6.4f1 隔离 Play Mode 基线 7 项中 3 项失败（正反向下落、三倍重力）；修复后 7 项通过，并检查弹池复用刚体位置。兔子原有 RunRabbitTacticsBatch 的 23 项也通过。日志 /tmp/rule-rabbit-projectile-before.log、/tmp/rule-rabbit-projectile-after.log、/tmp/rule-rabbit-projectile-tactics.log；未跑其他 Boss 或全量构建。

2026-10-16 用户反馈常驻激光不消失且转速过快。现改为停火 → 每次预警 → 0.35 秒短扫射 → 完全消失；光束存活期间仍沿针尖旋转。ClockHour/Minute/SecondInterval 恢复为消失后的停火时间（当前资产 4/2/1 秒），新增独立 RotationPeriod 默认 24/12/6 秒一圈；发射间隔不再影响转速。停火计时仅在上一束光消失后推进，避免短周期拼成常驻激光。预警与发射寿命、旋转/停火时钟都支持暂停和快照，普通脉冲寿命保留。真实 ClockBoss_Definition 缺省新字段已由同版本 Unity 加载并验证，无需手改资产。RunClockHandsBatch 在 Unity 6000.6.4f1 隔离 Play Mode 中 61 项通过、退出码 0，包含完整亮灭周期/完全停火/再预警、真实默认转速、独立转动、方向与伤害、七种身体状态、暂停、回溯、池复用及冲撞连续性。日志 /tmp/rule-clock-pulse-validation.log，计数 Project-Rule/Logs/ClockPulseValidation.txt。未重跑全套 Boss 或 Player 构建。

2026-10-16 用户反馈仍向脚底投掷、倒立未实际起飞。本轮补齐上一轮未覆盖的近身与倒立进攻条件：翻转演出开始即启用实例飞行并垂直离地；空中换位超时不能越过身体离地间距和侧向站位检查，玩家钻到正下方时先重新换位。地面贴身时重新站位，墙角反击落点至少越过自己的身体。兔子使用身体前方的独立出手点，弹道按该点计算；可选 origin 对其他敌人的原有调用不产生变化。中文 Inspector 将换侧时长说明为参考时长。新增三条回归在旧实现均失败，修复后 RabbitProjectileValidation 共 10 项通过，含实际 RabbitBoss prefab 物理：翻转中已上升，首轮 body=(0.87,3.00)、出手点=(2.12,2.70)、目标=(6,0.65)。RunRabbitTacticsBatch 23 项通过，原正下方攻击用例改为有效侧向站位；墙角反击、休息、快照保留。日志 /tmp/rule-rabbit-takeoff-before.log、/tmp/rule-rabbit-takeoff-after.log、/tmp/rule-rabbit-takeoff-tactics.log。未跑其他 Boss 或全量构建。

2026-10-16 用户澄清“不是倒立散射，是不倒立时也往脚底丢”。本轮只改站立 Throw/HideThrow 的初速度求解：旧普通弹 y 速度为 -2.42，旧近处/远处高抛实际仅升高 0.06/0.11 米；改为按目标与出手点之上的弧顶求解，普通弹默认上抛 1 米、高抛 1.6 米后落向锁定的主角位置。新增中文 Inspector 参数 RabbitThrowArcHeight（站立投掷上抛高度），倒立散射不使用该参数且未改动。世界失重时沿用直线运动，正常重力/增强重力均按实际加速度计算上抛。RabbitProjectileValidation 原有 10 项加入初速度向上、实际升高和到达目标检查后，旧实现 5 项失败，修复后 10 项通过；实测普通弹升高 0.96 米、高抛 1.56 米并到达目标，三倍重力为 1.52 米。RunRabbitTacticsBatch 的 23 项通过，锁定用例改验低弧向上而非旧低重力平射。日志 /tmp/rule-rabbit-standing-arc-before.log、/tmp/rule-rabbit-standing-arc-after.log、/tmp/rule-rabbit-standing-arc-tactics.log。未跑其他 Boss 或全量构建。

2026-10-17 按新增动作需求表接入九种关卡小怪：StoneGuard、ExplosiveBlock、BrickSoldier、SpringToy、ClockMouse、ClockBomb、PlushBird、HugBear、PlushSeal。用户确认弹簧怪同样可重组、默认等待7秒。EnemyKind只追加，Definition.IsBoss显式区分四Boss；旧两种召唤物保留MinionBrain。新增FieldMinionBrain共享感知/巡逻/散架/快照，按行为分七种Brain。Actor新增身体/移动/接触开关及去重范围伤害，Motor提供真实跳跃，PlayerController的持有者集合仅限制自主移动、保留挥剑。拥抱通过反击、距离、超时、死亡/停用释放；回溯恢复限制。新增9配置/9白盒预制体/9可玩场景与保留参数的生成菜单，正式美术未交付时用FieldMinionView；旧Boss生成器范围固定原六种避免误建新类型。需求摘要保存在Doc/References/EnemyLogic/FieldMinionRequirements.json。Unity6000.6.4f1隔离PlayMode的FieldMinionValidation46项通过，日志/tmp/rule-field-minions-validation.log；未跑全Boss、正式关卡验收或Player构建。当前分支feat/minion-ai来自feat/brick-city-components，本轮未合并上游或改地图摆放。

## 证据

`CLAUDE.md`、`Doc/系统模块架构.md`、`Doc/敌人系统接入.md`；工程内 `Packages/manifest.json`、`ProjectSettings/{ProjectVersion.txt,QualitySettings.asset,GraphicsSettings.asset,EditorBuildSettings.asset,ProjectSettings.asset}`；一方 `.asmdef`；`Scripts/Gameplay/Enemy/*.cs`、`Gameplay/Player/EntityStatus.cs`、`PlayerController.cs`、`RuleReleaseService.cs`、`Scripts/Editor/Enemy/EnemyLabBuilder.cs`、`Scripts/Rules/RuleSystemDriver.cs`。

<!-- unity-onboarding:generated:end -->

用户分支命名要求：不使用 codex/ 前缀；新功能分支使用 feat/ 等项目惯用前缀。

2026-10-17 小怪战术复查：守卫新增限距追击/短时视线记忆，飞鸟保持射程，海豹按可达距离/施法可用性选技能并增加滑行前摇；弹簧空中锁向、发条鼠限时钻地。关卡小怪累计破韧阈值（重型20/其他12）与1.2秒硬直保护；近战前摇早期跟随、出手锁向，命中去重，保留不硬直时闪白。参数中文可调。修改前7组针对性用例失败，修改后隔离PlayMode65项通过，日志/tmp/rule-minion-tactics-after.log；未验证正式关卡整体难度。

2026-10-17 小怪预制体放置简化：九种 FieldMinions 已通过 Editor API 补齐身体 Hurtbox、显式受击承受方和出生点相对活动范围。EnemyActor 自动绑定玩家、复用场景 CombatSystem（缺少时创建共享服务），缺失/自动绑定的目标失效时每 0.5 秒重试；旧显式引用和 Boss 世界边界保留。新增 UI Toolkit 简化 Inspector 与 Scene 二维边框手柄，默认宽高 20×20，范围固定于出生点。寻路白盒清除临时受击箱，使用预制体继承的一个受击箱和自动接线。使用文档为 Doc/小怪预制体使用指南.md。隔离工程保持实际核心程序集名称编译，20 项定向检查通过，补充核对九种资产的受击承受方与程序集标识；未进行原工程 Play、面板视觉操作、完整 Player 构建和敌群性能验收。

2026-10-18 小怪寻路交付同步受伤视觉 PR #12（48ca1f6）和最新 p2-p3-merge（5fd88a7），保留两套改动。合并时结合弱点 Hurtbox 开合与扣血反馈，Boss 资产采用最新集成版本。EnemyNavigationValidation.RunCombatBatch 针对合并风险运行 12 项通过，覆盖弹体/光束、弱点受击箱开合和一次扣血一次反馈；未重复运行导航或渲染套件。小怪提交排除自动 stash 中的字体图集和 ProjectSettings，原 stash 保留。

2026-10-18 魔王城地图组件：依据本次《游戏组件需求大全.xlsx》交付九类机关与地形/共用滑板车，共 21 个 Prefab（Prefabs/DemonCastle）、独立 Test_DemonCastleComponents 和 Tools/规则游戏/魔王城 放置面板。支持中文搜索、网格吸附、连续点放、父物体、Prefab Stage 与 Undo/Redo；生成仅补缺失资产。新增运行时位于 Gameplay/Scene/DemonCastle，复用 CityMechanism/CityPathMotor、世界规则/快照、现有承载与弹射接口；烟花和落石走 Hurtbox 与 CombatSystem。拼图房、烟花开关组和钢琴房已内部接线。白盒外观和合成琴音可替换，使用说明 Doc/魔王城组件接入.md。Unity6000.6.4f1完整隔离工程验证 87 项通过，含通过两枚发条求解及真实 URP 渲染；日志 /tmp/rule-demon-final-validation.log。未改正式场景、Build Settings、包或原有代码；未跑整关/Player构建。仍见已有 Unity Search 索引异常。

2026-10-18 魔王城补充美术接入：用户给出 Downloads/场景组件(4) 后，从功能组件与同级基础瓦片接入 13 张魔王城地形、2 张按钮状态和2张溜溜球，共17张原PNG，使用 Sprite Data Provider 裁透明边距、保留原图字节和序列共同轴心。预制体增至32个；地面/短长平台/按钮替换外观，额外地块/立柱/中平台可从Tools放置；溜溜球只作双帧视觉件，行为未定义。其余9类机关美术未找到，用户确认暂无其它位置，保留原功能和占位。Presentation/DemonCastle 的 SpriteView 负责暂停与按钮选帧，Editor ArtSetup 按版本标记执行一次美术迁移，保留后续调参。完整隔离工程124项通过、0失败，含真实URP；日志 /tmp/rule-demon-art-validation.log，预览 Doc/Preview/DemonCastle/DeliveredArt.png，来源表 Doc/References/DemonCastle/DemonCastleArtSources.json。未改其它正式地图、包或Build Settings。

2026-10-18 魔王城功能补查及菜单迁移：魔王城工具统一移至 Tools/规则游戏/地图组件/魔王城，放置菜单分地形与装饰、机关、组合机关、美术展示；用户明确保留地图组件下其它条目。单发条/琴键/拼图本体和烟花标注接线要求，溜溜球明确仅动画。32个Prefab实际构成为14个静态地形、17个机关单体/组合和1个视觉件。补充实际按钮触碰点火、落石命中、木马/滑板车往返承载、推力探测及质量削弱后真实推动的检查；隔离基线135项通过0失败，日志 /tmp/rule-demon-function-audit.log。未修改运行代码、正式地图或其它条目；同工作区NPC/轮盘/UI的同期修改未纳入本次检查。

2026-10-18 按用户要求完善魔王城测试场：保持 Test_DemonCastleComponents.unity 的GUID，改成00_Ground与Zone_01—12分区，覆盖九个机关区域及地形试跳/六种立柱/溜溜球展示。DemonCastleLabControls增加下拉/F1/F2跳转、随位置变更说明、生命能量HUD、F5复原机关/角色、F6/F7快照、F9补能、每区复活点。Tools/规则游戏/地图组件/魔王城 提供生成/打开/明确重建测试场景。完整工程副本 /tmp/rule-demon-scene-20261018 验证150项0失败；日志 /tmp/rule-demon-scene-validation.log。已在当前Editor实际进入Play看中文HUD并用F1跳区，完成后退出Play并留在本场景；仅调整测试场、生成器和测试控制，不写回LevelRuntime或正式地图。

2026-10-18 魔王城提交验证：以本地 p2-p3-merge（73857b6）为基线建立 codex/demon-castle-components 独立分支，仅纳入魔王城资产、代码、测试场与文档；在该分支完整工程运行 DemonCastleValidation.RunBatch，150项通过、0失败，Unity进程退出码0。日志 /tmp/rule-demon-branch-validation.log；未纳入规则轮盘美术或NPC的同期工作，未运行Player构建。

2026-10-18 魔王城工具按用户要求改为平铺菜单：移除组件放置面板和放置/分类子菜单，32个Prefab在 Tools/规则游戏/地图组件/魔王城 下直接列出，点击放到Scene视图中心。DemonCastlePalette精简并重命名为DemonCastleMenu，保持脚本GUID、Prefab连接及Undo/Redo/Prefab Stage放置实现；同步使用文档和现有验证引用。当前Unity Editor刷新编译后检查实际菜单，实际点击岩石平台·短可放置且撤销后移除；32项唯一菜单与32个Prefab一一对应。未修改运行时、预制体、场景或其它地图组件菜单。

2026-10-18 魔王城菜单进一步收窄：按用户要求仅保留17个机关单体及组合件的平铺放置入口，移除15个静态地形/立柱/纯动画入口；原有32个Prefab及测试场布局保留。Unity刷新编译后核对实际Tools菜单，机关入口完整，地块、岩石平台、立柱和溜溜球入口均已消失，其它地图组件条目保留。

2026-10-18 规则轮盘美术：接入Src/ui，10(1).18，12am/规则释放中的实心/空心原图，提取6张属性牌、5张关系牌及对应光边，保留手绘轮廓。共享RuleWheel.uss自动覆盖Boot/LevelRuntime、四Boss及规则测试场景；使用仍为Shift、鼠标悬停和左键释放。RuleFan增加0.24秒依次浮出、悬停抬起金光和0.18秒释放散开/取消淡出，使用unscaledDeltaTime，逻辑关闭立即归还输入/镜头/时间，快速重开取消旧淡出。独立副本Unity6000.6.4f1 PlayMode验证62项通过，涵盖贴图、慢时间动画、左右镜像与正向文字、一次扣费、免费取消、快速重开和四轮真实A/D输入。原验证在旧场景被x=3靶子阻挡，未改轮盘的基线同样失败；仅测试运行时移开Dummy/TestBox，镜头检查改用已有RuleSelectionCamera.IsFocused，费用仅测试临时设7点并恢复。日志/tmp/rule-wheel-art-final.log，报告/左右截图/动图在Doc/UI预览/RuleWheelPaper*；使用和手改位置见Doc/规则轮盘美术接入.md。未执行Player构建或全量Boss回归。

2026-10-18 用户要求清亮高频。本轮先调正在迭代的玩家Hurt/Land及Explosion：移除旧2.2–2.6kHz低通，玩家3.5kHz高架+5–6dB/260Hz低中频-3dB，保留14kHz带宽；落地切换concrete002短鞋底瞬态，两个旧WAV原路径/GUID保留。Explosion制作Explosion_Bright.wav（高架+5dB/低中频-2.5dB），EditorAPI只改Cue30素材引用，事件音量/音高/BGM0.25保留。与2c619d0相比3–12kHz相对RMS占比+8.20/+9.86/+6.16dB；Hurt/Land全带宽RMS近似原值，爆炸为峰值余量降低3.53dB。指标是PCM测量，不代表人工听感。prepare_player_feedback.py支持--install，prepare_bright_explosion.py可重现；来源/处理/hash/频段对照均保存在Doc/AudioSources，合集Doc/AudioPreviews/BrightFeedback/Overview.wav。原工程试听41项、玩家轮盘76项、世界85项通过，恢复用户Test_Enemy_BatBoss；未重跑BGM或Player构建。整套音效范围的可选澄清尚未回复，本轮未改其他Cue音色。
