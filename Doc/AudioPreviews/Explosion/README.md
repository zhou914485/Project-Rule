# 当前爆炸音效试听

原素材：Kenney [Sci-Fi Sounds](https://kenney.nl/assets/sci-fi-sounds) / explosionCrunch_000，CC0，原OGG保留。

游戏现在播放 Explosion_Bright.wav：削减280Hz低中频、3.5kHz以上高架提升5dB、保留14kHz带宽，仍有低频爆发主体；峰值限制通过整体增益留余量，不做波形硬削波。

Explosion_Audition.wav 为当前清亮版的单次试听，乘事件音量0.65及主音量0.85，不含距离衰减、引信。原件/处理/增益/哈希在 Doc/AudioSources/ExplosionBrightManifest.json。

复现：python3 Tools/Audio/prepare_bright_explosion.py --install，需要ffmpeg；不加--install只更新对照文件。
