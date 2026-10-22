using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 按「暴露面」给白盒铺美术。白盒刷实心块，本工具逐格判断哪些面朝外，按朝向选构件 + 变换。
    ///
    ///   面朝上   = 地板
    ///   面朝下   = 天花板（上下翻转）
    ///   面朝左   = 面朝左的墙（旋转 +90°）
    ///   面朝右   = 面朝右的墙（旋转 −90°）
    ///
    /// 三条关键规则（都是为了不糊）：
    ///
    /// 1) 用哪套件由**整条 run** 决定，不是逐格。美术件是半透明的（主体 alpha 0.96，端帽羽化到 0.02），
    ///    一重叠 alpha 就相加变亮。逐格判断会把同一条连续地面切成「厚件 + 薄件」混用，接缝处一厚一薄 → 糊。
    ///    现在：一条 run 里过半格子是「1 格厚的板」→ 整条用平台套件；否则整条用地面套件。
    ///
    /// 2) 所有 run 端点都用**硬切**，不用端帽。Ground Cap 外沿羽化，会在 Scene/Game 里留下灰雾边；
    ///    端点统一用中段石片，避免羽化边朝外，T 接头也不会露出圆头。
    ///
    /// 3) 厚结构不铺背面。3 格以上的厚墙正反两面都铺会大面积重叠 → 只铺外侧空间更大的一面；
    ///    厚地板的下表面不铺。厚天花板的下表面用菜单 ③ 单独铺。
    ///
    /// 用法：Hierarchy 选中逻辑 Grid，菜单 Tools/规则游戏/铺装美术/*（Ctrl+Shift+G = 全部）。
    /// 铺完自动隐藏白盒层（Ctrl+Shift+L 可再显示）。
    /// </summary>
    public static class GroundArtDresser
    {
        private const string StoneFolder = "Assets/_Project/Art/Sprites/Tiles/Stone/";
        private const string ArtChildName = "Art";
        private const int ArtSortingOrder = 1;
        private const int SpaceProbe = 64;

        /// <summary>九宫格构件的铺装重叠率：素材边缘有一列半透明羽化，紧贴会留下竖缝，压 2% 盖住。</summary>
        private const float NineSliceOverlap = 1.02f;

        /// <summary>冰面层：单独一张 Tilemap，碰撞体带 0 摩擦物理材质。</summary>
        private const string IceChildName = "Ice";
        private const string IceMaterialPath = "Assets/_Project/Data/Config/Ice_Surface.physicsMaterial2D";
        private const int IceSortingOrder = 0;

        /// <summary>危险格瓦片：开始场景的刺就是它，复用刺时按它的伤害配置补 HazardField。</summary>
        private const string SpikeTilePath = "Assets/_Project/Art/Tiles/Spike_Logic.asset";
        private const float SpikeDamage = 10f;
        private const float SpikeKnockback = 6f;
        private const float SpikeRepeatInterval = 0.5f;

        private enum Face { Up, Down, Left, Right }

        [MenuItem("Tools/规则游戏/铺装美术/① 全部（地板 + 墙 + 薄板 + 内部） %#g", false, 10)]
        public static void DressAll()
        {
            if (DressNineSlice()) return;   // 套件切好了九宫格：按角色铺，内部自然是满的
            Dress(null, false);
            FillInteriorMenu();             // 没切九宫格的老套件：按暴露面铺 + 内部补块
        }

        [MenuItem("Tools/规则游戏/铺装美术/② 只铺地板（面朝上）", false, 11)]
        public static void DressUp() => Dress(new[] { Face.Up }, false);

        [MenuItem("Tools/规则游戏/铺装美术/③ 只铺天花板（含厚结构的下表面）", false, 12)]
        public static void DressDown() => Dress(new[] { Face.Down }, true);

        [MenuItem("Tools/规则游戏/铺装美术/④ 只铺面朝左的墙", false, 13)]
        public static void DressLeft() => Dress(new[] { Face.Left }, false);

        [MenuItem("Tools/规则游戏/铺装美术/⑤ 只铺面朝右的墙", false, 14)]
        public static void DressRight() => Dress(new[] { Face.Right }, false);

        [MenuItem("Tools/规则游戏/铺装美术/⑥ 清空美术 %#h", false, 30)]
        public static void Clear()
        {
            var selected = Selection.activeGameObject;
            if (selected == null) { Debug.LogError("[铺装] 请先选中逻辑 Grid。"); return; }

            var root = ResolveRoot(selected);
            var artRoot = root.Find(ArtChildName);
            if (artRoot == null)
            {
                Debug.LogWarning($"[铺装] {root.name} 下面没有 {ArtChildName} 层，没什么可清的。");
                return;
            }

            int count = artRoot.childCount;
            ClearChildren(artRoot);
            Debug.Log($"[铺装] 已清空美术层：删掉 {count} 个构件。");
        }

        [MenuItem("Tools/规则游戏/铺装美术/⑦ 显示 / 隐藏白盒 %#l", false, 32)]
        public static void ToggleLogicRenderer()
        {
            var selected = Selection.activeGameObject;
            if (selected == null) { Debug.LogError("[铺装] 请先选中逻辑 Grid。"); return; }
            var tilemap = FindLogicTilemap(ResolveRoot(selected).gameObject);
            if (tilemap == null) { Debug.LogError("[铺装] 找不到 Tilemap。"); return; }
            var renderer = tilemap.GetComponent<TilemapRenderer>();
            if (renderer == null) return;
            renderer.enabled = !renderer.enabled;
            Debug.Log("[铺装] 白盒显示 = " + renderer.enabled);
        }

        /// <summary>建（或复用）冰面层，并把逻辑层里画着的冰面格搬过去。</summary>
        [MenuItem("Tools/规则游戏/铺装美术/⑧ 建冰面层（0 摩擦）", false, 33)]
        public static void BuildIceLayer()
        {
            var selected = Selection.activeGameObject;
            if (selected == null) { Debug.LogError("[铺装] 请先在 Hierarchy 里选中逻辑 Grid 物体。"); return; }

            var root = ResolveRoot(selected).gameObject;
            var logic = FindLogicTilemap(root);
            if (logic == null) { Debug.LogError("[铺装] 选中的物体下面找不到 Tilemap。"); return; }

            var ice = EnsureIceLayer(root, logic);
            int moved = MigrateIceCells(logic, ice);
            Debug.Log($"[铺装] 冰面层就绪：{IceChildName}（0 摩擦材质），从逻辑层搬过来 {moved} 格冰面。");
        }

        private static void Dress(Face[] only, bool includeThickDown)
        {
            var selected = Selection.activeGameObject;
            if (selected == null) { Debug.LogError("[铺装] 请先在 Hierarchy 里选中逻辑 Grid 物体。"); return; }

            // 选中 Grid 或它下面的 Logic / Mask / Art 都行：统一往上解析成 Grid。
            var root = ResolveRoot(selected).gameObject;

            var tilemap = FindLogicTilemap(root);
            if (tilemap == null) { Debug.LogError("[铺装] 选中的物体下面找不到 Tilemap。"); return; }

            var kits = LoadKits();
            if (kits == null) return;

            // 冰面必须单独一层（碰撞体才能带 0 摩擦材质）：先把逻辑层里画着的冰面格搬过去，
            // 否则它会以「实心几何」的身份参与暴露面判断、还会被当成墙去铺装。
            var iceLayer = EnsureIceLayer(root, tilemap);
            int movedIce = MigrateIceCells(tilemap, iceLayer);
            if (movedIce > 0)
            {
                Debug.Log($"[铺装] 已把 {movedIce} 格冰面从逻辑层搬到 {IceChildName} 层" +
                          "（同一格、高度不变，碰撞与 0 摩擦材质都归冰面层）。");
            }

            // 跳穿平台瓦片要 OneWayField 才能在运行时变成碰撞体：有这种格子就自动补上。
            EnsureOneWayField(tilemap);

            var artRoot = EnsureArtRoot(root);
            ClearChildren(artRoot);

            var grid = tilemap.layoutGrid;
            var cell = grid.cellSize;
            var bounds = tilemap.cellBounds;

            // 区域套件：这一格属于哪个王国，就用哪套方块。没配就回退出生点石头。
            var region = TileRegion.FindFor(tilemap);
            if (region == null || region.DefaultKit == null)
            {
                Debug.LogWarning("[铺装] 这张 Grid 上没配区域套件（TileRegion），全部按出生点石头铺。");
            }
            System.Func<Vector3Int, TileRegionKit> kitAt =
                c => region != null ? region.ResolveKit(grid.CellToWorld(c)) : null;

            // 危险瓦片（刺等）不是几何：既不给它铺墙皮，也不能让它参与暴露面判断。
            var hazards = tilemap.GetComponent<HazardField>();
            // 暗墙是几何（照常铺墙皮），但要单独记账：揭开时墙皮要跟着消失。
            var secretWalls = tilemap.GetComponent<SecretWallField>();

            var solid = new HashSet<Vector3Int>();
            var secret = new HashSet<Vector3Int>();
            // 表面瓦片（冰面等）：**是**实心几何（要挡住下面的外露面），但自己不铺装 ——
            // 它的贴图就是最终画面，直接搬到美术层。
            var surfaces = new HashSet<Vector3Int>();
            // 跳穿平台瓦片：也是实心几何（挡住下面），但美术要**染色**后再铺，
            // 让人一眼看出这一格能穿过去。
            var oneWay = new HashSet<Vector3Int>();
            var misplaced = new HashSet<string>();
            int misplacedCells = 0;
            // 名字带 Secret 却没被 SecretWallField 声明的瓦片：单独拎出来报错。
            var undeclaredSecrets = new HashSet<string>();

            for (int x = bounds.xMin; x < bounds.xMax; x++)
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var p = new Vector3Int(x, y, 0);
                    var tile = tilemap.GetTile(p);
                    if (tile == null) continue;

                    if (hazards != null && hazards.IsHazardCell(p)) continue;   // 刺：不是几何

                    bool isSecret = secretWalls != null && secretWalls.IsSecretCell(p);

                    // 既没被声明成暗墙、Collider Type 又是 None 的瓦片，基本是**画错层**了
                    // （最常见：刷遮罩时目标层还停在 Logic）。当成几何会凭空造出「墙」，
                    // 把正常墙的外露面挡掉 → 转角整块没有美术。
                    // 跳穿瓦片本来就没有碰撞（碰撞由 OneWayField 逐格生成），不算「画错层」。
                    bool isOneWay = tile is OneWayTile;

                    if (!isSecret && !isOneWay && tile is Tile bare && bare.colliderType == Tile.ColliderType.None)
                    {
                        misplaced.Add(tile.name);
                        misplacedCells++;

                        // 名字带 Secret 却没人声明 = 暗墙没登记进 SecretWallField。
                        // 症状很隐蔽：这些格子被当成「画错层」跳过，**一格美术都不铺** ——
                        // 看起来就是「隐形墙没被铺装」。
                        if (tile.name.IndexOf("Secret", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            undeclaredSecrets.Add(tile.name);
                        }
                        continue;
                    }

                    solid.Add(p);
                    if (isSecret) secret.Add(p);
                    if (tile is RuleGame.Gameplay.Scene.SurfaceTile) surfaces.Add(p);
                    if (isOneWay) oneWay.Add(p);
                }

            if (undeclaredSecrets.Count > 0)
            {
                Debug.LogError($"[铺装] {string.Join("、", undeclaredSecrets)} 是暗墙瓦片，" +
                               "但 Grid/Logic 上的 SecretWallField **没有声明它** —— " +
                               "这些格子被当成「画错层」跳过，**一格美术都不会铺**（看起来就是隐形墙没被铺装）。\n" +
                               "修法：菜单 Tools/规则游戏/关卡瓦片/⑤ 一键补登，或手动把它们加进 SecretWallField 的「暗墙瓦片」列表。");
            }

            if (misplacedCells > 0)
                Debug.LogWarning($"[铺装] 逻辑层里有 {misplacedCells} 格不属于逻辑层的瓦片（{string.Join("、", misplaced)}），" +
                                 "已按「不是墙」处理。两种常见原因：① 刷遮罩时选错了目标层（菜单 ② 移到 Mask 层）；" +
                                 "② 是暗墙瓦片但没登记进 SecretWallField（菜单 ⑤ 补登）。");

            if (solid.Count == 0) { Debug.Log("[铺装] 白盒是空的，没有可铺的面。"); return; }

            bool Has(int x, int y) => solid.Contains(new Vector3Int(x, y, 0));
            int Space(int x, int y, int dx, int dy)
            {
                int n = 0, cx = x + dx, cy = y + dy;
                while (n < SpaceProbe && !Has(cx, cy)) { n++; cx += dx; cy += dy; }
                return n;
            }

            var buckets = new Dictionary<Face, List<Vector3Int>>();
            foreach (Face f in System.Enum.GetValues(typeof(Face))) buckets[f] = new List<Vector3Int>();

            foreach (var p in solid)
            {
                if (surfaces.Contains(p) || oneWay.Contains(p)) continue;   // 表面/跳穿瓦片单独处理，不按面铺
                int x = p.x, y = p.y;
                bool up = !Has(x, y + 1);
                bool down = !Has(x, y - 1);
                bool left = !Has(x - 1, y);
                bool right = !Has(x + 1, y);
                if (!up && !down && !left && !right) continue;

                bool plateH = up && down;       // 正好 1 格厚的横板
                bool plateV = left && right;    // 正好 1 格厚的竖板
                // T 接头中心虽被竖向白盒挡住 Up 面，但横向美术带需要经过此格才能不断开。
                bool upJoin = !up && down && Has(x, y + 1) && Has(x - 1, y) && Has(x + 1, y);
                int spL = Space(x, y, -1, 0), spR = Space(x, y, 1, 0);

                if (plateH)
                {
                    // 1 格厚的板只铺一面：平台套件 0.48 已经占满 0.5 格，
                    // 两面都铺只会让两张件的羽化端帽重叠 → 端点亮斑。
                    if (up && (only == null || only.Contains(Face.Up))) buckets[Face.Up].Add(p);
                }
                else if (plateV)
                {
                    // 同上，1 格厚的竖板只铺外侧空间更大的一面
                    if (spL >= spR)
                    {
                        if (left && (only == null || only.Contains(Face.Left))) buckets[Face.Left].Add(p);
                    }
                    else
                    {
                        if (right && (only == null || only.Contains(Face.Right))) buckets[Face.Right].Add(p);
                    }
                }
                else
                {
                    if ((up || upJoin) && (only == null || only.Contains(Face.Up))) buckets[Face.Up].Add(p);
                    // 厚结构的下表面默认不铺（会和上面大面积重叠）；要铺用菜单 ③
                    if (down && includeThickDown && (only == null || only.Contains(Face.Down))) buckets[Face.Down].Add(p);
                    if (left && spL >= spR && (only == null || only.Contains(Face.Left))) buckets[Face.Left].Add(p);
                    if (right && spR >= spL && (only == null || only.Contains(Face.Right))) buckets[Face.Right].Add(p);
                }
            }

            int runs = 0, pieces = 0;
            foreach (var kv in buckets)
            {
                if (kv.Value.Count == 0) continue;
                bool vertical = kv.Key == Face.Left || kv.Key == Face.Right;
                foreach (var run in GroupRuns(kv.Value, vertical, secret, kitAt))
                {
                    // 暗墙那一段的构件要能被单独销毁：先记下本段新建了哪些子物体。
                    bool secretRun = secret.Contains(run[0]);
                    int firstNew = artRoot.childCount;

                    pieces += DressRun(artRoot, kv.Key, run, grid, cell, kits, solid, kitAt(run[0]));

                    if (secretRun)
                        for (int i = firstNew; i < artRoot.childCount; i++)
                            Undo.AddComponent<SecretArt>(artRoot.GetChild(i).gameObject);

                    runs++;
                }
            }

            // 表面瓦片（冰面等）：把自己的贴图搬到美术层，隐藏白盒之后它还看得见。
            pieces += DressSurfaces(artRoot, surfaces, tilemap, grid, cell);

            // 跳穿平台：用所在区域套件的方块，但**染上瓦片自己的颜色**。
            pieces += DressOneWay(artRoot, oneWay, tilemap, grid, cell, kits, kitAt);

            // 刺：跟着区域套件走（积木王国 = 旧版红色带凸起瓦片），不参与墙皮铺装。
            pieces += DressSpikes(artRoot, hazards, tilemap, grid, cell, kitAt, solid, kits.DefaultSpikes);

            var logicRenderer = tilemap.GetComponent<TilemapRenderer>();
            if (logicRenderer != null && logicRenderer.enabled)
            {
                logicRenderer.enabled = false;
                EditorUtility.SetDirty(logicRenderer);
            }

            // 铺完就把遮罩切成纯黑：这时候是「看最终效果」，半透明会让人误以为遮罩没生效。
            // 想改回半透明继续对位，跑菜单「③ 遮罩：半透明 ↔ 纯黑」。
            var maskLayer = root.GetComponentInChildren<MaskLayer>(true);
            if (maskLayer != null)
            {
                maskLayer.SetEditorAlpha(1f);
                EditorUtility.SetDirty(maskLayer);
            }

            Debug.Log($"[铺装] 完成：{runs} 段，{pieces} 个构件。白盒层已隐藏（Ctrl+Shift+L 可再显示）；" +
                      $"遮罩已切纯黑（菜单 ③ 可切回半透明）。");
        }

        // ==================================================================
        //  逻辑层瓦片体检 / 纠错
        // ==================================================================

        /// <summary>
        /// 抓「把遮罩刷进了逻辑层」这类选错目标层的事故。
        /// </summary>
        /// <remarks>
        /// Tile Palette 的刷子只认**当前选中的目标层**：选了 Mask_Black 但目标层还停在 Logic 时，
        /// 遮罩就刷进了逻辑层 —— 逻辑层从此多出一堆「看不见的墙」，
        /// 铺装器的外露面判断全歪，表现为**转角整块没有美术**。
        /// </remarks>
        [MenuItem("Tools/规则游戏/关卡瓦片/① 体检：逻辑层里有哪些瓦片", false, 40)]
        public static void InspectLogicTiles()
        {
            if (!TryGetLogicAndMask(out Tilemap logic, out Tilemap mask)) return;

            var logicTiles = CountTiles(logic);
            var maskNames = new HashSet<string>(CountTiles(mask).Keys);
            var lines = new List<string> { "[瓦片体检] 逻辑层：" };
            var stray = new List<string>();

            foreach (var kv in logicTiles)
            {
                lines.Add($"  {kv.Key} x {kv.Value}");
                if (maskNames.Contains(kv.Key)) stray.Add(kv.Key);
            }

            if (stray.Count == 0)
            {
                lines.Add("[瓦片体检] 没有发现刷错层的遮罩瓦片。");
                Debug.Log(string.Join("\n", lines));
                return;
            }

            lines.Add($"[瓦片体检] 逻辑层里有遮罩瓦片：{string.Join("、", stray)}。" +
                      "跑「② 把遮罩瓦片移回 Mask 层」清理，然后重铺一次。");
            Debug.LogWarning(string.Join("\n", lines));
        }

        /// <summary>把误画在逻辑层的遮罩瓦片搬回 Mask 层（保留你刷的位置）。</summary>
        [MenuItem("Tools/规则游戏/关卡瓦片/② 把遮罩瓦片移回 Mask 层", false, 41)]
        public static void MoveStrayMaskTiles()
        {
            if (!TryGetLogicAndMask(out Tilemap logic, out Tilemap mask)) return;

            // 「遮罩瓦片」= 出现在 Mask 层上的瓦片。不用手填名字，换素材也不会失效。
            var maskNames = new HashSet<string>(CountTiles(mask).Keys);
            var cells = new List<Vector3Int>();
            var bounds = logic.cellBounds;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    var tile = logic.GetTile(c);
                    if (tile != null && maskNames.Contains(tile.name)) cells.Add(c);
                }

            if (cells.Count == 0)
            {
                Debug.Log("[瓦片纠错] 逻辑层里没有遮罩瓦片，不需要处理。");
                return;
            }

            Undo.RecordObjects(new Object[] { logic, mask }, "移动刷错层的遮罩瓦片");
            foreach (var c in cells)
            {
                var tile = logic.GetTile(c);
                if (!mask.HasTile(c)) mask.SetTile(c, tile);
                logic.SetTile(c, null);
            }
            logic.RefreshAllTiles();
            mask.RefreshAllTiles();
            EditorUtility.SetDirty(logic);
            EditorUtility.SetDirty(mask);

            Debug.Log($"[瓦片纠错] 已把 {cells.Count} 格遮罩瓦片从逻辑层移回 Mask 层。现在选中 Grid 重铺一次（Ctrl+Shift+G）。");
        }

        /// <summary>遮罩预览：在「编辑器半透明」和「纯黑」之间切换（运行时始终纯黑）。</summary>
        [MenuItem("Tools/规则游戏/关卡瓦片/③ 遮罩：半透明 ↔ 纯黑", false, 42)]
        public static void ToggleMaskPreview()
        {
            var root = Selection.activeGameObject;
            if (root == null) { Debug.LogError("[遮罩] 请先在 Hierarchy 里选中逻辑 Grid 物体。"); return; }

            var layer = ResolveRoot(root).GetComponentInChildren<MaskLayer>(true);
            if (layer == null)
            {
                Debug.LogError("[遮罩] 没找到 MaskLayer 组件（应该挂在 Grid/Mask 上）。");
                return;
            }

            bool opaque = layer.EditorAlpha > 0.99f;
            layer.SetEditorAlpha(opaque ? 0.5f : 1f);
            EditorUtility.SetDirty(layer);

            Debug.Log($"[遮罩] 编辑器预览已切到 {(opaque ? "半透明 0.5" : "纯黑 1")}（运行时始终纯黑）。");
        }

        /// <summary>一键清掉逻辑层里的暗墙瓦片（改主意、不想留暗门时用）。</summary>
        /// <summary>
        /// 把逻辑层里**用到**的暗墙瓦片补进 <see cref="SecretWallField"/> 的声明列表。
        /// </summary>
        /// <remarks>
        /// 判据是瓦片**名字里带 Secret** —— 只加不删，不会误伤已经登记过的条目。
        /// 为什么需要它：漏登记的暗墙格会被铺装器当成「画错层」跳过，**一格美术都不铺**，
        /// 现场看起来就是「隐形墙没被铺装」，很难一眼看出是配置问题。
        /// </remarks>
        [MenuItem("Tools/规则游戏/关卡瓦片/⑤ 补登暗墙瓦片（SecretWallField）", false, 44)]
        public static void RegisterSecretTiles()
        {
            if (!TryGetLogicAndMask(out Tilemap logic, out _)) return;

            var field = logic.GetComponent<SecretWallField>();
            if (field == null)
            {
                Debug.LogError("[铺装] Grid/Logic 上没有 SecretWallField，先加上它再补登。");
                return;
            }

            var so = new SerializedObject(field);
            var array = so.FindProperty("_secretTiles");

            var already = new HashSet<TileBase>();
            for (int i = 0; i < array.arraySize; i++)
            {
                var t = array.GetArrayElementAtIndex(i).objectReferenceValue as TileBase;
                if (t != null) already.Add(t);
            }

            var used = new HashSet<TileBase>();
            BoundsInt bounds = logic.cellBounds;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var tile = logic.GetTile(new Vector3Int(x, y, 0));
                    if (tile == null) continue;
                    if (tile.name.IndexOf("Secret", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    used.Add(tile);
                }
            }

            int added = 0;
            foreach (var tile in used)
            {
                if (already.Contains(tile)) continue;

                array.arraySize++;
                array.GetArrayElementAtIndex(array.arraySize - 1).objectReferenceValue = tile;
                added++;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(field);

            if (added > 0)
            {
                Debug.Log($"[铺装] 已把 {added} 个暗墙瓦片补登进 SecretWallField（{string.Join("、", used)}）。" +
                          "重铺一次就会给这些格子铺上美术。");
            }
            else
            {
                Debug.Log($"[铺装] 逻辑层用到的暗墙瓦片都已登记过（共 {already.Count} 个），无需补登。");
            }
        }

        [MenuItem("Tools/规则游戏/关卡瓦片/④ 清掉逻辑层里的暗墙瓦片", false, 43)]
        public static void ClearSecretTiles()
        {
            var selected = Selection.activeGameObject;
            if (selected == null) { Debug.LogError("[瓦片工具] 请先在 Hierarchy 里选中逻辑 Grid 物体。"); return; }

            var root = ResolveRoot(selected);
            var logic = FindLayer(root, "Logic");
            if (logic == null) { Debug.LogError("[瓦片工具] 找不到名为 Logic 的 Tilemap。"); return; }

            var field = logic.GetComponent<SecretWallField>();
            if (field == null) { Debug.LogWarning("[瓦片工具] Logic 上没有 SecretWallField，不知道哪些算暗墙。"); return; }

            var cells = new List<Vector3Int>();
            var bounds = logic.cellBounds;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    if (field.IsSecretCell(c)) cells.Add(c);
                }

            if (cells.Count == 0)
            {
                Debug.Log("[瓦片工具] 逻辑层里没有暗墙瓦片，不需要处理。");
                return;
            }

            Undo.RecordObject(logic, "清除暗墙瓦片");
            foreach (var c in cells) logic.SetTile(c, null);
            logic.RefreshAllTiles();
            EditorUtility.SetDirty(logic);

            var where = new List<string>();
            for (int i = 0; i < cells.Count && i < 12; i++) where.Add(cells[i].ToString());
            Debug.Log($"[瓦片工具] 已清掉 {cells.Count} 格暗墙瓦片：{string.Join(" ", where)}" +
                      (cells.Count > 12 ? " …" : "") + "。现在选中 Grid 重铺一次（Ctrl+Shift+G）。");
        }

        private static Tilemap FindLayer(Transform root, string name)
        {
            foreach (var map in root.GetComponentsInChildren<Tilemap>(true))
                if (map.gameObject.name.Equals(name, System.StringComparison.OrdinalIgnoreCase)) return map;
            return null;
        }

        private static Dictionary<string, int> CountTiles(Tilemap map)
        {
            var result = new Dictionary<string, int>();
            var bounds = map.cellBounds;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var tile = map.GetTile(new Vector3Int(x, y, 0));
                    if (tile == null) continue;
                    result[tile.name] = result.TryGetValue(tile.name, out int n) ? n + 1 : 1;
                }
            return result;
        }

        private static bool TryGetLogicAndMask(out Tilemap logic, out Tilemap mask)
        {
            logic = null;
            mask = null;

            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogError("[瓦片工具] 请先在 Hierarchy 里选中逻辑 Grid 物体。");
                return false;
            }

            var root = ResolveRoot(selected);
            logic = FindLayer(root, "Logic");
            mask = FindLayer(root, "Mask");

            if (logic == null) { Debug.LogError("[瓦片工具] 找不到名为 Logic 的 Tilemap。"); return false; }
            if (mask == null) { Debug.LogError("[瓦片工具] 找不到名为 Mask 的 Tilemap。"); return false; }
            return true;
        }

        private static List<List<Vector3Int>> GroupRuns(List<Vector3Int> cells, bool vertical,
                                                       HashSet<Vector3Int> secret,
                                                       System.Func<Vector3Int, TileRegionKit> kitAt)
        {
            var result = new List<List<Vector3Int>>();
            foreach (var line in cells.GroupBy(c => vertical ? c.x : c.y))
            {
                var sorted = line.OrderBy(c => vertical ? c.y : c.x).ToList();
                var cur = new List<Vector3Int> { sorted[0] };
                for (int i = 1; i < sorted.Count; i++)
                {
                    int prev = vertical ? sorted[i - 1].y : sorted[i - 1].x;
                    int now = vertical ? sorted[i].y : sorted[i].x;

                    // 暗墙边界必须断开：一段要么全是暗墙、要么全是普通墙。
                    // 否则同一段墙皮会横跨两种格子，揭开时要么留残块、要么在正常墙上开洞。
                    bool sameKind = secret.Contains(sorted[i]) == secret.Contains(sorted[i - 1]);
                    // 区域边界也必须断开：一段里混两种石材，接头会一半石头一半发条。
                    bool sameKit = ReferenceEquals(kitAt(sorted[i]), kitAt(sorted[i - 1]));

                    if (now == prev + 1 && sameKind && sameKit) cur.Add(sorted[i]);
                    else { result.Add(cur); cur = new List<Vector3Int> { sorted[i] }; }
                }
                result.Add(cur);
            }
            return result;
        }

        private static int DressRun(Transform artRoot, Face face, List<Vector3Int> run, GridLayout grid,
                                    Vector3 cell, Kit kit, HashSet<Vector3Int> solid, TileRegionKit regionKit)
        {
            // 新素材是「一格一个方块」：四个朝向都用它 —— 每格一块，
            // 方块自带「粗糙的上边」，靠旋转让粗糙边朝外（地板朝上、天花板朝下、墙朝左右）。
            // 区域套件优先；这一片没配（或套件里没方块）就回退出生点石头。
            Sprite[] blocks = regionKit != null && regionKit.IsUsable ? regionKit.Blocks : kit.WallBlocks.ToArray();
            if (blocks.Length > 0)
            {
                return DressBlocks(artRoot, face, run, grid, cell, blocks, regionKit);
            }

            bool vertical = face == Face.Left || face == Face.Right;
            float span = (vertical ? cell.y : cell.x) * run.Count;

            var first = run[0];
            var last = run[run.Count - 1];
            Vector3 cellOrigin = grid.CellToWorld(first);
            Vector3 cursor;
            Quaternion rot = Quaternion.identity;
            switch (face)
            {
                case Face.Up:
                    // 把板面放进逻辑格，而不是贴在格子外侧。
                    cursor = new Vector3(cellOrigin.x, cellOrigin.y, 0f);
                    break;
                case Face.Down:
                    cursor = new Vector3(cellOrigin.x, cellOrigin.y, 0f);
                    break;
                case Face.Left:
                    // 旋转后的石片从锚点向左伸；锚在格子右边，覆盖当前格。
                    cursor = new Vector3(cellOrigin.x + cell.x, cellOrigin.y, 0f);
                    rot = Quaternion.Euler(0f, 0f, 90f);
                    break;
                default:
                    Vector3 top = grid.CellToWorld(last);
                    // 右墙从锚点向右伸；锚在格子左边，覆盖当前格。
                    cursor = new Vector3(cellOrigin.x, top.y + cell.y, 0f);
                    rot = Quaternion.Euler(0f, 0f, -90f);
                    break;
            }
            // The source sprite alpha fades farther at its top than at its bottom; orient its cleaner edge outward.
            bool flipY = face != Face.Down;

            int thinCount = 0;
            foreach (var c in run)
            {
                bool up = !solid.Contains(new Vector3Int(c.x, c.y + 1, 0));
                bool down = !solid.Contains(new Vector3Int(c.x, c.y - 1, 0));
                bool left = !solid.Contains(new Vector3Int(c.x - 1, c.y, 0));
                bool right = !solid.Contains(new Vector3Int(c.x + 1, c.y, 0));
                if ((up && down) || (left && right)) thinCount++;
            }
            bool thin = thinCount * 2 >= run.Count;

            var slabs = thin ? kit.SlabSlabs : kit.GroundSlabs;
            float avgSlab = slabs.Average(s => s.bounds.size.x);
            // 端帽带羽化外边，按需求一律改硬切：首尾也用中段石片，不再留下灰色雾边。
            float middle = span;
            if (middle < 1.5f)
            {
                var one = slabs.OrderBy(s => Mathf.Abs(s.bounds.size.x - span)).First();
                Place(artRoot, one, face, cursor, rot, flipY, false, span / one.bounds.size.x, cell);
                return 1;
            }

            int count = 0;
            int pairCount = Mathf.Max(1, Mathf.RoundToInt(middle / (avgSlab * 2f)));
            float half = middle * 0.5f;
            float baseW = half / pairCount;
            var pairW = new float[pairCount];
            float sum = 0f;
            var rng = new System.Random(20261014 + first.x * 31 + first.y * 17);
            for (int i = 0; i < pairCount; i++)
            {
                pairW[i] = Mathf.Max(avgSlab * 0.5f, baseW * (1f + (float)(rng.NextDouble() * 0.24 - 0.12)));
                sum += pairW[i];
            }
            float fix = half / sum;
            for (int i = 0; i < pairCount; i++) pairW[i] *= fix;

            for (int p = 0; p < pairCount; p++)
            {
                float w = pairW[p];
                var baseSlab = slabs.OrderBy(s => Mathf.Abs(s.bounds.size.x - w)).First();
                float scale = w / baseSlab.bounds.size.x;
                Place(artRoot, baseSlab, face, cursor, rot, flipY, false, scale, cell);
                cursor += rot * new Vector3(w, 0f, 0f);
                Place(artRoot, baseSlab, face, cursor, rot, flipY, true, scale, cell);
                cursor += rot * new Vector3(w, 0f, 0f);
                count += 2;
            }

            return count;
        }

        /// <summary>
        /// 每格一块方块（新素材 <c>Wall_Stone_Block_*</c>），四个朝向通用。
        /// </summary>
        /// <remarks>
        /// 方块自带「粗糙的上边」，靠旋转让粗糙边朝外：
        /// 地板 0°（朝上）、天花板 180°（朝下）、左墙 +90°、右墙 −90°。
        /// 旋转后贴图从锚点向另一侧伸展，所以锚点要放在格子对应的对角。
        /// </remarks>
        private static int DressBlocks(Transform artRoot, Face face, List<Vector3Int> run,
                                       GridLayout grid, Vector3 cell, Sprite[] blocks, TileRegionKit regionKit)
        {
            // 链式套件（积木王国）：沿「本段的方向」逐格递增，相邻两格拼起来颜色才连续。
            // 普通套件（石头）：按坐标哈希，一块块独立，不会撞出规律。
            bool chain = regionKit != null && regionKit.ChainVariants && regionKit.IsUsable;
            bool vertical = face == Face.Left || face == Face.Right;

            int count = 0;
            foreach (var c in run)
            {
                int variant = chain
                    ? regionKit.VariantIndex(c, vertical)
                    : VariantIndex(c, blocks.Length, regionKit != null ? regionKit.VariantSeed : 0);

                var sprite = blocks[variant % blocks.Length];
                Vector3 origin = grid.CellToWorld(c);

                // 旋转后贴图从锚点伸出的方向不同，锚点放在「对角」才能盖住这一格。
                Vector3 anchor;
                float rotZ;
                switch (face)
                {
                    case Face.Down:
                        anchor = new Vector3(origin.x + cell.x, origin.y + cell.y, 0f);   // 右上角
                        rotZ = 180f;
                        break;
                    case Face.Left:
                        anchor = new Vector3(origin.x + cell.x, origin.y, 0f);            // 右下角
                        rotZ = 90f;
                        break;
                    case Face.Right:
                        anchor = new Vector3(origin.x, origin.y + cell.y, 0f);            // 左上角
                        rotZ = -90f;
                        break;
                    default:                                                              // Face.Up
                        anchor = new Vector3(origin.x, origin.y, 0f);                     // 左下角
                        rotZ = 0f;
                        break;
                }

                var go = new GameObject(face + "_" + sprite.name);
                go.transform.SetParent(artRoot, false);
                go.transform.position = anchor;
                go.transform.rotation = Quaternion.Euler(0f, 0f, rotZ);

                // 按 sprite 实际尺寸归一化到一格：区域素材之间有 1~2 px 差异
                // （实测 64/65/66 宽都有），硬编码 0.5 会让墙缝逐段错开。
                Vector3 size = sprite.bounds.size;
                go.transform.localScale = new Vector3(
                    cell.x / Mathf.Max(size.x, 0.0001f),
                    cell.y / Mathf.Max(size.y, 0.0001f), 1f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = ArtSortingOrder;
                Undo.RegisterCreatedObjectUndo(go, "铺装美术");
                count++;
            }
            return count;
        }



        /// <summary>九宫格铺装：按「露哪条边」选块，实心格一格一块，内部自然被填满。</summary>
        /// <returns>这套图没有九宫格切分时返回 false，由调用方回退到「按暴露面」的老铺法。</returns>
        /// <remarks>
        /// <para>角色顺序与 <see cref="TileRegionKit.NineSlice"/> 一致：
        /// 0 左上 · 1 上 · 2 右上 · 3 柱顶 · 4 左 · 5 心 · 6 右 · 7 柱中 ·
        /// 8 左下 · 9 下 · 10 右下 · 11 柱底 · 12 平台左 · 13 平台中 · 14 平台右 · 15 单块。</para>
        /// <para>竖条（左右都没邻居）走立柱段，横条（上下都没邻居）走平台段，其余走九宫格 —— 和预览图同一套判断。</para>
        /// </remarks>
        public static bool DressNineSlice()
        {
            var selected = Selection.activeGameObject;
            if (selected == null) return false;
            var root = ResolveRoot(selected).gameObject;
            var tilemap = FindLogicTilemap(root);
            if (tilemap == null) return false;
            var region = TileRegion.FindFor(tilemap);
            if (region == null) return false;
            TileRegionKit defaultKit = region.DefaultKit;
            if (defaultKit == null || !defaultKit.HasNineSlice) return false;

            var hazards = tilemap.GetComponent<HazardField>();
            var secretWalls = tilemap.GetComponent<SecretWallField>();
            var solid = new HashSet<Vector3Int>();
            BoundsInt bounds = tilemap.cellBounds;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var p = new Vector3Int(x, y, 0);
                    var tile = tilemap.GetTile(p);
                    if (tile == null) continue;
                    if (hazards != null && hazards.IsHazardCell(p)) continue;
                    if (tile is OneWayTile) continue;
                    bool isSecret = secretWalls != null && secretWalls.IsSecretCell(p);
                    if (!isSecret && tile is Tile bare && bare.colliderType == Tile.ColliderType.None) continue;
                    solid.Add(p);
                }
            if (solid.Count == 0) return false;

            var artRoot = EnsureArtRoot(root);
            ClearChildren(artRoot);

            var grid = tilemap.layoutGrid;
            Vector3 cell = grid.cellSize;
            bool Has(int x, int y) => solid.Contains(new Vector3Int(x, y, 0));

            int count = 0;
            // 上万格逐格注册 Undo 会把撤销栈打爆（实测 Console 一串 overflow），这里只标脏场景。
            foreach (var p in solid)
            {
                var kit = region.ResolveKit(grid.CellToWorld(p));
                if (kit == null || !kit.HasNineSlice) kit = defaultKit;
                Sprite[] roles = kit.NineSlice;

                bool left = Has(p.x - 1, p.y), right = Has(p.x + 1, p.y);
                bool up = Has(p.x, p.y + 1), down = Has(p.x, p.y - 1);
                int role = NineSliceRole(left, right, up, down);
                var sprite = roles[role];
                if (sprite == null) continue;

                var go = new GameObject("Nine_" + sprite.name);
                go.transform.SetParent(artRoot, false);

                // 按精灵自身轴心落位：素材可能是左下轴心（旧方块）也可能居中（九宫格切图）。
                float pivotX = sprite.rect.width > 0f ? sprite.pivot.x / sprite.rect.width : 0f;
                float pivotY = sprite.rect.height > 0f ? sprite.pivot.y / sprite.rect.height : 0f;
                Vector3 min = grid.CellToWorld(p);
                go.transform.position = new Vector3(min.x + pivotX * cell.x, min.y + pivotY * cell.y, 0f);

                Vector3 size = sprite.bounds.size;
                go.transform.localScale = new Vector3(
                    cell.x / Mathf.Max(size.x, 0.0001f) * NineSliceOverlap,
                    cell.y / Mathf.Max(size.y, 0.0001f) * NineSliceOverlap, 1f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = ArtSortingOrder;
                count++;
            }
            // ⚠️ 九宫格只铺实心格；刺（Hazard 格）不是实心，走的是自己的那一趟。
            // 这里以前直接 return —— 于是九宫格套件的场景（出生点那个分区）**一格刺都不铺**。
            int spikes = DressSpikes(artRoot, hazards, tilemap, grid, cell,
                                     p => region.ResolveKit(grid.CellToWorld(p)), solid, LoadDefaultSpikes());

            // 白盒收尾：九宫格路径以前没关白盒，刺的红色占位会和美术叠在一起。
            var logicRenderer = tilemap.GetComponent<TilemapRenderer>();
            if (logicRenderer != null && logicRenderer.enabled)
            {
                logicRenderer.enabled = false;
                EditorUtility.SetDirty(logicRenderer);
            }
            var maskLayer = root.GetComponentInChildren<MaskLayer>(true);
            if (maskLayer != null)
            {
                maskLayer.SetEditorAlpha(1f);
                EditorUtility.SetDirty(maskLayer);
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(tilemap.gameObject.scene);
            Debug.Log($"[铺装] 九宫格铺了 {count} 格（套件：{defaultKit.DisplayName}），刺 {spikes} 个。白盒层已隐藏。");
            return true;
        }

        /// <summary>露边 → 角色下标（0..15）。</summary>
        private static int NineSliceRole(bool hasLeft, bool hasRight, bool hasUp, bool hasDown)
        {
            bool left = !hasLeft, right = !hasRight, up = !hasUp, down = !hasDown;
            if (left && right)
            {
                if (up && down) return 15;
                if (up) return 3;
                if (down) return 11;
                return 7;
            }
            if (up && down)
            {
                if (left) return 12;
                if (right) return 14;
                return 13;
            }
            if (up && left) return 0;
            if (up && right) return 2;
            if (down && left) return 8;
            if (down && right) return 10;
            if (up) return 1;
            if (down) return 9;
            if (left) return 4;
            if (right) return 6;
            return 5;
        }

        /// <summary>把**内部**的实心格补齐：外露面已经由上面那几条铺过了，这里只补「四面都被包住」的格子。</summary>
        /// <remarks>
        /// <para><b>为什么需要</b>：铺装是按「暴露面」走的，厚结构只有一圈外壳有美术，
        /// 里面是空的 —— 摄像机贴着墙看、或者墙被打穿时就会露出空洞。</para>
        /// <para><b>只补内部</b>：四面都有邻居的格子看不见，但正是它们让地形「实心」。
        /// 面的构件在上（<c>ArtSortingOrder</c>），补块压一层（<c>ArtSortingOrder - 1</c>）。</para>
        /// <para><b>可重复执行</b>：每次先把上一次的 <c>Fill_*</c> 清掉再补，不会叠。</para>
        /// </remarks>
        [MenuItem("Tools/规则游戏/铺装美术/⑨ 填内部（四面被包住的实心格）", false, 16)]
        public static void FillInteriorMenu()
        {
            var selected = Selection.activeGameObject;
            if (selected == null) { Debug.LogError("[铺装] 请先在 Hierarchy 里选中逻辑 Grid 物体。"); return; }
            var root = ResolveRoot(selected).gameObject;
            var tilemap = FindLogicTilemap(root);
            if (tilemap == null) { Debug.LogError("[铺装] 选中的物体下面找不到 Tilemap。"); return; }
            var kits = LoadKits();
            if (kits == null) return;
            var artRoot = EnsureArtRoot(root);
            int filled = FillInterior(artRoot, tilemap, kits);
            Debug.Log($"[铺装] 内部补齐 {filled} 格（外露面仍由 ①~⑤ 负责）。");
        }

        /// <summary>
        /// 只铺刺：按 <see cref="HazardField"/> 的危险格铺刺美术，**不动**其它已经铺好的墙皮。
        /// </summary>
        /// <remarks>
        /// <para>给「已经有美术、但刺还没铺」的场景用（魔王城复用开始场景的刺就是这条路径）。
        /// 整张重铺（菜单 ①）会清空 Art 层重新生成，手工修过的构件会被覆盖。</para>
        /// <para>场景没有 <see cref="HazardField"/> 时，只要逻辑层真的画了 <c>Spike_Logic</c>，
        /// 就按开始场景的参数补一个（伤害 10 / 击退 6 / 0.5 秒重复）—— 开始场景的刺之所以会扎人，
        /// 靠的就是这个组件，只铺图不补它等于铺了一层装饰。</para>
        /// </remarks>
        [MenuItem("Tools/规则游戏/铺装美术/⑩ 只铺刺（危险格，不改其它美术）", false, 17)]
        public static void DressSpikesOnly()
        {
            var selected = Selection.activeGameObject;
            if (selected == null) { Debug.LogError("[铺装] 请先在 Hierarchy 里选中逻辑 Grid 物体。"); return; }

            var root = ResolveRoot(selected).gameObject;
            var tilemap = FindLogicTilemap(root);
            if (tilemap == null) { Debug.LogError("[铺装] 选中的物体下面找不到 Tilemap。"); return; }

            var hazards = EnsureSpikeHazardField(tilemap);
            if (hazards == null)
            {
                Debug.LogWarning("[铺装] 逻辑层上没有 HazardField，也没画 " + SpikeTilePath + "，没有刺可铺。");
                return;
            }

            var region = TileRegion.FindFor(tilemap);
            var artRoot = EnsureArtRoot(root);

            // 只清上一次铺的刺，其它构件（墙皮 / 内部补块 / 跳穿平台）原样保留。
            for (int i = artRoot.childCount - 1; i >= 0; i--)
            {
                var child = artRoot.GetChild(i);
                if (child != null && child.name.StartsWith("Spike_", System.StringComparison.Ordinal))
                    Undo.DestroyObjectImmediate(child.gameObject);
            }

            var grid = tilemap.layoutGrid;
            Vector3 cell = grid.cellSize;
            var solid = CollectSolidCells(tilemap, hazards);
            var kits = LoadKits();
            if (kits == null) return;

            int count = DressSpikes(artRoot, hazards, tilemap, grid, cell,
                                    c => region != null ? region.ResolveKit(grid.CellToWorld(c)) : null,
                                    solid, kits.DefaultSpikes);

            EditorUtility.SetDirty(tilemap);
            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(tilemap.gameObject.scene);

            Debug.Log($"[铺装] 只铺刺完成：{count} 个构件" +
                      $"（区域套件：{(region != null && region.DefaultKit != null ? region.DefaultKit.DisplayName : "未配")}）。");
        }

        /// <summary>
        /// 逻辑层没有 <see cref="HazardField"/> 时，只要画了刺瓦片就按开始场景的参数补一个。
        /// </summary>
        private static HazardField EnsureSpikeHazardField(Tilemap tilemap)
        {
            var field = tilemap.GetComponent<HazardField>();
            if (field != null) return field;

            var spike = AssetDatabase.LoadAssetAtPath<TileBase>(SpikeTilePath);
            if (spike == null) return null;

            bool used = false;
            var bounds = tilemap.cellBounds;
            for (int x = bounds.xMin; x < bounds.xMax && !used; x++)
                for (int y = bounds.yMin; y < bounds.yMax && !used; y++)
                    used = tilemap.GetTile(new Vector3Int(x, y, 0)) == spike;

            if (!used) return null;

            var added = Undo.AddComponent<HazardField>(tilemap.gameObject);
            var so = new SerializedObject(added);
            var hazards = so.FindProperty("_hazards");
            hazards.arraySize = 1;
            var entry = hazards.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("Tile").objectReferenceValue = spike;
            entry.FindPropertyRelative("Damage").floatValue = SpikeDamage;
            entry.FindPropertyRelative("Knockback").floatValue = SpikeKnockback;
            entry.FindPropertyRelative("SelfArt").boolValue = false;
            so.FindProperty("_repeatInterval").floatValue = SpikeRepeatInterval;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(added);

            Debug.Log($"[铺装] 逻辑层没有 HazardField，已按开始场景的刺补齐：伤害 {SpikeDamage} / " +
                      $"击退 {SpikeKnockback} / {SpikeRepeatInterval} 秒重复。");
            return added;
        }

        /// <summary>
        /// 收集实心几何格（刺只用它判断地刺 / 顶刺）。
        /// </summary>
        /// <remarks>与九宫格铺装同一套口径：跳过刺、跳穿平台、没登记的 None 碰撞瓦片。</remarks>
        private static HashSet<Vector3Int> CollectSolidCells(Tilemap tilemap, HazardField hazards)
        {
            var secretWalls = tilemap.GetComponent<SecretWallField>();
            var solid = new HashSet<Vector3Int>();
            var bounds = tilemap.cellBounds;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var p = new Vector3Int(x, y, 0);
                    var tile = tilemap.GetTile(p);
                    if (tile == null) continue;
                    if (hazards != null && hazards.IsHazardCell(p)) continue;
                    if (tile is OneWayTile) continue;
                    bool isSecret = secretWalls != null && secretWalls.IsSecretCell(p);
                    if (!isSecret && tile is Tile bare && bare.colliderType == Tile.ColliderType.None) continue;
                    solid.Add(p);
                }
            return solid;
        }

        private static int FillInterior(Transform artRoot, Tilemap tilemap, Kit kits)
        {
            // 旧的补块先清掉，重复执行不会叠。
            for (int i = artRoot.childCount - 1; i >= 0; i--)
            {
                var child = artRoot.GetChild(i);
                if (child != null && child.name.StartsWith("Fill_", System.StringComparison.Ordinal)) Undo.DestroyObjectImmediate(child.gameObject);
            }

            var hazards = tilemap.GetComponent<HazardField>();
            var secretWalls = tilemap.GetComponent<SecretWallField>();
            var solid = new HashSet<Vector3Int>();
            var oneWay = new HashSet<Vector3Int>();

            BoundsInt bounds = tilemap.cellBounds;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var p = new Vector3Int(x, y, 0);
                    var tile = tilemap.GetTile(p);
                    if (tile == null) continue;
                    if (hazards != null && hazards.IsHazardCell(p)) continue;
                    if (tile is OneWayTile) { oneWay.Add(p); continue; }
                    bool isSecret = secretWalls != null && secretWalls.IsSecretCell(p);
                    if (!isSecret && tile is Tile bare && bare.colliderType == Tile.ColliderType.None) continue;
                    solid.Add(p);
                }

            var region = TileRegion.FindFor(tilemap);
            var grid = tilemap.layoutGrid;
            Vector3 cell = grid.cellSize;

            bool Has(int x, int y) => solid.Contains(new Vector3Int(x, y, 0));

            int count = 0;
            foreach (var p in solid)
            {
                // 只补「四面都被包住」的：有外露面的格子交给 ①~⑤ 的构件，补块会露在下面顶着。
                if (!Has(p.x - 1, p.y) || !Has(p.x + 1, p.y) || !Has(p.x, p.y - 1) || !Has(p.x, p.y + 1)) continue;

                var regionKit = region != null ? region.ResolveKit(grid.CellToWorld(p)) : null;
                Sprite[] blocks = regionKit != null && regionKit.IsUsable ? regionKit.Blocks : kits.WallBlocks.ToArray();
                if (blocks == null || blocks.Length == 0) continue;

                int variant = VariantIndex(p, blocks.Length, regionKit != null ? regionKit.VariantSeed : 0);
                var sprite = blocks[variant % blocks.Length];

                var go = new GameObject("Fill_" + sprite.name);
                go.transform.SetParent(artRoot, false);
                go.transform.position = grid.CellToWorld(p);
                Vector3 size = sprite.bounds.size;
                go.transform.localScale = new Vector3(
                    cell.x / Mathf.Max(size.x, 0.0001f),
                    cell.y / Mathf.Max(size.y, 0.0001f), 1f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = ArtSortingOrder - 1;
                Undo.RegisterCreatedObjectUndo(go, "铺装美术");
                count++;
            }
            return count;
        }

        /// <summary>
        /// 逻辑层里有跳穿瓦片时，确保挂了 <see cref="OneWayField"/>。
        /// </summary>
        /// <remarks>
        /// 跳穿瓦片的 Collider Type 是 None（碰撞必须逐格生成），所以少了这个组件，
        /// 画上去的平台在运行时就是**没有碰撞的装饰**。
        /// </remarks>
        private static void EnsureOneWayField(Tilemap logic)
        {
            BoundsInt bounds = logic.cellBounds;
            bool any = false;
            for (int x = bounds.xMin; x < bounds.xMax && !any; x++)
            {
                for (int y = bounds.yMin; y < bounds.yMax && !any; y++)
                {
                    if (logic.GetTile(new Vector3Int(x, y, 0)) is OneWayTile) any = true;
                }
            }

            if (!any) return;
            if (logic.GetComponent<OneWayField>() != null) return;

            var field = Undo.AddComponent<OneWayField>(logic.gameObject);
            EditorUtility.SetDirty(field);
            Debug.Log("[铺装] 逻辑层里有跳穿平台瓦片，已自动补上 OneWayField（运行时逐格生成单向碰撞体）。");
        }

        /// <summary>
        /// 建（或复用）冰面层：单独一张 Tilemap + 自带 0 摩擦物理材质的碰撞体。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么冰面不能和地面同层</b>：摩擦是**碰撞体**的属性，而一整张 Tilemap
        /// 只有一个碰撞体 —— 同层就没法逐格设摩擦，物理驱动的物体（石头、投掷物）踩上去也不会打滑。</para>
        /// <para>冰面层自己渲染、自己有碰撞体，所以逻辑层那一格要**空出来**（由
        /// <see cref="MigrateIceCells"/> 负责），高度才不会多出半格。</para>
        /// </remarks>
        private static Tilemap EnsureIceLayer(GameObject root, Tilemap logic)
        {
            var existing = root.transform.Find(IceChildName);
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = new GameObject(IceChildName);
                go.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(go, "建冰面层");
            }

            // ⚠️ AddComponent<Tilemap>() **不会**自动带上 TilemapRenderer（没有 RequireComponent），
            // 少了渲染器冰面就是隐形的 —— 所以这里显式补齐，老的层也能被修好。
            var map = go.GetComponent<Tilemap>() ?? go.AddComponent<Tilemap>();

            // ⚠️ 冰面层必须和**逻辑层同层**（Ground 8）：玩家的位移解算（Rigidbody2D.Slide）
            // 和接地探测都只扫这个层。层不对 → 冰面「看得见、踩空」，而且 Console 一句错都不报。
            // 这里同时修老数据：早先新建冰面层时没设层，一直留在 Default(0)。
            int wanted = logic.gameObject.layer;
            if (go.layer != wanted)
            {
                int old = go.layer;
                go.layer = wanted;
                EditorUtility.SetDirty(go);
                Debug.Log($"[铺装] 冰面层层级修正：{LayerMask.LayerToName(old)} → " +
                          $"{LayerMask.LayerToName(wanted)}（原来那一层玩家扫不到，冰面会踩空）");
            }

            var renderer = go.GetComponent<TilemapRenderer>();
            if (renderer == null) renderer = go.AddComponent<TilemapRenderer>();
            var logicRenderer = logic.GetComponent<TilemapRenderer>();
            renderer.sortingLayerID = logicRenderer != null ? logicRenderer.sortingLayerID : 0;
            renderer.sortingOrder = IceSortingOrder;        // 逻辑层 −2、美术 1：冰面夹在中间
            renderer.enabled = true;

            var collider = go.GetComponent<TilemapCollider2D>();
            if (collider == null) collider = go.AddComponent<TilemapCollider2D>();

            // 编辑器里没有帧循环，TilemapCollider2D 不会自己重建形状 ——
            // 「形状没建好」和「层不对」的症状都是踩空，两个都主动兜一遍。
            if (!Application.isPlaying)
            {
                collider.enabled = false;
                collider.enabled = true;
            }

            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(IceMaterialPath);
            if (material != null)
            {
                collider.sharedMaterial = material;
            }
            else
            {
                Debug.LogWarning($"[铺装] 找不到冰面物理材质 {IceMaterialPath}，" +
                                 "冰面会按引擎默认摩擦处理（玩家仍会打滑，但物理物体不会）。");
            }

            return map;
        }

        /// <summary>
        /// 把逻辑层里的表面瓦片（冰面）搬到冰面层：**同一格、高度不变**，碰撞交给冰面层。
        /// </summary>
        private static int MigrateIceCells(Tilemap logic, Tilemap ice)
        {
            if (ice == null || ice == logic) return 0;

            var bounds = logic.cellBounds;
            int moved = 0;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    var tile = logic.GetTile(c);
                    if (!(tile is SurfaceTile)) continue;

                    ice.SetTile(c, tile);
                    logic.SetTile(c, null);
                    moved++;
                }

            if (moved > 0)
            {
                EditorUtility.SetDirty(logic);
                EditorUtility.SetDirty(ice);
                ice.RefreshAllTiles();

                // 编辑器里没有帧循环，TilemapCollider2D 不会自己重建形状（冰面会"看得见但踩空"）。
                // 关一下再开，逼它重算。
                var iceCollider = ice.GetComponent<TilemapCollider2D>();
                if (iceCollider != null)
                {
                    iceCollider.enabled = false;
                    iceCollider.enabled = true;
                }
            }
            return moved;
        }

        /// <summary>
        /// 铺跳穿平台：用所在区域套件的方块，但**染上瓦片自己的颜色**。
        /// </summary>
        /// <remarks>
        /// 这样它能和地形一样按格对齐、一样吃区域美术，只是颜色不同 ——
        /// 玩家一眼就知道「这块能穿过去」，而不用为它单独准备一套素材。
        /// 改颜色 = 改瓦片资产上的 <c>_tint</c>。
        /// </remarks>
        private static int DressOneWay(Transform artRoot, HashSet<Vector3Int> cells, Tilemap tilemap,
                                       GridLayout grid, Vector3 cell, Kit kit,
                                       System.Func<Vector3Int, TileRegionKit> kitAt)
        {
            if (cells.Count == 0) return 0;

            int count = 0;
            foreach (var c in cells)
            {
                if (!(tilemap.GetTile(c) is OneWayTile tile)) continue;

                // 每一格各自查区域套件：一块跳穿平台可能横跨两个王国。
                TileRegionKit regionKit = kitAt(c);
                Sprite[] blocks = regionKit != null && regionKit.IsUsable ? regionKit.Blocks : kit.WallBlocks.ToArray();
                if (blocks.Length == 0) continue;

                int seed = regionKit != null ? regionKit.VariantSeed : 0;
                var sprite = blocks[VariantIndex(c, blocks.Length, seed)];
                var go = new GameObject("OneWay_" + sprite.name);
                go.transform.SetParent(artRoot, false);
                go.transform.position = grid.CellToWorld(c);        // 左下角对齐，和普通方块一致

                Vector3 size = sprite.bounds.size;
                go.transform.localScale = new Vector3(
                    cell.x / Mathf.Max(size.x, 0.0001f),
                    cell.y / Mathf.Max(size.y, 0.0001f), 1f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.color = tile.Tint;                               // ← 染色：一眼看出能跳穿
                sr.sortingOrder = ArtSortingOrder + 1;              // 压在地形之上，别被盖住
                Undo.RegisterCreatedObjectUndo(go, "铺装美术");
                count++;
            }
            return count;
        }

        /// <summary>
        /// 给刺（<see cref="HazardField"/> 的格子）铺美术：一格一张，取自所在区域套件的 <see cref="TileRegionKit.Spike"/>。
        /// </summary>
        /// <remarks>
        /// <para>刺不是几何（不参与外露面判断），所以单独一趟处理：铺完关掉白盒 Renderer 之后，
        /// 这些格子就只剩这里的图。</para>
        /// <para>套件没配 <c>Spike</c> 的区域**一格都不铺** —— 保持逻辑瓦片原样，不会突然多出方块。</para>
        /// </remarks>
        /// <summary>
        /// 解锁瓦片的「占用颜色」标记：锁着的时候 <c>Tilemap.SetColor</c> 是**静默无效**的。
        /// </summary>
        /// <remarks>会写到瓦片资产上（全项目共用），但只是放开一个开关，不影响画法。</remarks>
        private static void UnlockTileColor(TileBase tile)
        {
            if (!(tile is Tile t)) return;
            if ((t.flags & TileFlags.LockColor) == 0) return;

            Undo.RecordObject(t, "刺：解锁瓦片颜色");
            t.flags &= ~TileFlags.LockColor;
            EditorUtility.SetDirty(t);
        }

        private static int DressSpikes(Transform artRoot, HazardField hazards, Tilemap tilemap,
                                       GridLayout grid, Vector3 cell,
                                       System.Func<Vector3Int, TileRegionKit> kitAt,
                                       HashSet<Vector3Int> solid, List<Sprite> defaultSpikes)
        {
            if (hazards == null) return 0;

            int count = 0;
            var bounds = tilemap.cellBounds;

            // 先把所有刺格的颜色复位（重铺时上一次隐藏的要还原），再按需隐藏。
            Undo.RecordObject(tilemap, "刺：隐藏原瓦片");
            for (int x = bounds.xMin; x < bounds.xMax; x++)
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var rc = new Vector3Int(x, y, 0);
                    if (tilemap.GetTile(rc) == null || !hazards.IsHazardCell(rc)) continue;
                    UnlockTileColor(tilemap.GetTile(rc));
                    tilemap.SetColor(rc, Color.white);
                }

            for (int x = bounds.xMin; x < bounds.xMax; x++)
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    if (tilemap.GetTile(c) == null) continue;
                    if (!hazards.IsHazardCell(c)) continue;
                    // 齿轮这类自带贴图的危险物：逻辑瓦片本身就是最终画面，盖刺会把它挡住。
                    if (hazards.IsSelfArtCell(c)) continue;

                    var regionKit = kitAt(c);
                    var sprite = regionKit != null ? regionKit.Spike : null;

                    // 区域套件没配刺 → 用**默认刺**（荆棘玫瑰花丛，开始场景那套）；
                    // 都没有才跳过（保持逻辑瓦片原样）。
                    if (sprite == null && defaultSpikes != null && defaultSpikes.Count > 0)
                        sprite = defaultSpikes[VariantIndex(c, defaultSpikes.Count, 0)];
                    if (sprite == null) continue;

                    var go = new GameObject("Spike_" + sprite.name);
                    go.transform.SetParent(artRoot, false);

                    // 刺图的轴心不统一：荆棘是「底边中轴」，新交付的毛绒 / 发条刺是「中心」。
                    // 直接按格子左下角放，中心轴心的图会偏出半格，所以先把 pivot 换算成世界偏移。
                    Vector3 size = sprite.bounds.size;
                    Vector3 scale = new Vector3(
                        cell.x / Mathf.Max(size.x, 0.0001f),
                        cell.y / Mathf.Max(size.y, 0.0001f), 1f);
                    Vector2 pivot = sprite.pivot / Mathf.Max(sprite.pixelsPerUnit, 0.0001f);
                    go.transform.position = grid.CellToWorld(c)                       // 左下角对齐
                                            + new Vector3(pivot.x * scale.x, pivot.y * scale.y, 0f);
                    go.transform.localScale = scale;

                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = sprite;
                    sr.sortingOrder = ArtSortingOrder + 1;           // 压在地形之上

                    // 挂在天花板上的刺：凸起要朝**下**（贴地时才朝上）。
                    // 判据看这一格的实心支撑在哪：下方有实心 → 地刺；否则上方有实心 → 顶刺。
                    bool onCeiling = !solid.Contains(new Vector3Int(c.x, c.y - 1, 0))
                                     && solid.Contains(new Vector3Int(c.x, c.y + 1, 0));
                    sr.flipY = onCeiling;

                    // ⚠️ flipY 是**绕 pivot** 翻的：pivot 在底边时整体会掉一个图高，
                    // 必须补回来，否则图跑到格子下面（看起来就是「没铺到逻辑层上」）。
                    // 通用公式：翻完要上移 h - 2d（h=图高、d=pivot 距底），pivot 居中时正好不用补。
                    if (onCeiling)
                    {
                        float h = sprite.bounds.size.y * go.transform.localScale.y;
                        float d = sprite.pivot.y / Mathf.Max(sprite.pixelsPerUnit, 0.0001f)
                                  * go.transform.localScale.y;
                        go.transform.position += Vector3.up * (h - 2f * d);
                    }

                    // 原瓦片（红色占位）设成全透明：美术压在上面时不能两层叠着。
                    // ⚠️ 只改颜色，**瓦片本身和碰撞都不动** —— 刺的伤害逻辑还在 HazardField 上。
                    UnlockTileColor(tilemap.GetTile(c));
                    tilemap.SetColor(c, new Color(1f, 1f, 1f, 0f));

                    Undo.RegisterCreatedObjectUndo(go, "铺装美术");
                    count++;
                }

            EditorUtility.SetDirty(tilemap);
            return count;
        }

        /// <summary>
        /// 把表面瓦片（冰面等）自己的贴图搬到美术层。
        /// </summary>
        /// <remarks>
        /// 这类瓦片**不参与铺装**（铺装器跳过它们），贴图就是最终画面 ——
        /// 但铺完会把白盒层的 Renderer 关掉，不搬一份的话它们就跟着一起消失了。
        /// </remarks>
        private static int DressSurfaces(Transform artRoot, HashSet<Vector3Int> surfaces,
                                         Tilemap tilemap, GridLayout grid, Vector3 cell)
        {
            int count = 0;
            foreach (var c in surfaces)
            {
                var sprite = tilemap.GetTile(c) is Tile plain ? plain.sprite : null;
                if (sprite == null) continue;

                var go = new GameObject("Surface_" + tilemap.GetTile(c).name);
                go.transform.SetParent(artRoot, false);
                // 瓦片贴图用中心轴心，所以对齐到格子中心
                go.transform.position = grid.CellToWorld(c) + new Vector3(cell.x * 0.5f, cell.y * 0.5f, 0f);
                go.transform.localScale = Vector3.one;

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = ArtSortingOrder + 1;   // 压在石头墙皮之上：它就是地面最表面
                Undo.RegisterCreatedObjectUndo(go, "铺装美术");
                count++;
            }
            return count;
        }

        /// <summary>按格子坐标稳定地挑一个变体（同一格每次重铺都挑到同一块）。</summary>
        /// <remarks><paramref name="seed"/> 来自区域套件：让相邻区域即使格子坐标相同也不排出一样的图。</remarks>
        private static int VariantIndex(Vector3Int c, int variants, int seed)
        {
            int h = (c.x + seed) * 73856093 ^ (c.y - seed) * 19349663;
            h = (h ^ (h >> 13)) & 0x7fffffff;
            return h % variants;
        }

        private static void Place(Transform parent, Sprite sprite, Face face, Vector3 anchor,
                                  Quaternion rot, bool flipY, bool mirror, float scale, Vector3 cellSize)
        {
            var go = new GameObject(face + "_" + sprite.name);
            go.transform.SetParent(parent, false);
            float w = sprite.bounds.size.x * scale;
            Vector3 pos = anchor + rot * new Vector3(mirror ? w : 0f, 0f, 0f);
            go.transform.position = pos;
            go.transform.rotation = rot;
            float faceThickness = face == Face.Left || face == Face.Right ? cellSize.x : cellSize.y;
            float thicknessScale = faceThickness / sprite.bounds.size.y;
            go.transform.localScale = new Vector3(scale, thicknessScale, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = ArtSortingOrder;
            sr.flipX = mirror;
            sr.flipY = flipY;
            Undo.RegisterCreatedObjectUndo(go, "铺装美术");
        }

        private class Kit
        {
            public Sprite GroundCapLeft, GroundCapRight, GroundShort;
            public List<Sprite> GroundSlabs;
            public Sprite SlabCapLeft, SlabCapRight, SlabShort;
            public List<Sprite> SlabSlabs;
            /// <summary>墙面方块（新素材，一格一块）。空则退回旧的石片逻辑。</summary>
            public List<Sprite> WallBlocks;
            /// <summary>默认刺（区域套件没配 Spike 时用它）。</summary>
            public List<Sprite> DefaultSpikes;
        }

        /// <summary>
        /// 默认刺 = 荆棘玫瑰花丛（本地文档/功能组件-通用 交付图，已导入 Art/Sprites/Tiles/Hazards）。
        /// </summary>
        /// <remarks>九宫格路径与老路径**都要用**它，所以抽出来共享。</remarks>
        private static List<Sprite> LoadDefaultSpikes()
        {
            var list = new List<Sprite>();
            for (int i = 1; i <= 4; i++)
            {
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(
                    $"Assets/_Project/Art/Sprites/Tiles/Hazards/ThornBush_{i:00}.png");
                if (s != null) list.Add(s);
            }
            return list;
        }

        private static Kit LoadKits()
        {
            Sprite L(string n) => AssetDatabase.LoadAssetAtPath<Sprite>(StoneFolder + n + ".png");
            var kit = new Kit
            {
                GroundCapLeft = L("Ground_Stone_CapLeft"),
                GroundCapRight = L("Ground_Stone_CapRight"),
                GroundShort = L("Ground_Stone_Short_01"),
                GroundSlabs = new List<Sprite>(),
                SlabCapLeft = L("Platform_Stone_CapLeft"),
                SlabCapRight = L("Platform_Stone_CapRight"),
                SlabShort = L("Platform_Stone_Short_01"),
                SlabSlabs = new List<Sprite>(),
                WallBlocks = new List<Sprite>(),
                DefaultSpikes = new List<Sprite>(),
            };
            kit.DefaultSpikes.AddRange(LoadDefaultSpikes());
            for (int i = 1; i <= 16; i++)
            {
                var b = L($"Spawn/Wall_Stone_Block_{i:00}");
                if (b != null) kit.WallBlocks.Add(b);
            }
            for (int i = 4; i <= 8; i++)
            {
                var s = L($"Ground_Stone_Slab_{i:00}");
                if (s != null) kit.GroundSlabs.Add(s);
            }
            var p3 = L("Platform_Stone_Slab_03");
            if (p3 != null) kit.SlabSlabs.Add(p3);
            if (kit.GroundCapLeft == null || kit.GroundCapRight == null || kit.GroundSlabs.Count == 0
                || kit.SlabCapLeft == null || kit.SlabCapRight == null || kit.SlabSlabs.Count == 0)
            {
                Debug.LogError("[铺装] 石块构件贴图没找全，检查 " + StoneFolder);
                return null;
            }
            return kit;
        }

        /// <summary>
        /// 把 Hierarchy 里选中的东西统一解析成「逻辑 Grid」的 Transform。
        /// </summary>
        /// <remarks>
        /// 菜单里选中的可能是 Grid，也可能是它下面的 Logic / Mask / Art。
        /// 以前「清空美术」只找选中物体**直接**的子物体 Art，选中 Logic 时就静默什么都不做 —— 看着像功能坏了。
        /// </remarks>
        private static Transform ResolveRoot(GameObject selected)
        {
            var grid = selected.GetComponentInParent<UnityEngine.Grid>();
            return grid != null ? grid.transform : selected.transform;
        }

        private static Transform EnsureArtRoot(GameObject root)
        {
            var t = root.transform.Find(ArtChildName);
            if (t != null) return t;
            var go = new GameObject(ArtChildName);
            go.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "创建 Art 子物体");
            return go.transform;
        }

        private static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Undo.DestroyObjectImmediate(t.GetChild(i).gameObject);
        }

        private static Tilemap FindLogicTilemap(GameObject root)
        {
            var maps = root.GetComponentsInChildren<Tilemap>(true);
            if (maps.Length == 0) return null;
            var named = maps.FirstOrDefault(m => m.gameObject.name.ToLowerInvariant().Contains("logic"));
            return named != null ? named : maps[0];
        }
    }
}
