using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver.Engine.Common;
using System.Text;

namespace CombatSolver;

internal sealed partial class SimulatedCombatState
{
    private ForkableDictionary<(Creature Creature, string Name), int>? _monsterIntStates;

    public int GetMonsterInt(Creature creature, string name)
    {
        (Creature, string) key = (creature, name);
        if (_monsterIntStates?.TryGetValue(key, out int value) == true)
            return value;
        if (_rootMaterialized && _rootCreatures.Contains(creature))
            throw new InvalidOperationException($"Root monster state {name} was not captured for {creature.Name}.");
        value = MonsterValueReader.ReadInt(
            creature.Monster ?? throw new InvalidOperationException("生物不是怪物。"),
            name);
        (_monsterIntStates ??= [])[key] = value;
        return value;
    }

    public int GetCustomMonsterInt(Creature creature, string name, int defaultValue = 0)
        => _monsterIntStates?.GetValueOrDefault((creature, name), defaultValue) ?? defaultValue;

    public int GetFabricatorLastSpawn(Creature creature)
    {
        const string stateName = "fabricator_last_spawn";
        (Creature, string) key = (creature, stateName);
        if (_monsterIntStates?.TryGetValue(key, out int value) == true)
            return value;
        if (_rootMaterialized && _rootCreatures.Contains(creature))
            throw new InvalidOperationException($"Root monster state {stateName} was not captured for {creature.Name}.");
        string id = (MonsterValueReader.ReadObject(creature.Monster!, "_lastSpawned") as MonsterModel)?.Id.Entry
            ?? string.Empty;
        value = id switch
        {
            "GUARDBOT" => 1,
            "NOISEBOT" => 2,
            "ZAPBOT" => 3,
            "STABBOT" => 4,
            _ => 0,
        };
        (_monsterIntStates ??= [])[key] = value;
        return value;
    }

    public bool GetMonsterBool(Creature creature, string name)
    {
        (Creature, string) key = (creature, name);
        if (_monsterIntStates?.TryGetValue(key, out int value) == true)
            return value != 0;
        if (_rootMaterialized && _rootCreatures.Contains(creature))
            throw new InvalidOperationException($"Root monster state {name} was not captured for {creature.Name}.");
        bool result = MonsterValueReader.ReadBool(
            creature.Monster ?? throw new InvalidOperationException("生物不是怪物。"),
            name);
        (_monsterIntStates ??= [])[key] = result ? 1 : 0;
        return result;
    }

    public void SetMonsterInt(Creature creature, string name, int value)
        => (_monsterIntStates ??= [])[(creature, name)] = value;

    public void SetMonsterBool(Creature creature, string name, bool value)
        => SetMonsterInt(creature, name, value ? 1 : 0);

    private void AppendMonsterStateFingerprint(ref StateFingerprintBuilder fingerprint)
    {
        foreach (Creature creature in KnownEnemies)
            _ = DescribePredictedMonsterState(creature);
        if (_monsterIntStates == null && _monsterCombatRngs == null)
            return;
        if (_monsterIntStates != null)
        {
            foreach (((Creature creature, string name), int value) in _monsterIntStates
                         .OrderBy(item => item.Key.Creature.CombatId)
                         .ThenBy(item => item.Key.Name, StringComparer.Ordinal))
            {
                fingerprint.Add('m');
                fingerprint.Add(creature.CombatId ?? uint.MaxValue);
                fingerprint.Add(name);
                fingerprint.Add(value);
            }
        }
        if (_monsterCombatRngs != null)
        {
            foreach ((Creature creature, MegaCrit.Sts2.Core.Random.Rng rng) in _monsterCombatRngs
                         .OrderBy(item => item.Key.CombatId))
            {
                PredictionRngState state = rng.CaptureState();
                fingerprint.Add('r');
                fingerprint.Add(creature.CombatId ?? uint.MaxValue);
                fingerprint.Add(state.Counter);
                fingerprint.Add(state.State0);
                fingerprint.Add(state.State1);
                fingerprint.Add(state.State2);
                fingerprint.Add(state.State3);
            }
        }
    }

    public void AppendPredictedMonsterStateContinuation(StringBuilder text)
    {
        for (int index = 0; index < Enemies.Count; index++)
        {
            Creature creature = Enemies[index];
            text.Append(";MS").Append(index).Append('=')
                .Append(DescribePredictedMonsterState(creature)).Append('/')
                .Append((int)(_deathPhases?.GetValueOrDefault(creature) ?? PredictedDeathPhase.None));
        }
    }

    public static void AppendLiveMonsterStateContinuation(StringBuilder text, IReadOnlyList<Creature> enemies)
    {
        for (int index = 0; index < enemies.Count; index++)
        {
            Creature creature = enemies[index];
            text.Append(";MS").Append(index).Append('=')
                .Append(DescribeLiveMonsterState(creature)).Append('/')
                .Append(LiveDeathPhase(creature));
        }
    }

    private string DescribePredictedMonsterState(Creature creature)
    {
        switch (Act4HeartCompat.GetFinaleMonsterKind(creature.Monster
                     ?? throw new InvalidOperationException("怪物状态描述的持有者不是怪物。")))
        {
            case Act4HeartCompat.FinaleMonsterKind.SpireShield:
                return $"{GetMonsterInt(creature, "state")}:{DescribeMonsterCombatRng(TryGetMonsterCombatRngState(creature))}";
            case Act4HeartCompat.FinaleMonsterKind.SpireSpear:
                return GetMonsterInt(creature, "state").ToString();
            case Act4HeartCompat.FinaleMonsterKind.CorruptHeart:
                return $"{GetMonsterInt(creature, "state")},{GetMonsterInt(creature, "buff_counter")}";
        }
        string type = creature.Monster?.GetType().Name ?? string.Empty;
        return type switch
        {
            "FrogKnight" => Bool("_hasBeetleCharged"),
            "TwoTailedRat" => $"{GetMonsterInt(creature, "_turnsUntilSummonable")},{GetMonsterInt(creature, "_callForBackupCount")}",
            "Fabricator" => FabricatorSpawnId(GetFabricatorLastSpawn(creature)),
            "ToughEgg" => Bool("_isHatched"),
            "TestSubject" => $"{GetMonsterInt(creature, "_respawns")},{GetMonsterInt(creature, "_extraMultiClawCount")}",
            "Tunneler" => Bool("_isStunned"),
            "LagavulinMatriarch" or "SlumberingBeetle" => Bool("_isAwake"),
            "Queen" => Bool("_hasAmalgamDied"),
            _ => "-",
        };

        string Bool(string name) => GetMonsterBool(creature, name) ? "1" : "0";
    }

    private static string DescribeMonsterCombatRng(PredictionRngState? state)
        => state is { } rng
            ? $"{rng.Counter:x}:{rng.State0:x16}:{rng.State1:x16}:{rng.State2:x16}:{rng.State3:x16}"
            : "-";

    private static string DescribeLiveMonsterState(Creature creature)
    {
        MonsterModel monster = creature.Monster!;
        switch (Act4HeartCompat.GetFinaleMonsterKind(monster))
        {
            case Act4HeartCompat.FinaleMonsterKind.SpireShield:
                return $"{MonsterValueReader.ReadInt(monster, "state")}:{DescribeMonsterCombatRng(monster.Rng.CaptureState())}";
            case Act4HeartCompat.FinaleMonsterKind.SpireSpear:
                return MonsterValueReader.ReadInt(monster, "state").ToString();
            case Act4HeartCompat.FinaleMonsterKind.CorruptHeart:
                return $"{MonsterValueReader.ReadInt(monster, "state")},{MonsterValueReader.ReadInt(monster, "buff_counter")}";
        }
        return monster.GetType().Name switch
        {
            "FrogKnight" => Bool("_hasBeetleCharged"),
            "TwoTailedRat" => $"{MonsterValueReader.ReadInt(monster, "_turnsUntilSummonable")},{MonsterValueReader.ReadInt(monster, "_callForBackupCount")}",
            "Fabricator" => (MonsterValueReader.ReadObject(monster, "_lastSpawned") as MonsterModel)?.Id.Entry ?? "-",
            "ToughEgg" => Bool("_isHatched"),
            "TestSubject" => $"{MonsterValueReader.ReadInt(monster, "_respawns")},{MonsterValueReader.ReadInt(monster, "_extraMultiClawCount")}",
            "Tunneler" => Bool("_isStunned"),
            "LagavulinMatriarch" or "SlumberingBeetle" => Bool("_isAwake"),
            "Queen" => Bool("_hasAmalgamDied"),
            _ => "-",
        };

        string Bool(string name) => MonsterValueReader.ReadBool(monster, name) ? "1" : "0";
    }

    private static int LiveDeathPhase(Creature creature)
    {
        if (creature.CurrentHp > 0)
            return (int)PredictedDeathPhase.None;
        string move = creature.Monster?.NextMove.Id ?? string.Empty;
        // Reattach's final-death hook leaves the old DEAD/REATTACH move (and
        // private animation flag) on segments already waiting to revive. Its
        // actual revival contract requires another living Reattach owner.
        // Project that contract for both root capture and continuation checks;
        // a stale move label is not evidence of a remaining revival.
        if (move is "DEAD_MOVE" or "REATTACH_MOVE"
            && creature.HasPower<ReattachPower>()
            && (creature.CombatState
                    ?? throw new InvalidOperationException("重新接合的死亡阶段缺少所属战斗。"))
                .GetTeammatesOf(creature)
                .Where(other => other != creature
                    && other.HasPower<ReattachPower>())
                .All(static other => other.IsDead))
        {
            return (int)PredictedDeathPhase.PermanentlyDead;
        }
        return move is "RESPAWN_MOVE" or "REVIVE_MOVE" or "DEAD_MOVE" or "REATTACH_MOVE"
            ? (int)PredictedDeathPhase.Reviving
            : (int)PredictedDeathPhase.PermanentlyDead;
    }

    private static string FabricatorSpawnId(int value)
        => value switch
        {
            1 => "GUARDBOT",
            2 => "NOISEBOT",
            3 => "ZAPBOT",
            4 => "STABBOT",
            _ => "-",
        };
}
