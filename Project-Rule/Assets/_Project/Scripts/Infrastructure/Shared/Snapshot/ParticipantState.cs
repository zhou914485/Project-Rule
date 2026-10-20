namespace RuleGame.Infrastructure.Snapshot
{
    /// <summary>
    /// 一个静态机关在快照时刻的模块状态。
    /// </summary>
    public struct ParticipantState
    {
        /// <summary>参与者；恢复时用它把状态写回去。</summary>
        public ISnapshotParticipant Participant;

        /// <summary>参与者自己定义的状态对象。</summary>
        public object State;
    }
}
