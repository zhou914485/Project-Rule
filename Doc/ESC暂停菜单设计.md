# ESC 暂停菜单设计

> **状态**：**已落地**（2026-10-20，DSH 实现）；视觉仍是占位色块，等美术资产到位再换皮
> **预览**：
> - 主画面 [暂停菜单_预览.png](UI预览/暂停菜单_预览.png)
> - 四态对照 [暂停菜单_状态对照.png](UI预览/暂停菜单_状态对照.png)
> **生成脚本**：`Tools/Art/pause_menu_preview/gen_preview.py`（纯 PIL，复用工程真实精灵，可重跑）

---

## 0. 一句话

按 ESC 停住世界，只给三个出口：**继续 / 回到上一个存档点 / 回到主菜单**。
两个危险动作都要二次确认，且**默认焦点落在「取消」**——回车永远先救人。

---

## 1. 现状：能力齐了，入口没接

| 已有 | 位置 |
| --- | --- |
| 暂停 = 停物理步进 + 停规则时钟 | `TimeManager.SetPaused(bool)`（`Infrastructure/Time/TimeManager.cs`，把 `Physics2D.simulationMode` 切成 `Script`） |
| 玩家侧冻结（冷却 / 能量） | `EntityStatus.SetPaused(bool)` |
| 屏蔽玩法输入 | `((IRuleSelectionInputGate)player).BlockGameplayInput()` 返回租约 |
| 暂停期遮罩 / 面板的现成美术 | `Panel_Cream.png`（9-slice）、`Button_Cream.png`、`Button_Close.png` |
| 主菜单里「暂停 + 输入租约 + 退出时恢复旧值」的成套写法 | `MainMenuPresenter.EnterMenu / ReleasePause` |

**缺的只有三件事**：ESC 入口、暂停面板、两个流程动作（回档 / 回主菜单）。

⚠️ 现在 ESC 被四处消费，**没有一处是「游戏中暂停」**：
`MainMenuPresenter`（关对话框）、`RuleWheelPresenter`（关轮盘）、`TrainTravelPanel`（取消选站）、`P4DemoController`（演示用）。
新增入口必须排进这条**返回栈**，不能抢。

---

## 2. 五条判据

1. **ESC = 逐层返回**，不是「到处都开暂停」：轮盘 / 火车面板 / 对话框 / 确认态开着时，ESC 只关最上面那一层。
2. **危险操作不抢回车**：确认态默认焦点在「取消」，「回到存档点 / 回主菜单」需要玩家主动移过去。
3. **一屏三个键，不搞二三级菜单**：设置 / 音量留在主菜单，暂停菜单里不出现。
4. **键盘 / 手柄全可达**（默认就是手柄场景），鼠标可选：hover 高亮、点击直接生效。
5. **暂停只走 `TimeManager`**，绝不 `Time.timeScale = 0`（项目铁律，会瞬移穿碰撞体）。

---

## 3. 布局规格（1920 × 1080 画板）

沿用主菜单那套 **1920×1080 artboard + 等比缩放 + 上下留黑**（`MainMenuPresenter.ViewportChanged` 同一算法），
所有坐标都是画板坐标。

### 3.1 遮罩

| 项 | 值 |
| --- | --- |
| 底色 | `rgba(11, 26, 40, 0.52)` |
| 暗角 | 径向，边缘压到 `0.78`，半径约屏宽 60% 处开始 |
| HUD | **留在遮罩之上**，整层 62% 不透明度 —— 世界被压暗，血条 / 生效规则仍然读得出来 |

### 3.2 面板

| 项 | 值 |
| --- | --- |
| 尺寸 | **660 × 522**，屏幕居中（x 630–1290，y 279–801） |
| 底色 | `#F8F0DA`（与 `Panel_Cream.png` 同色），9-slice 可直接用那张图 |
| 描边 | `3px #6B4A2A`，圆角 `8`，内 1px 高光 `rgba(255,250,235,0.47)` |
| 投影 | `0 26px 64px rgba(8,18,26,0.42)` |
| 内边距 | 上 34 / 左右 44 / 下 30，内容宽 **572** |

### 3.3 面板内部（自上而下）

| 元素 | 位置 / 尺寸 | 字号 · 颜色 |
| --- | --- | --- |
| eyebrow 左 `PAUSED` | y 313 | 12px，字距 3，`#967E58` |
| eyebrow 右 `规则时钟已停` | 右对齐 y 313 | 12px，字距 6，同上 |
| 标题 `暂停` | y 335 | **54px 粗**，字距 12，`#3A2A14` |
| 副标题 `世界已经静止。想清楚了再继续。` | y 399 | 17px，`#7A6A4F` |
| 分隔线 | y 449，通栏 | 1px `#D9C8A4`，左实右虚 |
| 按键 ×3 | y 471 / 561 / 651，**572 × 76**，间距 14 | 24px 粗 `#43301A`，字距 2 |
| 按键说明行（仅存档点项） | 键内 y +18 | 13px `#8D7A58` |
| 底部键位提示 | y 749，居中 | 13px `#97866A`，键名加粗 `#6F5C3D` |

### 3.4 按键的四态

| 态 | 表现 |
| --- | --- |
| 常态 | 底 `#FDF6E6`，2px 边 `#B39463`，圆角 5 |
| hover | 底提到 `#FBEEC9`，边 `#8A6A3A` |
| **焦点** | 3px 边 `#D9A63F` + 左侧 **6 × 60 金色指示块**（键外 −14）+ 整行右移 4px + 轻投影 |
| 禁用 | 底 `#F1E9D7`，边 `#D6C8AB`，字 `#A89A80`，右侧一道 26px 短杠；**键盘焦点跳过它** |

> 焦点的金色取自主菜单 `.art-button:focus` 的 `#f3ca70` 一档，压暗到 `#D9A63F` 以适配纸底。

---

## 4. 四个状态（见对照图）

| | 状态 | 关键点 |
| --- | --- | --- |
| **A** | 打开时 | 焦点在「继续」，`Esc` 角标常驻；按 Esc / Enter 立刻回游戏 |
| **B** | 还没有存档点 | 「回到上一个存档点」置灰 + 一行说明（**说清怎么存档**：打一下秋千存档点） |
| **C** | 确认：回到存档点 | 默认焦点「取消」；顶部红字 `不可撤销`；中间一张存档点卡片（点名 / 关卡 / 多久以前） |
| **D** | 确认：回到主菜单 | 同一套版式，主按钮换成危险红 `#A8432C` |

**为什么确认态要单独一屏而不是弹窗套弹窗**：面板本身就是最上层，再叠一层会让「返回栈」多一层、
也让 ESC 的语义变含混。**在同一个面板里换内容**，返回栈深度不变。

---

## 5. 交互

| 输入 | 行为 |
| --- | --- |
| `Esc` / 手柄 `Start` | 游戏中：打开；菜单里：继续（= 关闭）；确认态里：取消 |
| `↑ ↓` / 左摇杆 | 焦点上下（**循环**，跳过禁用项） |
| `Enter` / 手柄 `A` | 触发当前焦点 |
| `← →` | 仅确认态：在「取消 / 确认」间切 |
| 鼠标 | hover 高亮；点击即触发（确认态同样，不额外加「长按」） |

- 打开时把焦点放到「继续」；关闭时焦点还给打开前的元素（`MainMenuPresenter.ShowDialog/CloseDialog` 同一套做法）。
- **不做**打开动画的位移，只做 120ms 透明度淡入 —— 暂停手感要「立刻停」，不要「飘出来」。

---

## 6. 与真实系统的接线

### 6.1 打开 / 关闭（照抄 `MainMenuPresenter` 的租约写法）

```csharp
_status = _player.GetComponent<EntityStatus>();
_pausedBefore = TimeManager.Instance.IsPaused;      // ⚠️ 先记旧值
_statusPausedBefore = _status.IsPaused;
TimeManager.Instance.SetPaused(true);
_status.SetPaused(true);
_inputLease = ((IRuleSelectionInputGate)_player).BlockGameplayInput();
_ownsPause = true;
// 关闭时：Dispose 租约 → 两个 SetPaused 都恢复成旧值 → _ownsPause = false
```

**为什么要记旧值**：规则轮盘 / 存档过场本身可能已经处于暂停态，直接 `SetPaused(false)`
会把它们一起放出去。

### 6.2 三个按键

| 按键 | 现在能调的 API | 还差什么 |
| --- | --- | --- |
| 继续 | 上面的关闭流程 | 无 |
| 回到上一个存档点 | **快照回滚**：`SnapshotSystem.CaptureCheckpointSnapshot()`（存档驱动落盘时拍）+ `TryRestoreCheckpointSnapshot()`；兜底才走 `Load/Apply` + `ReloadAll` | 已接（§11） |
| 回到主菜单 | 无 | **完全没有入口**：`MainMenuPresenter._started` 是私有、`GameRoot.HasStarted` 只读、分区卸载与「回到标题」没有串联。这一项要先做主菜单侧的返回路径 |

⚠️ **「回到上一个存档点」到底是哪种语义，必须先定**（见 §8 第 1 条）：
- **读盘回档**（当前 UI 文案按这个写的）：丢掉存档点之后的进度，机关 / 血量 / 规则全部还原。
- **内存快照回滚**（`SnapshotSystem` 有 `Pin` / `TryRestorePinned`）：能保留更多，但和「存档点」不是一回事。

### 6.3 禁用条件

| 条件 | 效果 |
| --- | --- |
| 没有存档（`Load()` 返回 null / `LatestRespawnId` 为空） | 「回到上一个存档点」禁用（状态 B） |
| 玩家死亡中（死亡动画未播完） | 整个菜单不打开（ESC 忽略），避免和 `Respawn()` 抢流程 |
| 存档过场 / 黑场进行中 | 不打开 |

---

## 7. 边界情况

- **规则轮盘开着时按 ESC**：只关轮盘，不进这个菜单（轮盘本身就是暂停态，两者不能同时存在）。
- **对话 / 火车面板开着时按 ESC**：只关它们。
- **暂停中 Animator 仍在播**：`Physics2D.simulationMode = Script` 只停物理，Animator 仍吃 `deltaTime`。
  这是**既有暂停的已知行为**，不在本 UI 范围内；要冻结动画由表现层把 `Animator.speed` 归零。
- **暂停中不应触发任何玩法输入**：靠 `BlockGameplayInput` 租约，别只靠面板挡在前面。
- **HUD 在遮罩之上**：暂停时玩家仍要能看血量和剩余规则时间 —— 这是把 HUD 从「世界」里拆出来的理由。

---

## 8. 未定（需要主程 / 策划拍板，**不替团队决定**）

1. ~~回档语义~~：**已定 = 快照回滚**（2026-10-20）：存档点落盘时顺手拍一张快照，回档走 `TryRestoreCheckpointSnapshot()`；
   跨分区 / 没有快照时才退回读盘 + 重载分区。
2. **回主菜单后本局怎么办**：清空进新局，还是保留内存状态等下次「继续游戏」？
3. **要不要第四个键「重开本关」**：`LevelFlow.OnRestartRequested` 现成可用，但需求只要三个键，故未加。
4. **暂停与规则轮盘的关系**：轮盘是否应该从暂停菜单里进（「暂停 → 拼规则」），还是维持现在的独立入口？
5. **音效**：打开 / 关闭 / 焦点移动 / 确认，四个音效事件是否进 `敌方与机关音效接入` 那套总线。

---

## 9. 原计划清单（已由 §11 的落地实现取代，保留以便对照）

| 文件 | 内容 |
| --- | --- |
| `Assets/_Project/UI/Pause/PauseMenu.uxml` | 面板 + 三个键 + 确认态（同面板换内容，`.hidden` 切换） |
| `Assets/_Project/UI/Pause/PauseMenu.uss` | §3 全部数值；字体 `P4ChineseFont`；**现在用纯色 + 描边占位**，换皮时再换成 `Panel_Cream.png` / `Button_Cream.png` 的 9-slice |
| `Scripts/UI/Pause/PauseMenuPresenter.cs` | 打开/关闭租约、焦点管理、确认态切换、三个回调（`RuleGame.UI` 程序集） |
| `Prefabs/LevelRuntime.prefab` | 挂到常驻 UI 节点（**不要每个场景手加**，GameRoot / Boot 都继承它） |
| `PlayerController` 输入分支 | ~~ESC 只发一个事件~~ → 实际没动玩家输入：ESC 由 `PauseMenuPresenter` 自己读，
与主菜单 / 火车面板 / 轮盘同一套做法（面板开着时玩家输入已被 `BlockGameplayInput` 收走） |

**验收判据（建议写成编辑期 / 隔离场景冒烟）**

1. 游戏中 ESC → 面板出现，`Physics2D.simulationMode == Script` 且 `TimeManager.IsPaused`；
2. 再按 ESC → 两处都恢复，且**恢复成进入前的值**（在轮盘暂停态下进入再退出，仍是暂停）；
3. 焦点在「继续」时按 Enter 直接回游戏；Tab / ↓ 循环且**跳过禁用项**；
4. 没有存档时「回到上一个存档点」禁用且有说明行；
5. 确认态默认焦点在「取消」；Esc 取消回主面板，不直接关整个菜单；
6. 暂停期间玩家不响应移动 / 跳跃 / 攻击输入；
7. 死亡动画播放中按 ESC 无反应。

---

## 10. 配色速查

| 用途 | 值 |
| --- | --- |
| 纸底 | `#F8F0DA` |
| 按键底 / 悬停 | `#FDF6E6` / `#FBEEC9` |
| 面板描边 | `#6B4A2A` |
| 按键描边 | `#B39463` |
| 正文墨色 | `#3A2A14`（标题）/ `#6D5C40`（说明）/ `#97866A`（弱化） |
| 焦点金 | `#D9A63F` |
| 危险红 | `#B4462F`（文字）/ `#A8432C`（按钮底） |
| 主按钮深蓝 | `#23303F` |
| 遮罩 | `rgba(11,26,40,0.52)` |

---

## 11. 实现记录（2026-10-20）

### 11.1 落地的文件

| 文件 | 作用 |
| --- | --- |
| `Assets/_Project/UI/Pause/PauseMenu.uxml` | 主面板 + 确认面板（同一 UIDocument 内切换），元素名与 §3 对应 |
| `Assets/_Project/UI/Pause/PauseMenu.uss` | §3 全部数值；纯色 + 描边占位 |
| `Scripts/UI/Pause/PauseMenuPresenter.cs` | ESC 仲裁、暂停租约、确认态、回档 / 回主菜单 |
| `Prefabs/LevelRuntime.prefab` 新增子物体 `Pause Menu` | UIDocument（sorting 300）+ Presenter（`_player` 已接） |
| `TrainTravelPanel` | 加 `public bool IsVisible`，给 ESC 仲裁用 |
| `MainMenuPresenter` | 加 `public void ShowMenu()`（回主菜单时把标题界面叫回来） |
| `GameRoot` | 加 `public void StopForMenu()`（卸分区 + 停流式加载 + 复位 `HasStarted`） |

### 11.2 两个实现决定

1. **ESC 仲裁靠执行序，不靠事件总线**：`PauseMenuPresenter` 是 `[DefaultExecutionOrder(-500)]`，
   比轮盘 / 火车面板 / 对话（默认 0）**更早**跑。它先看「这一帧谁开着」，有人开着就让路。
   跑在它们后面就会出事：轮盘在同一次 ESC 里关掉 → 菜单看到 `IsOpen == false` → 顺手弹出暂停。
2. **「回到上一个存档点」= 快照回滚**（2026-10-20 改）：`PersistenceDriver` 在 `SaveCheckpoint` 之后调
   `SnapshotSystem.CaptureCheckpointSnapshot()`（**与时间恒定的定点槽分开**，不能共用 —— 时间规则 Pin 时按 ESC 会互相踩）。
   回档时 `TryRestoreCheckpointSnapshot()` 一把写回玩家 / 敌人 / 机关 / 规则表 / 血量，**不读盘、不重载分区**；
   随后 `PlayerController.TeleportTo(body.position)` 收尾（清移动器里的旧速度 + `SyncTransforms`），
   否则解冻第一帧会带着暂停前的速度被推走。
   **兜底**：存档点分区已卸载（快照里的实体全空了）或没有快照（从盘续的档）→ 退回 `Load → Apply → ReloadAll`。
   两种路径都盖黑场（`IScreenFade`，Boot 里的 `SwingCheckpointFadeScreen`），面板与遮罩在过场期间收起。

### 11.3 已知缺口（下一轮）

- **手柄方向键导航没接**：UITK 默认导航认键盘（↑↓ / Tab），手柄要靠 Input System 的 UI 模块或自己转发
  `NavigationMoveEvent`；`Start` 键开关是通的。
- **没有音效**：开关 / 焦点移动 / 确认都还没接 `SfxPlayer`。
- **跨分区回档退回读盘**：快照只覆盖「拍快照时已加载的分区」；存档点在别的分区时会走兜底（读盘 + 重载），
  那一次仍然是一次空场（但有黑场盖着）。
- **快照拍在「打中存档点那一刻」**：秋千过场本身的动画状态不在快照里，回滚后人落在存档点附近、过场不重播。
- **`LoadContext.ContinueGame` 会被置成 true**：回档后再回主菜单、再「开始游戏」会被当成续档
  （`PlayerProgress.StartNewGame` 不执行）。要区分的话在主菜单侧清一次。
- **设置项没做**：按 §2 判据 3，音量 / 画面留在主菜单。
- **黑场层级**：`SwingCheckpointFade` 是 `OnGUI` 画的，UI Toolkit 面板若渲染在它之上会盖住黑场 ——
  本面板已把自己收起；HUD 这类别的 UIDocument 是否露在黑场上要实机看一眼（火车传送是同一套，可一起验）。

### 11.4 怎么验

1. 打开 `Scenes/Boot.unity` 按 Play，点「开始游戏」；
2. 游戏中按 `Esc` → 面板出现、世界停住（角色不动、机关不跑）；
3. 再按 `Esc` → 恢复，且**如果进面板前已经处于暂停**（例如轮盘开着时先关轮盘再进），退出后仍是暂停；
4. 走到 / 打一下秋千存档点，再开面板 → 「回到上一个存档点」可用，副标题显示存档点编号；
5. 确认态默认焦点在「取消」，`Enter` 先触发取消；
6. 「回到主菜单」→ 标题界面出现，再点「开始游戏」能重新开局。
