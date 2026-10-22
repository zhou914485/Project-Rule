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

        /// <summary>内存永久成长存储（宝箱记录 + 生命 / 能量上限增量 + 能力位）。</summary>
        public sealed class MemoryProgressStore : IPlayerProgressStore
        {
            private readonly List<string> _claimed = new List<string>();

            public float MaxHealthBonus { get; private set; }
            public float MaxEnergyBonus { get; private set; }
            public int UnlockedAbilities { get; private set; }

            public MemoryProgressStore(IEnumerable<string> claimed = null,
                                       float healthBonus = 0f, float energyBonus = 0f,
                                       int unlockedAbilities = 0)
            {
                if (claimed != null) _claimed.AddRange(claimed);
                MaxHealthBonus = healthBonus;
                MaxEnergyBonus = energyBonus;
                UnlockedAbilities = unlockedAbilities;
            }

            public IReadOnlyCollection<string> ClaimedSourceIds => _claimed;

            public void RestoreProgress(IReadOnlyCollection<string> claimedSourceIds,
                                        float healthBonus, float energyBonus)
            {
                _claimed.Clear();
                if (claimedSourceIds != null) _claimed.AddRange(claimedSourceIds);
                MaxHealthBonus = healthBonus;
                MaxEnergyBonus = energyBonus;
            }

            public void RestoreAbilities(int unlockedAbilities) => UnlockedAbilities = unlockedAbilities;
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
