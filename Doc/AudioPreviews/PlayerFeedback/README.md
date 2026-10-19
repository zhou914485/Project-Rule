# 玩家反馈试听候选

当前采用 Hurt_A/ Land_A 用于玩家受击与落地，B 保留对照；准备脚本本身不修改游戏配置。来源为 Kenney Impact Sounds，CC0；原件在 Doc/AudioSources/FeedbackOriginals，处理与哈希在 PlayerFeedbackManifest.json。

| 候选 | 方向 | 原素材 |
| --- | --- | --- |
| Hurt_A | 保留清脆高频的短击 | impactPunch_heavy_003 |
| Hurt_B | 带高频细节的软撞击 | impactSoft_medium_003 |
| Land_A | 清楚的鞋底接触瞬态 | footstep_concrete_002 |
| Land_B | 较柔和的脚步对照 | footstep_carpet_000 |

不带叫声、呻吟或木板敲击。撤销旧版2.2–2.6kHz低通，3.5kHz以上高架提升5–6dB，仅在14kHz处保留带宽保护；削减260Hz低中频，保留起音和短淡出。目标是用户要求的清亮高频；实际听感仍需在游戏中确认。

无后缀 WAV 为可接入素材；_Audition.wav 每段重复三次，已乘拟定事件音量（受击0.60/落地0.33）与主音量0.85。接入时使用无后缀版本，避免重复乘音量。两种受击、两种落地分别匹配近似RMS并保留峰值余量。

复现：python3 Tools/Audio/prepare_player_feedback.py /path/to/kenney_impact-sounds.zip，需要 ffmpeg；加 --install 可更新两个现有 Unity 播放文件，保留其 .meta。
