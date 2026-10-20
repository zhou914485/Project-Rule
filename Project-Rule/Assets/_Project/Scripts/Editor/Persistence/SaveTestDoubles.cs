using System.Collections.Generic;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Persistence;
using RuleGame.Infrastructure.Rules;
using RuleGame.Persistence;

namespace RuleGame.Editor.Persistence
{
    /// <summary>
    /// 存档验证用的内存 / 假对象。集中放这里，验证主文件只讲断言。
    /// </summary>
    internal static class SaveTestDoubles
    {
        /// <summary>内存存储，并统计写入次数（验证自动存档触发次数）。</summary>
        public sealed class MemoryStorage : ISaveStorage
        {
            private string _json;
            public int WriteCount { get; private set; }

            public bool Exists() => _json != null;
            public string Read() => _json;
            public void Write(string content) { _json = content; WriteCount++; }
            public void Clear() { _json = null; }
        }

        /// <summary>假玩家状态：只实现存档真正用到的能量 / 生命，其余给安全默认值。</summary>
        public sealed class FakePlayerStatus : IEntityStatus
        {
            public float Energy { get; private set; }
            public float EnergyMax => 100f;
            public float Health { get; private set; }
            public float HealthMax => 100f;
            public object HeldItem => null;
            public int ThrowableCount => 0;

            public FakePlayerStatus(float energy, float health)
            {
                Energy = energy;
                Health = health;
            }

            public bool TryConsumeEnergy(float amount)
            {
                if (Energy < amount) return false;
                Energy -= amount;
                return true;
            }

            public void RestoreEnergy(float amount) => Energy += amount;
            public bool IsOnCooldown(AttributeId attr, RelationId rel) => false;
            public void BeginCooldown(AttributeId attr, RelationId rel, float seconds) { }
            public float GetCooldownRemaining(AttributeId attr, RelationId rel) => 0f;

            public EntityStatusData ToData() => new EntityStatusData
            {
                Energy = Energy,
                Health = Health,
            };

            public void ApplyData(EntityStatusData data)
            {
                if (data == null) return;
                Energy = data.Energy;
                Health = data.Health;
            }

            public event System.Action<float, float> OnEnergyChanged;
            public event System.Action<float, float> OnHealthChanged;
            public event System.Action<AttributeId, RelationId, float> OnCooldownStarted;
        }

        /// <summary>内存解锁词条存储（玩家解锁系统落地前的测试替身）。</summary>
        public sealed class MemoryUnlockStore : IRuleUnlockStore
        {
            private readonly List<string> _attributes = new List<string>();
            private readonly List<string> _relations = new List<string>();

            public MemoryUnlockStore(IEnumerable<string> attributes = null, IEnumerable<string> relations = null)
            {
                if (attributes != null) _attributes.AddRange(attributes);
                if (relations != null) _relations.AddRange(relations);
            }

            public IReadOnlyList<string> UnlockedAttributes => _attributes;
            public IReadOnlyList<string> UnlockedRelations => _relations;

            public void RestoreUnlocks(IEnumerable<string> attributes, IEnumerable<string> relations)
            {
                _attributes.Clear();
                _relations.Clear();
                if (attributes != null) _attributes.AddRange(attributes);
                if (relations != null) _relations.AddRange(relations);
            }
        }
    }
}
