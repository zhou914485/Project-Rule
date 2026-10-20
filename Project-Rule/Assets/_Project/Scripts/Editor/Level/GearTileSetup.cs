using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 把**危险齿轮瓦片**登记进当前打开场景里每个 <see cref="HazardField"/> 的危险瓦片表，
    /// 并顺手把 <see cref="GearTileSpinner"/> 挂到同一个 Tilemap 上让它转起来。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要这一步</b>：危险瓦片的伤害是按 <c>Tile</c> 资产匹配的
    /// （<see cref="HazardField"/> 的 <c>_hazards</c> 数组），铺上瓦片不等于会扎人 ——
    /// 没登记的格子只是纯贴图。</para>
    ///
    /// <para><b>幂等</b>：已经登记过的只校正成默认值（伤害 10 / 击退 6 / 自带美术），
    /// 不会重复追加条目；自转组件已经有了就只补瓦片列表。重复点菜单是安全的。</para>
    ///
    /// <para><b>自带美术</b>：登记时会把 <c>SelfArt</c> 打开 —— 齿轮是拿着贴图当最终画面的，
    /// 铺装器（<see cref="GroundArtDresser"/>）因此不会在这格上再盖一层荆棘。</para>
    ///
    /// <para>用法：先在 Logic 层的 Tilemap 上把齿轮瓦片铺好，再点这个菜单，最后保存场景。</para>
    /// </remarks>
    public static class GearTileSetup
    {
        /// <summary>齿轮瓦片资产（<c>Art/Tiles/GearTrap_Logic.asset</c>）。</summary>
        public const string TilePath = "Assets/_Project/Art/Tiles/GearTrap_Logic.asset";

        private const float DefaultDamage = 10f;
        private const float DefaultKnockback = 6f;

        private enum Result { Unchanged, Added, Updated }

        [MenuItem("Tools/规则游戏/关卡瓦片/⑥ 把齿轮瓦片登记进本场景的 HazardField", false, 45)]
        public static void Register()
        {
            var tile = AssetDatabase.LoadAssetAtPath<TileBase>(TilePath);
            if (tile == null)
            {
                Debug.LogError("[齿轮瓦片] 找不到 " + TilePath);
                return;
            }

            int fields = 0, added = 0, updated = 0;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                bool sceneChanged = false;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (HazardField field in root.GetComponentsInChildren<HazardField>(true))
                    {
                        fields++;

                        Result result = RegisterOn(field, tile);
                        if (result == Result.Added) { added++; sceneChanged = true; }
                        else if (result == Result.Updated) { updated++; sceneChanged = true; }

                        if (EnsureSpinner(field, tile)) sceneChanged = true;
                    }
                }

                // 只标脏真的改过的场景 —— 多场景打开时别把别的场景一起弄脏。
                if (sceneChanged) EditorSceneManager.MarkSceneDirty(scene);
            }

            if (fields == 0)
            {
                Debug.LogWarning("[齿轮瓦片] 当前打开的场景里没有 HazardField —— " +
                                 "它挂在 Logic 层的 Tilemap 上，先确认场景里有没有这一层。");
                return;
            }

            Debug.Log($"[齿轮瓦片] 检查了 {fields} 个 HazardField：新增 {added} 条，" +
                      $"校正 {updated} 条。齿轮现在会扎人了（伤害 {DefaultDamage} / 击退 {DefaultKnockback}）。\n" +
                      "· 自转：已给 Logic 层的 Tilemap 挂上 GearTileSpinner（转速默认 120°/s）\n" +
                      "· 别忘了保存场景\n" +
                      "· 瓦片铺在 Logic 层；齿轮格子自带美术，铺装美术时不会被荆棘盖住");
        }

        private static Result RegisterOn(HazardField field, TileBase tile)
        {
            var serialized = new SerializedObject(field);

            SerializedProperty hazards = serialized.FindProperty("_hazards");
            if (hazards == null)
            {
                Debug.LogWarning("[齿轮瓦片] HazardField 上找不到 _hazards 字段，跳过。", field);
                return Result.Unchanged;
            }

            // 已经登记过：只校正数值与自带美术标记，不追加重复条目。
            for (int i = 0; i < hazards.arraySize; i++)
            {
                SerializedProperty entry = hazards.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("Tile").objectReferenceValue != tile) continue;

                SerializedProperty damage = entry.FindPropertyRelative("Damage");
                SerializedProperty knockback = entry.FindPropertyRelative("Knockback");
                SerializedProperty selfArt = entry.FindPropertyRelative("SelfArt");

                if (Mathf.Approximately(damage.floatValue, DefaultDamage)
                    && Mathf.Approximately(knockback.floatValue, DefaultKnockback)
                    && selfArt.boolValue)
                {
                    return Result.Unchanged;
                }

                Undo.RecordObject(field, "登记齿轮瓦片");
                damage.floatValue = DefaultDamage;
                knockback.floatValue = DefaultKnockback;
                selfArt.boolValue = true;
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(field);
                return Result.Updated;
            }

            Undo.RecordObject(field, "登记齿轮瓦片");
            hazards.arraySize++;

            SerializedProperty added = hazards.GetArrayElementAtIndex(hazards.arraySize - 1);
            added.FindPropertyRelative("Tile").objectReferenceValue = tile;
            added.FindPropertyRelative("Damage").floatValue = DefaultDamage;
            added.FindPropertyRelative("Knockback").floatValue = DefaultKnockback;
            added.FindPropertyRelative("SelfArt").boolValue = true;

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(field);
            return Result.Added;
        }

        /// <summary>
        /// 确保 Tilemap 上有 <see cref="GearTileSpinner"/>，并且它的瓦片列表里有齿轮。
        /// 返回 true = 真的改动了（要标脏场景）。
        /// </summary>
        private static bool EnsureSpinner(HazardField field, TileBase tile)
        {
            var tilemap = field.GetComponent<Tilemap>();
            if (tilemap == null) return false;

            bool changed = false;

            var spinner = tilemap.GetComponent<GearTileSpinner>();
            if (spinner == null)
            {
                spinner = Undo.AddComponent<GearTileSpinner>(tilemap.gameObject);
                changed = true;
            }

            var serialized = new SerializedObject(spinner);
            SerializedProperty tiles = serialized.FindProperty("_spinTiles");
            if (tiles == null) return changed;

            for (int i = 0; i < tiles.arraySize; i++)
            {
                if (tiles.GetArrayElementAtIndex(i).objectReferenceValue == tile) return changed;
            }

            Undo.RecordObject(spinner, "登记齿轮瓦片");
            tiles.arraySize++;
            tiles.GetArrayElementAtIndex(tiles.arraySize - 1).objectReferenceValue = tile;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(spinner);
            return true;
        }
    }
}
