using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuleGame.Editor.Audio
{
    internal sealed class AudioSourceBrowser : VisualElement
    {
        internal sealed class Item
        {
            public string Path;
            public bool Directory;
            public string Label => (Directory ? "[目录] " : "♪ ") + System.IO.Path.GetFileName(Path);
        }
        private readonly ListView _files;
        private readonly Label _path, _count;
        private readonly ToolbarSearchField _search;
        private readonly Action<string> _status, _remember;
        private readonly Func<float> _volume;
        private string _directory;
        private Vector2 _dragStart;
        private bool _canDrag;
        private IVisualElementScheduledItem _pendingSearch;

        internal AudioSourceBrowser(string directory, Func<float> volume, Action<string> status, Action<string> remember)
        {
            name = "audio-source-browser";
            AddToClassList("workbench-browser");
            _volume = volume; _status = status; _remember = remember;
            Add(new Label("素材目录 · 点击即试听") { name = "source-heading" });
            string src = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Src"));
            var locations = new VisualElement(); locations.AddToClassList("workbench-toolbar"); Add(locations);
            locations.Add(new Button(() => Navigate(Application.dataPath)) { text = "项目 Assets" });
            var srcButton = new Button(() => Navigate(src)) { text = "Src 源素材" };
            srcButton.SetEnabled(Directory.Exists(src)); locations.Add(srcButton);
            locations.Add(new Button(() =>
            {
                string chosen = EditorUtility.OpenFolderPanel("选择音效素材目录", _directory, "");
                if (!string.IsNullOrEmpty(chosen)) Navigate(chosen);
            }) { text = "选目录…" });
            var navigation = new VisualElement(); navigation.AddToClassList("workbench-toolbar"); Add(navigation);
            navigation.Add(new Button(() => Navigate(Directory.GetParent(_directory)?.FullName ?? _directory)) { text = "↑ 上级" });
            navigation.Add(new Button(Refresh) { text = "刷新" });
            _path = new Label(); _path.AddToClassList("workbench-source-path"); Add(_path);
            _search = new ToolbarSearchField { name = "source-search", tooltip = "搜索当前目录及子目录，最多显示 2000 项" };
            _search.RegisterValueChangedCallback(_ =>
            {
                _pendingSearch?.Pause();
                _pendingSearch = schedule.Execute(Refresh).StartingIn(180);
            });
            Add(_search);
            _files = new ListView { name = "source-files", fixedItemHeight = 26, selectionType = SelectionType.Multiple,
                makeItem = MakeRow, bindItem = (element, index) =>
                {
                    var item = (Item)_files.itemsSource[index];
                    element.userData = item; ((Label)element).text = item.Label; element.tooltip = item.Path;
                } };
            _files.AddToClassList("workbench-source-files"); _files.selectionChanged += Selected; Add(_files);
            _count = new Label(); Add(_count);
            var note = new Label("拖到事件：添加变体（可多选）\n拖到变体槽：替换素材\n源素材仅在拖入时导入，试听不会导入。");
            note.AddToClassList("workbench-note"); Add(note);
            Navigate(Directory.Exists(directory) ? directory : Directory.Exists(src) ? src : Application.dataPath);
        }

        private VisualElement MakeRow()
        {
            var row = new Label(); row.AddToClassList("workbench-source-row");
            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || !(row.userData is Item item) || item.Directory) return;
                _canDrag = true; _dragStart = evt.position;
                if (_files.selectedItems.Contains(item)) Selected(new object[] { item });
            });
            row.RegisterCallback<PointerUpEvent>(_ => _canDrag = false);
            row.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_canDrag || (evt.pressedButtons & 1) == 0 || Vector2.Distance(_dragStart, evt.position) < 6) return;
                _canDrag = false;
                var paths = _files.selectedItems.OfType<Item>().Where(item => !item.Directory).Select(item => item.Path).ToArray();
                if (paths.Length == 0 && row.userData is Item current && !current.Directory) paths = new[] { current.Path };
                if (paths.Length == 0) return;
                DragAndDrop.PrepareStartDrag(); DragAndDrop.paths = paths;
                DragAndDrop.objectReferences = paths.Select(AudioClipDrop.ProjectClip).Where(clip => clip != null).Cast<UnityEngine.Object>().ToArray();
                DragAndDrop.StartDrag("音效素材"); evt.StopPropagation();
            });
            return row;
        }

        private void Selected(IEnumerable<object> selected)
        {
            var item = selected.OfType<Item>().LastOrDefault();
            if (item == null) return;
            if (item.Directory) { schedule.Execute(() => Navigate(item.Path)); return; }
            try { AudioAudition.PlayFile(item.Path, _volume(), _status); }
            catch (Exception error) { _status(error.GetBaseException().Message); }
        }

        internal void Navigate(string directory)
        {
            if (!Directory.Exists(directory)) return;
            _directory = Path.GetFullPath(directory); _remember(_directory);
            _pendingSearch?.Pause(); _search.SetValueWithoutNotify(""); Refresh();
        }

        private void Refresh()
        {
            try
            {
                var rows = Scan(_directory, _search.value);
                _files.ClearSelection(); _files.itemsSource = rows; _files.Rebuild();
                _path.text = _directory;
                _count.text = rows.Count + " 项" + (rows.Count >= 2000 ? " · 已达到显示上限，请缩小搜索范围" : "");
            }
            catch (Exception error) { _status(error.GetBaseException().Message); }
        }

        internal static List<Item> Scan(string directory, string search)
        {
            bool searching = !string.IsNullOrWhiteSpace(search);
            var rows = new List<Item>();
            if (!searching) rows.AddRange(Directory.EnumerateDirectories(directory).Where(path => !Ignored(Path.GetFileName(path)))
                .OrderBy(path => path).Take(2000).Select(path => new Item { Path = path, Directory = true }));
            var options = new EnumerationOptions { RecurseSubdirectories = searching, IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint };
            rows.AddRange(Directory.EnumerateFiles(directory, "*", options)
                .Where(path => AudioClipDrop.Supported(path) && !path.Split(Path.DirectorySeparatorChar).Any(Ignored)
                    && (!searching || Path.GetFileName(path).Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)))
                .Take(2000 - rows.Count).OrderBy(path => path).Select(path => new Item { Path = path }));
            return rows;
        }
        private static bool Ignored(string name) => name.StartsWith(".") || name == "__MACOSX" || name == "Library" || name == "Temp";
    }
}
