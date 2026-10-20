namespace RuleGame.Persistence
{
    /// <summary>
    /// 存档字节的存放处。只回答「有没有、读、写、删」，不认识 <see cref="Infrastructure.Persistence.SaveData"/>。
    /// </summary>
    /// <remarks>
    /// 抽这一层的理由与快照 / 时间系统一致：<b>纯 C# 可测</b>。
    /// 生产用 <see cref="FileSaveStorage"/>（<c>Application.persistentDataPath</c>），
    /// 验证用内存实现，不为测试去碰真实磁盘。
    /// </remarks>
    public interface ISaveStorage
    {
        /// <summary>存档是否已存在。</summary>
        bool Exists();

        /// <summary>读取全部文本；不存在时返回 null。读失败也返回 null（当作无档）。</summary>
        string Read();

        /// <summary>覆盖写入全部文本。</summary>
        void Write(string content);

        /// <summary>删除存档（调试 / 「清空进度」用）。不存在时静默成功。</summary>
        void Clear();
    }
}
