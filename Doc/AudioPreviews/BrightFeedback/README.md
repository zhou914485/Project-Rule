# 清亮音色快速试听

Overview.wav 依次为：玩家受击两次 → 落地两次 → 爆炸一次。

取当前游戏的 PlayerHurt/PlayerLand/Explosion_Bright WAV，分别乘事件音量0.60/0.33/0.65及主音量0.85，固定原速，不含距离衰减。事件间隔350ms，组间追加400ms，开头静音200ms。源素材处理与哈希见PlayerFeedbackManifest及ExplosionBrightManifest；频段对比见BrightFeedbackVerification。

游戏中仍由原事件触发各自的独立短音效，这个合集仅供听感比较。
