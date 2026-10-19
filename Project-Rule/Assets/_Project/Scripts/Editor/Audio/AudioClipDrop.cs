using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using RuleGame.Presentation.Audio;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Audio
{
    internal static class AudioClipDrop
    {
        internal const string ImportedFolder = "Assets/_Project/Audio/SFX/Imported";
        [Serializable] private sealed class Origin { public string Source; public string Sha256; }

        internal static bool Supported(string path) => new[] { ".wav", ".ogg", ".mp3", ".aif", ".aiff" }
            .Contains(Path.GetExtension(path).ToLowerInvariant());

        internal static string[] DragPaths() => DragAndDrop.paths
            .Concat(DragAndDrop.objectReferences.OfType<AudioClip>().Select(AssetDatabase.GetAssetPath))
            .Where(path => Supported(path) && File.Exists(path)).Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        internal static AudioClip ProjectClip(string path)
        {
            string full = Path.GetFullPath(path).Replace('\\', '/');
            string assets = Application.dataPath.Replace('\\', '/') + "/";
            return full.StartsWith(assets, StringComparison.OrdinalIgnoreCase)
                ? AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/" + full.Substring(assets.Length)) : null;
        }

        internal static int Assign(SfxBank bank, SfxCue cue, IEnumerable<string> paths, int replace = -1)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请退出 Play Mode 后修改音效配置。");
            var entry = bank?.Find(cue) ?? throw new InvalidOperationException("当前配置没有这个事件。");
            var clips = new List<AudioClip>(entry.Clips ?? Array.Empty<AudioClip>());
            if (replace >= clips.Count) throw new InvalidOperationException("变体列表已变化，请重新拖入。");
            var sources = paths.Where(Supported).Distinct(StringComparer.OrdinalIgnoreCase);
            if (replace >= 0) sources = sources.Take(1);
            var imported = sources.Select(Import).Distinct().ToArray();
            if (imported.Length == 0) throw new InvalidOperationException("没有可用的音频素材。");
            if (replace >= 0) clips[replace] = imported[0];
            else foreach (var clip in imported) if (!clips.Contains(clip)) clips.Add(clip);
            if (!clips.SequenceEqual(entry.Clips ?? Array.Empty<AudioClip>()))
            {
                Undo.RecordObject(bank, "拖入音效素材");
                entry.Clips = clips.ToArray();
                EditorUtility.SetDirty(bank);
            }
            return replace >= 0 ? replace : clips.IndexOf(imported[0]);
        }

        private static AudioClip Import(string path)
        {
            var existing = ProjectClip(path);
            if (existing != null) return existing;
            string source = Path.GetFullPath(path);
            string hash;
            using (var stream = File.OpenRead(source))
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            string name = Regex.Replace(Path.GetFileNameWithoutExtension(source), "[^a-zA-Z0-9_-]", "_");
            if (name.Length > 80) name = name.Substring(0, 80);
            string destination = ImportedFolder + "/" + name + "_" + hash.Substring(0, 12) + Path.GetExtension(source).ToLowerInvariant();
            if (!File.Exists(destination))
            {
                Directory.CreateDirectory(ImportedFolder);
                File.Copy(source, destination);
                AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(destination) as AudioImporter;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(destination);
                if (importer == null || clip == null) throw new InvalidOperationException("Unity 无法导入这个音频素材。");
                var settings = importer.defaultSampleSettings;
                settings.loadType = clip.length <= 15f ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.CompressedInMemory;
                settings.compressionFormat = clip.length <= 15f ? AudioCompressionFormat.PCM : AudioCompressionFormat.Vorbis;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings;
                string repository = Path.GetFullPath(Path.Combine(Application.dataPath, "../..")) + Path.DirectorySeparatorChar;
                importer.userData = JsonUtility.ToJson(new Origin
                {
                    Source = source.StartsWith(repository, StringComparison.OrdinalIgnoreCase) ? source.Substring(repository.Length) : source,
                    Sha256 = hash
                });
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(destination)
                ?? throw new InvalidOperationException("导入素材失败，请检查格式。");
        }
    }
}
