using System;
using System.IO;
using UnityEngine;

namespace RuleGame.Persistence
{
    /// <summary>
    /// <see cref="ISaveStorage"/> 的文件实现：单个 JSON 文件放在
    /// <c>Application.persistentDataPath</c> 下。
    /// </summary>
    /// <remarks>
    /// <para><b>单存档位</b>（策划案 §十：自动存档，不做手动存档，不做槽位选择）。</para>
    /// <para>写入用「临时文件 + 替换」而不是直接覆盖目标文件 —— 写到一半进程被杀
    /// （手机切后台 / 崩溃）时，旧档仍然完整，不会留下半截 JSON 的坏档。</para>
    /// </remarks>
    public sealed class FileSaveStorage : ISaveStorage
    {
        /// <summary>默认文件名。放在 persistentDataPath 根目录，不建子目录（单档没必要）。</summary>
        public const string DefaultFileName = "savegame.json";

        private readonly string _directory;
        private readonly string _fileName;

        private string FilePath => Path.Combine(_directory, _fileName);
        private string TempPath => FilePath + ".tmp";

        /// <summary>生产构造：<c>persistentDataPath/savegame.json</c>。</summary>
        public FileSaveStorage() : this(Application.persistentDataPath, DefaultFileName) { }

        /// <summary>测试 / 多档构造：指定目录与文件名（例如临时目录）。</summary>
        public FileSaveStorage(string directory, string fileName = DefaultFileName)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("存档目录不能为空。", nameof(directory));
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("存档文件名不能为空。", nameof(fileName));

            _directory = directory;
            _fileName = fileName;
        }

        public bool Exists() => File.Exists(FilePath);

        public string Read()
        {
            try
            {
                // ReadAllText 内部就是「打开 → 读完 → 关闭」，不会长时间占句柄。
                return File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;
            }
            catch (Exception e)
            {
                // 读失败按「无档」处理：磁盘满 / 杀毒软件锁文件都不该让游戏进不去。
                Debug.LogError($"[FileSaveStorage] 读取存档失败，按无档处理：{e.Message}");
                return null;
            }
        }

        public void Write(string content)
        {
            try
            {
                Directory.CreateDirectory(_directory);

                // 先写临时文件再替换：任何时刻正式存档要么是旧的完整版、要么是新的完整版。
                File.WriteAllText(TempPath, content);

                if (File.Exists(FilePath)) File.Replace(TempPath, FilePath, null);
                else File.Move(TempPath, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[FileSaveStorage] 写入存档失败：{e.Message}");
                // 别把半截临时文件留在那，下次 Exists/Read 虽不看它，但碍眼且误导排查。
                try { if (File.Exists(TempPath)) File.Delete(TempPath); } catch { /* 忽略 */ }
            }
        }

        public void Clear()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception e) { Debug.LogError($"[FileSaveStorage] 删除存档失败：{e.Message}"); }
        }
    }
}
