# Boss 网上重选试听

本轮19项 Boss 事件使用17份实际素材。按当前工作台配置，规则施放与阶段转换共用，兔子藏帽与冒出共用；未引用的两份派生素材已清理。原始来源全部为本次网上下载，不读取用户的 Src 素材，也不复用上一版 Boss 派生素材。

试听合集已乘当前事件音量与 0.85 主音量，各事件间隔0.4秒，音高固定1便于对比；游戏中仍沿用原有随机音高与距离衰减。

| 合集 | 顺序 |
| --- | --- |
| [积木](Brick.wav) | 蓄力、落锤、投掷、分裂 |
| [时钟](Clock.wav) | 冲撞、弹幕预警、光束、回溯、核心暴露 |
| [兔子](Rabbit.wav) | 藏帽、冒出、魔法投射 |
| [蝙蝠](Bat.wav) | 俯冲预警、俯冲、展翼、叫声 |
| [共用提示](Shared.wav) | 施法、召唤、阶段转换 |
| [全部](Overview.wav) | 依上表顺序；具体时间点见 Timeline.json |

选材方向：积木以厚木块与钝击为主，分裂叠加碎裂；时钟以真实上发条、齿轮与金属卡扣为主，预警用三次递进发条声；兔子藏帽保留布料与短促魔法声；蝙蝠采用实录蝙蝠叫声、布料拍动与竹棍破风。术法提示采用重新下载的魔法音效。

来源与许可：

- [Kenney Impact Sounds](https://kenney.nl/assets/impact-sounds) / [RPG Audio](https://kenney.nl/assets/rpg-audio)，CC0。
- [Brian MacIntosh Clock Wind Sounds](https://opengameart.org/content/clock-wind-sounds) / [Mechanical Sounds](https://opengameart.org/content/mechanical-sounds)，CC0。
- [qubodup 竹棍破风](https://opengameart.org/content/swish-bamboo-stick-weapon-swhoshes) / [Earth Element Magic Spell](https://opengameart.org/content/earth-element-magic-spell)，CC0。
- [AntumDeluge / polymorpheva Bat Screeches](https://opengameart.org/content/bat-screeches)，CC0。
- [Ogrebane Teleport Spell](https://opengameart.org/content/teleport-spell)，CC0。
- [ViRiX Dreamcore (David Mckee) Magic SFX Sample](https://opengameart.org/content/magic-sfx-sample)，CC BY 3.0；署名与修改说明见随素材的 Credits.txt。

加工只涉及截取、单声道、重采样、去直流、相对增益、分层、淡入淡出；时钟回溯包含反转发条声。未添加EQ、失真、位深破坏或人工生成的音调。全部原件、下载URL和SHA256在 Doc/AudioSources/BossOnline/Downloads.json，实际采用的层、起止点、相对音量及输出哈希在 BossSfxManifest.json。

可运行 `python Tools/Audio/prepare_boss_sfx.py` 重建；需 numpy、soundfile 和支持7z的 tar。

清理后的当前配置在原工程 Unity 6000.6.4f1 中编译并通过20项定向检查：19个Boss事件的新文件哈希、导入PCM、配置引用和原生参数试听输出一致；实际使用17份素材。全音效配置63处素材引用均可解析，清理未修改用户当前音效配置。未进入Play、保存场景、重跑玩法检查或执行Player构建。

清理后仅保留实际选材使用的15份网上原件/原始压缩包；删除未采用的候选、网页缓存和旧版Boss下载。重建脚本与验证使用当前共用关系，验证菜单只读，不再把手动选择改回默认。
