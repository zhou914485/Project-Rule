using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RuleGame.Presentation.Audio;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuleGame.Editor.Audio
{
    [InitializeOnLoad]
    internal static class AudioBrowserValidation
    {
        private const string Request = "Temp/AudioBrowserValidation.request";
        private const string Probe = "Temp/AudioBrowserProbe.wav";
        private static readonly List<string> Results = new List<string>();
        private static SfxBank _original, _clone;
        private static AudioWorkbenchWindow _window;
        private static string _imported, _snapshot;
        private static double _deadline, _due;
        private static int _step;
        private static float _position;
        private static readonly Type Util = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        private static AudioClip Temporary => (AudioClip)typeof(AudioAudition).GetField("_temporary", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        static AudioBrowserValidation()
        {
            if (File.Exists(Request)) EditorApplication.delayCall += Run;
        }

        [MenuItem("Tools/规则游戏/音频/验证目录试听与拖入")]
        private static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            if (File.Exists(Request)) File.Delete(Request);
            Results.Clear(); _step = 0; _imported = null;
            try
            {
                _original = AssetDatabase.LoadAssetAtPath<SfxBank>(SfxSetup.BankPath);
                _snapshot = EditorJsonUtility.ToJson(_original);
                _clone = UnityEngine.Object.Instantiate(_original); _clone.name = "AudioBrowserValidation";
                string src = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Src"));
                var matches = AudioSourceBrowser.Scan(src, "Blade Swing Noise Slice 02");
                Check(matches.Count == 1 && !matches[0].Path.Contains("__MACOSX"), "递归搜索过滤资源分叉文件");
                Check(AudioSourceBrowser.Scan(src, "").Count(item => item.Directory) >= 2, "源素材根目录可浏览");
                File.Copy(matches[0].Path, Probe, true);
                AudioWorkbenchWindow.Open(_clone);
                _window = EditorWindow.GetWindow<AudioWorkbenchWindow>();
                _window.position = new Rect(100, 80, 1200, 760);
                var browser = _window.rootVisualElement.Q<AudioSourceBrowser>();
                browser.Navigate(Path.GetDirectoryName(matches[0].Path));
                var files = browser.Q<ListView>("source-files");
                int index = Enumerable.Range(0, files.itemsSource.Count).First(i => ((AudioSourceBrowser.Item)files.itemsSource[i]).Path == matches[0].Path);
                files.SetSelection(index);
                _deadline = EditorApplication.timeSinceStartup + 20;
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Check(bool condition, string text)
        {
            if (!condition) throw new InvalidOperationException(text);
            Results.Add("PASS " + text);
        }

        private static void Drop(VisualElement target, string path)
        {
            Check(target != null, "找到拖入目标 " + target?.name);
            DragAndDrop.PrepareStartDrag(); DragAndDrop.paths = new[] { path };
            DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
            using (var evt = DragUpdatedEvent.GetPooled()) { evt.target = target; target.SendEvent(evt); }
            using (var evt = DragPerformEvent.GetPooled()) { evt.target = target; target.SendEvent(evt); }
        }

        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("等待源文件试听超时。");
                if (_step == 0)
                {
                    if (Temporary == null) return;
                    var pcm = new float[Temporary.samples * Temporary.channels]; Temporary.GetData(pcm, 0);
                    Check(pcm.Any(value => Mathf.Abs(value) > .00001f), "源文件点击试听输出非零 PCM");
                    Check((bool)Util.GetMethod("IsPreviewClipPlaying").Invoke(null, null), "原生预览正在播放");
                    _position = (float)Util.GetMethod("GetPreviewClipPosition").Invoke(null, null);
                    _due = EditorApplication.timeSinceStartup + .09; _step = 1; return;
                }
                if (EditorApplication.timeSinceStartup < _due) return;
                if (_step == 1)
                {
                    float position = (float)Util.GetMethod("GetPreviewClipPosition").Invoke(null, null);
                    Check(position > _position, "试听实际播放进度前进");
                    AudioAudition.Stop();
                    var cue = SfxCue.SwordLight; var entry = _clone.Find(cue);
                    int count = entry.Clips.Length; float volume = entry.Volume;
                    var first = entry.Clips[0];
                    Drop(_window.rootVisualElement.Q<Button>("audio-event-" + (int)cue), Path.GetFullPath(Probe));
                    Check(entry.Clips.Length == count + 1, "拖到事件增加一个变体");
                    var imported = entry.Clips[count]; _imported = AssetDatabase.GetAssetPath(imported);
                    Check(_imported.StartsWith(AudioClipDrop.ImportedFolder + "/"), "外部素材仅在拖入时导入");
                    Check(AssetImporter.GetAtPath(_imported).userData.Contains("Sha256"), "导入记录来源及内容哈希");
                    Drop(_window.rootVisualElement.Q<Button>("audio-event-" + (int)cue), Path.GetFullPath(Probe));
                    Check(entry.Clips.Length == count + 1, "重复拖入复用资产且不重复增加变体");
                    Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
                    Drop(_window.rootVisualElement.Q("audio-variant-row-0"), Path.GetFullPath(_imported));
                    Check(entry.Clips[0] == imported && entry.Clips.Length == count + 1 && entry.Volume == volume, "拖到变体槽只替换对应素材");
                    Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                    Check(_clone.Find(cue).Clips[0] == first, "撤销恢复替换前素材");
                    AudioAudition.Stop();
                    var button = _window.rootVisualElement.Q<Button>("audio-event-" + (int)cue);
                    using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); }
                    Check(Temporary != null, "点击事件立即按配置试听");
                    AudioAudition.Stop(); Check(Temporary == null, "停止释放临时试听");
                    _due = EditorApplication.timeSinceStartup + .2; _step = 2; return;
                }
                var root = _window.rootVisualElement;
                var left = root.Q<AudioSourceBrowser>().worldBound;
                var list = root.Q(className: "workbench-list").worldBound;
                Check(left.width >= 250 && root.Q<ListView>("source-files").worldBound.height >= 150,
                    "左侧目录和文件列表有可用空间");
                Check(root.Q(className: "workbench-detail").worldBound.width >= 280 && left.xMax <= list.xMin,
                    "目录与右侧事件配置没有重叠");
                Check(EditorJsonUtility.ToJson(_original) == _snapshot, "实际音效配置未被验证修改");
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; AudioAudition.Stop();
            if (error != null) Results.Add("FAIL " + error);
            if (_original != null)
            {
                AudioWorkbenchWindow.Open(_original);
                _window = EditorWindow.GetWindow<AudioWorkbenchWindow>();
                _window.rootVisualElement.Q<AudioSourceBrowser>().Navigate(Path.GetFullPath(Path.Combine(Application.dataPath, "../../Src")));
            }
            if (_clone != null) { Undo.ClearUndo(_clone); UnityEngine.Object.DestroyImmediate(_clone); }
            if (!string.IsNullOrEmpty(_imported) && Path.GetFileName(_imported).StartsWith("AudioBrowserProbe_")) AssetDatabase.DeleteAsset(_imported);
            if (File.Exists(Probe)) File.Delete(Probe);
            Directory.CreateDirectory("Logs/AudioBrowser");
            File.WriteAllLines("Logs/AudioBrowser/Validation.txt", Results);
            Debug.Log("音频目录工作台验证：" + (error == null ? Results.Count + " 项通过" : "失败，请查看 Logs/AudioBrowser/Validation.txt"));
        }
    }
}
