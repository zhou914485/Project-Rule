using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 在**当前场景**里整批互换两种危险瓦片：红色刺（<c>Spike_Logic</c>）↔ 齿轮（<c>GearTrap_Logic</c>）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是菜单而不是改文件</b>：场景正开在 Unity 里，外部改磁盘会被编辑器写回覆盖。
    /// 这个工具走 Tilemap API，改完记 Undo、标脏场景，Ctrl+Z 能撤销。</para>
    ///
    /// <para><b>两个方向都能走</b>：⑦ 刺 → 齿轮，⑦b 齿轮 → 刺。换回去时
    /// <c>HazardField</c> 的那一条会跟着改回刺、关掉「自带美术」（让铺装器重新铺荆棘），
    /// 自转组件里的齿轮瓦片也会摘掉。</para>
    ///
    /// <para><b>顺手做的</b>：换成齿轮时给 Tilemap 挂上 <see cref="GearTileSpinner"/> 让它转。</para>
    ///
    /// <para>⚠️ 不自动保存场景 —— 先看一眼，确认无误再 Ctrl+S。</para>
    /// </remarks>
    public static class GearTileReplace
    {
        private const string SpikeTilePath = "Assets/_Project/Art/Tiles/Spike_Logic.asset";
        private const string GearTilePath = "Assets/_Project/Art/Tiles/GearTrap_Logic.asset";

        [MenuItem("Tools/规则游戏/关卡瓦片/⑦ 把红色刺瓦片换成齿轮（当前场景）", false, 46)]
        public static void SpikesToGears() =>
            Replace(SpikeTilePath, GearTilePath, gear: true, "刺换齿轮");

        [MenuItem("Tools/规则游戏/关卡瓦片/⑦b 把齿轮换回红色刺（当前场景）", false, 47)]
        public static void GearsToSpikes() =>
            Replace(GearTilePath, SpikeTilePath, gear: false, "齿轮换回刺");

        /// <summary>
        /// <paramref name="gear"/> = true 表示换**成**齿轮（挂自转、打开自带美术）；
        /// false 表示换**回**刺（摘掉自转、关掉自带美术）。
        /// </summary>
        private static void Replace(string fromPath, string toPath, bool gear, string undoName)
        {
            var from = AssetDatabase.LoadAssetAtPath<TileBase>(fromPath);
            var to = AssetDatabase.LoadAssetAtPath<TileBase>(toPath);

            if (from == null || to == null)
            {
                Debug.LogError("[瓦片互换] 找不到瓦片资产：\n" + fromPath + "\n" + toPath);
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[瓦片互换] 当前没有打开的场景。");
                return;
            }

            int replaced = 0;
            int maps = 0;
            var touched = new List<Tilemap>();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Tilemap map in root.GetComponentsInChildren<Tilemap>(true))
                {
                    int count = ReplaceIn(map, from, to, undoName);
                    if (count <= 0) continue;

                    replaced += count;
                    maps++;
                    touched.Add(map);
                    EditorUtility.SetDirty(map);
                }
            }

            if (replaced == 0)
            {
                Debug.Log($"[瓦片互换] 场景里没有 {fromPath} 铺的格子，什么都没改。");
                return;
            }

            int updated = UpdateHazardFields(scene, from, to, gear, undoName);
            int spinners = UpdateSpinners(touched, to, gear, undoName);

            EditorSceneManager.MarkSceneDirty(scene);

            string title = gear ? "刺 → 齿轮" : "齿轮 → 刺";

            Debug.Log($"[瓦片互换] {title}：{replaced} 格，{maps} 张 Tilemap。\n" +
                      $"· HazardField 配置更新 {updated} 处\n" +
                      (gear
                          ? $"· 自转组件：{spinners} 张 Tilemap 已接上（120°/s）\n"
                          : $"· 自转组件：{spinners} 张 Tilemap 已摘掉齿轮\n") +
                      "· ⚠️ 没有自动保存 —— 确认后自己 Ctrl+S\n" +
                      "· 如果之前用铺装器在这格上盖过美术（Spike_* 子物体），需要手动处理那一层");
        }

        /// <summary>替换一张 Tilemap 里的目标格。返回换了几格。</summary>
        private static int ReplaceIn(Tilemap map, TileBase from, TileBase to, string undoName)
        {
            BoundsInt bounds = map.cellBounds;
            int count = 0;

            Undo.RecordObject(map, undoName);

            foreach (Vector3Int cell in bounds.allPositionsWithin)
            {
                if (map.GetTile(cell) != from) continue;

                map.SetTile(cell, to);

                // 刺格平时被染成红色占位、铺装后又会被设成透明；换过之后交还给瓦片资产自己的颜色。
                map.SetColor(cell, Color.white);
                count++;
            }

            return count;
        }

        /// <summary>
        /// 把 HazardField 里对应的那一条原地改成另一种瓦片。
        /// 换回刺时把「自带美术」关掉，铺装器才会重新给它铺荆棘。
        /// </summary>
        private static int UpdateHazardFields(Scene scene, TileBase from, TileBase to,
                                              bool gear, string undoName)
        {
            int updated = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (HazardField field in root.GetComponentsInChildren<HazardField>(true))
                {
                    var serialized = new SerializedObject(field);
                    SerializedProperty hazards = serialized.FindProperty("_hazards");
                    if (hazards == null) continue;

                    bool changed = false;

                    for (int i = 0; i < hazards.arraySize; i++)
                    {
                        SerializedProperty entry = hazards.GetArrayElementAtIndex(i);
                        SerializedProperty tile = entry.FindPropertyRelative("Tile");
                        if (tile.objectReferenceValue != from) continue;

                        Undo.RecordObject(field, undoName);
                        tile.objectReferenceValue = to;
                        entry.FindPropertyRelative("SelfArt").boolValue = gear;
                        changed = true;
                        updated++;
                    }

                    if (!changed) continue;

                    serialized.ApplyModifiedProperties();
                    EditorUtility.SetDirty(field);
                }
            }

            return updated;
        }

        /// <summary>
        /// 换齿轮：挂上自转组件并加入齿轮瓦片；换回刺：从自转列表里摘掉齿轮。
        /// 组件本身留着（清空列表 = 不转，无害）。
        /// </summary>
        private static int UpdateSpinners(List<Tilemap> maps, TileBase gearTile,
                                          bool gear, string undoName)
        {
            int count = 0;

            for (int i = 0; i < maps.Count; i++)
            {
                Tilemap map = maps[i];
                if (map == null) continue;

                var spinner = map.GetComponent<GearTileSpinner>();

                if (gear)
                {
                    if (spinner == null)
                        spinner = Undo.AddComponent<GearTileSpinner>(map.gameObject);

                    if (!SetSpinnerTile(spinner, gearTile, true, undoName)) continue;
                }
                else
                {
                    if (spinner == null) continue;

                    Undo.RecordObject(spinner, undoName);
                    if (!SetSpinnerTile(spinner, gearTile, false, undoName)) continue;
                }

                EditorUtility.SetDirty(spinner);
                count++;
            }

            return count;
        }

        /// <summary>把一张瓦片加进 / 移出自转列表。返回是否真的改了。</summary>
        private static bool SetSpinnerTile(GearTileSpinner spinner, TileBase tile,
                                           bool add, string undoName)
        {
            var serialized = new SerializedObject(spinner);
            SerializedProperty tiles = serialized.FindProperty("_spinTiles");
            if (tiles == null) return false;

            int index = -1;
            for (int i = 0; i < tiles.arraySize; i++)
            {
                if (tiles.GetArrayElementAtIndex(i).objectReferenceValue != tile) continue;
                index = i;
                break;
            }

            if (add)
            {
                if (index >= 0) return false;   // 已经有了

                Undo.RecordObject(spinner, undoName);
                tiles.arraySize++;
                tiles.GetArrayElementAtIndex(tiles.arraySize - 1).objectReferenceValue = tile;
            }
            else
            {
                if (index < 0) return false;    // 本来就没有

                Undo.RecordObject(spinner, undoName);
                tiles.DeleteArrayElementAtIndex(index);

                // 删除可能只是把引用置空，再删一次才是真正缩短数组。
                if (tiles.arraySize > 0
                    && tiles.GetArrayElementAtIndex(index < tiles.arraySize ? index : tiles.arraySize - 1)
                            .objectReferenceValue == null)
                {
                    tiles.DeleteArrayElementAtIndex(index < tiles.arraySize ? index : tiles.arraySize - 1);
                }
            }

            serialized.ApplyModifiedProperties();
            return true;
        }
    }
}
