using UnityEngine;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene.Components
{
    /// <summary>机关被用掉时，把它的贴图切成碎片<b>塌落</b>下来：下坠、堆成碎石、淡出。</summary>
    /// <remarks>
    /// <para><b>运行时按原图切</b>：用 <see cref="Sprite.Create"/> 从源精灵的 <c>rect</c> 里切格子，
    /// 碎片与美术逐像素一致，换图不用另做碎块资源。前提是这张图没打进 Sprite Atlas ——
    /// 以后进了图集要改成按 <c>textureRect</c> 切，或者改成预切碎片。</para>
    /// <para><b>不接物理</b>：没有碰撞体也没有刚体，位移自己积分、按 <c>Physics2D.gravity</c> 下落，
    /// 与 <see cref="RuleGame.Gameplay.Player.PaperPlaneBurst"/> / <c>BoulderFragment</c> 同一条路线 ——
    /// 实心碎片会把玩家卡住。</para>
    /// <para><b>是塌落不是炸开</b>：水平速度只够让碎块向两侧散开一点，主体是往下掉；
    /// 落点按「中间高、两边低」的碎石堆算，上面的碎块落在别人身上而不是全摊平。</para>
    /// <para><b>地面是硬底</b>：按当前旋转角算矩形投影半高，碎块最多贴地，不会沉到地面以下。</para>
    /// <para>只做表现：不进快照、暂停时定格、寿命到了整把销毁（连运行时切的 Sprite 一起）。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CityShatterBurst : MonoBehaviour
    {
        /// <summary>整把碎片的寿命（秒）。</summary>
        public const float Lifetime = 1.1f;

        /// <summary>碎片受多少重力（1 = 与掉落物一致；塌落要比自由落体更沉一点）。</summary>
        private const float GravityScale = 1.6f;

        /// <summary>累计炸过几次（验证工具用）。</summary>
        public static int SpawnCount { get; private set; }

        private SpriteRenderer[] _pieces = System.Array.Empty<SpriteRenderer>();
        private Vector2[] _velocities = System.Array.Empty<Vector2>();
        private float[] _spins = System.Array.Empty<float>();
        private float[] _angles = System.Array.Empty<float>();
        private Vector2[] _sizes = System.Array.Empty<Vector2>();
        private float[] _startY = System.Array.Empty<float>();
        private float[] _restHeights = System.Array.Empty<float>();
        private Color[] _colors = System.Array.Empty<Color>();
        private Sprite[] _sprites = System.Array.Empty<Sprite>();
        private float _floorY;   // 地面线：精灵轴心那一行
        private float _age;

        /// <summary>
        /// 把 <paramref name="source"/> 当前的图炸成 <paramref name="shards"/> 片左右。
        /// </summary>
        public static CityShatterBurst Spawn(SpriteRenderer source, int shards = 12)
        {
            if (!Application.isPlaying) return null;   // 只做运行时表现：编辑期自检不该往场景里塞碎块
            if (source == null || source.sprite == null || shards <= 0) return null;

            var go = new GameObject("CityShatterBurst");
            go.transform.position = source.transform.position;

            var burst = go.AddComponent<CityShatterBurst>();
            if (!burst.Build(source, shards))
            {
                Destroy(go);
                return null;
            }

            SpawnCount++;
            return burst;
        }

        private bool Build(SpriteRenderer source, int shards)
        {
            Sprite sprite = source.sprite;
            Texture2D texture = sprite.texture;
            if (texture == null || sprite.rect.width < 2f || sprite.rect.height < 2f) return false;

            // 按图片长宽比挑列数，让碎块接近方形；再按请求数量补足行数。
            float aspect = Mathf.Max(0.25f, sprite.rect.width / Mathf.Max(1f, sprite.rect.height));
            int columns = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(shards * aspect)), 2, 6);
            int rows = Mathf.Clamp(Mathf.CeilToInt(shards / (float)columns), 2, 8);
            int count = columns * rows;

            float[] xs = SplitLine(columns);
            float[] ys = SplitLine(rows);

            Rect spriteRect = sprite.rect;
            Vector2 pivotPx = sprite.pivot;
            float ppu = sprite.pixelsPerUnit;
            Vector2 scale = source.transform.lossyScale;
            Vector2 centerPx = new Vector2(spriteRect.width * 0.5f, spriteRect.height * 0.5f);

            _pieces = new SpriteRenderer[count];
            _velocities = new Vector2[count];
            _spins = new float[count];
            _angles = new float[count];
            _sizes = new Vector2[count];
            _startY = new float[count];
            _restHeights = new float[count];
            _colors = new Color[count];
            _sprites = new Sprite[count];
            _floorY = source.transform.position.y;   // 精灵轴心在底部：这条线就是地面


            int index = 0;
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    float x0 = spriteRect.x + spriteRect.width * xs[col];
                    float x1 = spriteRect.x + spriteRect.width * xs[col + 1];
                    float y0 = spriteRect.y + spriteRect.height * ys[row];
                    float y1 = spriteRect.y + spriteRect.height * ys[row + 1];

                    var rect = new Rect(x0, y0, Mathf.Max(1f, x1 - x0), Mathf.Max(1f, y1 - y0));
                    Sprite piece = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), ppu);
                    piece.name = "Shard";

                    var shard = new GameObject("Shard");
                    shard.transform.SetParent(transform, false);

                    var renderer = shard.AddComponent<SpriteRenderer>();
                    renderer.sprite = piece;
                    renderer.sharedMaterial = source.sharedMaterial;
                    renderer.sortingLayerID = source.sortingLayerID;
                    renderer.sortingOrder = source.sortingOrder + 1;
                    renderer.color = source.color;

                    // 精灵轴心可能是左下角（补给石像就是 0.5 / 0），碎片要按「相对轴心的像素偏移」摆。
                    var offset = new Vector2(rect.center.x - spriteRect.x - pivotPx.x,
                                             rect.center.y - spriteRect.y - pivotPx.y) / ppu;
                    shard.transform.position = source.transform.TransformPoint(offset);
                    shard.transform.localScale = scale;
                    float tilt = Random.Range(-16f, 16f);   // 轻微歪斜，别像整齐的瓷砖
                    shard.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);

                    // 塌落而不是炸开：主体是往下掉，水平速度只够向两侧散开一点。
                    float side = offset.x - (centerPx.x - pivotPx.x) / ppu;
                    side = Mathf.Abs(side) < 0.05f ? (Random.value < 0.5f ? -1f : 1f) : Mathf.Sign(side);
                    float halfWidth = Mathf.Max(0.01f, spriteRect.width * 0.5f / ppu);
                    float nx = Mathf.Clamp01(Mathf.Abs(offset.x) / halfWidth);

                    _velocities[index] = new Vector2(side * Random.Range(0.45f, 1.35f) + Random.Range(-0.25f, 0.25f),
                                                     Random.Range(-0.25f, 0.15f));
                    _spins[index] = Random.Range(-260f, 260f);
                    _angles[index] = tilt;
                    _sizes[index] = new Vector2(rect.width / ppu * scale.x, rect.height / ppu * scale.y);
                    _startY[index] = shard.transform.position.y;
                    // 中间高、两边低的碎石堆：塌下来的碎块落在别人身上，而不是全摊平在地上。
                    _restHeights[index] = Mathf.Lerp(0.30f, 0.02f, nx) * Random.Range(0.4f, 1.1f);
                    _colors[index] = renderer.color;
                    _pieces[index] = renderer;
                    _sprites[index] = piece;
                    index++;
                }
            }
            return true;
        }

        /// <summary>把 0~1 切成 <paramref name="parts"/> 段，分界带一点随机偏移，避免碎成规整格子。</summary>
        private static float[] SplitLine(int parts)
        {
            var line = new float[parts + 1];
            line[0] = 0f;
            line[parts] = 1f;
            for (int i = 1; i < parts; i++)
            {
                line[i] = Mathf.Clamp01(i / (float)parts + Random.Range(-0.08f, 0.08f));
            }
            return line;
        }

        private void Update()
        {
            if (TimeManager.Instance?.IsPaused == true) return;   // 暂停：碎片定格

            float dt = Time.deltaTime;
            _age += dt;

            if (_age >= Lifetime)
            {
                Destroy(gameObject);
                return;
            }

            // 塌完再淡出：前 65% 不透明，最后 35% 消失。
            float fade = Mathf.Clamp01((1f - _age / Lifetime) / 0.35f);

            for (int i = 0; i < _pieces.Length; i++)
            {
                SpriteRenderer piece = _pieces[i];
                if (piece == null) continue;

                _velocities[i] += Physics2D.gravity * GravityScale * dt;

                Vector3 position = piece.transform.position + (Vector3)(_velocities[i] * dt);
                _angles[i] += _spins[i] * dt;
                piece.transform.Rotate(0f, 0f, _spins[i] * dt);

                // 地面是硬底：按当前旋转角算矩形的投影半高，碎块最多贴地滑动，不会沉到地面下。
                float radians = _angles[i] * Mathf.Deg2Rad;
                Vector2 size = _sizes[i];
                float halfHeight = (Mathf.Abs(size.x * Mathf.Sin(radians)) + Mathf.Abs(size.y * Mathf.Cos(radians))) * 0.5f;
                float floor = _floorY + halfHeight + _restHeights[i];
                floor = Mathf.Min(floor, _startY[i]);   // 本来就压在底下的碎块不需要往上跳
                if (position.y < floor)
                {
                    position.y = floor;
                    if (_velocities[i].y < 0f) _velocities[i].y = 0f;
                    _velocities[i].x *= 0.55f;          // 落地就基本不滑了
                    _spins[i] = 0f;                     // 也别在地上继续转
                }
                piece.transform.position = position;

                Color color = _colors[i];
                color.a *= fade;
                piece.color = color;
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _sprites.Length; i++)
            {
                if (_sprites[i] != null) Destroy(_sprites[i]);
            }
        }
    }
}
