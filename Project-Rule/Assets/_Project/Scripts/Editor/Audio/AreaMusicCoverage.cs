using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuleGame.Infrastructure.LevelFlow;
using RuleGame.Presentation.SceneComponents;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleGame.Editor.Audio
{
    /// <summary>
    /// 只读检查：分区表里的每个关卡场景，有没有带音乐的 <see cref="AreaZone"/>。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它</b>：PlayMode 音乐验证自己现场造区域，正式场景一个都没放也能全过
    /// —— 2026-10-21 的「音乐一直不切换」就是这么漏掉的。这里逐个打开分区场景数一遍，
    /// 场景里没有音乐区域时给 WARN，有区域但曲子不可播放时给 FAIL。</para>
    /// <para><b>只读</b>：additive 打开、检查完关掉；不保存、不改场景。</para>
    /// </remarks>
    public static class AreaMusicCoverage
    {
        private const string CatalogPath = "Assets/_Project/Data/Config/PartitionCatalog.asset";
        private const string LogPath = "Logs/AreaMusic/Coverage.txt";

        [MenuItem("Tools/规则游戏/音频/④ 检查区域音乐覆盖（EditMode）", false, 4)]
        public static void Run()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PartitionCatalog>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("找不到分区表：" + CatalogPath);

            var lines = new List<string>();
            int pass = 0, warn = 0, fail = 0;
            Scene active = SceneManager.GetActiveScene();

            for (int i = 0; i < catalog.Count; i++)
            {
                string path = catalog[i].ScenePath;
                if (string.IsNullOrEmpty(path)) continue;

                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

                try
                {
                    AreaZone[] zones = scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<AreaZone>(true))
                        .Where(zone => zone != null && zone.Music != null)
                        .ToArray();

                    if (zones.Length == 0)
                    {
                        lines.Add("WARN " + path + "：没有带音乐的 AreaZone，整片会一直放默认曲");
                        warn++;
                        continue;
                    }

                    bool ok = zones.All(zone => zone.Music.Loop != null && zone.Music.IsPlayable);
                    lines.Add((ok ? "PASS " : "FAIL ") + path + "：" +
                              string.Join("；", zones.Select(Describe)));
                    if (ok) pass++; else fail++;
                }
                catch (Exception ex)
                {
                    lines.Add("FAIL " + path + "：" + ex.Message);
                    fail++;
                }
                finally
                {
                    if (opened) EditorSceneManager.CloseScene(scene, true);
                    if (active.IsValid()) EditorSceneManager.SetActiveScene(active);
                }
            }

            string summary = "passed=" + pass + " warned=" + warn + " failed=" + fail;
            lines.Add(summary);
            Directory.CreateDirectory("Logs/AreaMusic");
            File.WriteAllLines(LogPath, lines);
            Debug.Log("[AreaMusicCoverage] " + summary + "\n" + string.Join("\n", lines));
            if (fail > 0) throw new InvalidOperationException(summary + "，详见 " + LogPath);
        }

        private static string Describe(AreaZone zone)
        {
            string kind = zone is BackdropZone ? "BackdropZone" : "MusicRegion";
            string shape = zone.HasParts ? zone.PartCount + "段" : "单矩形";
            return kind + " priority=" + zone.Priority + " " + shape +
                   " 外接=" + zone.Size + " music=" + zone.Music.name;
        }
    }
}