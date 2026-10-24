using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver.Engine.InCombat.Simulation;

namespace CombatSolver;

internal sealed partial class SimulatedCombatState
{
    public void DoomKill(CombatPredictionSimulator simulator, IReadOnlyList<Creature> creatures)
    {
        if (creatures.Count == 0)
            return;
        // Act 4 Heart 的 DoomKill HookBefore 补丁（heart_doom_damages_instead_of_kills 开启时）：
        // 持有 InvinciblePower 且当前生命超过剩余免疫额度的生物改为承受末日数值伤害并离开
        // 死亡名单；处理顺序与原补丁一致（倒序），死亡统计只按过滤后的名单。
        if (Act4HeartDoomDamagesInsteadOfKills)
            creatures = ApplyAct4HeartDoomDamage(simulator, creatures);
        if (creatures.Count == 0)
            return;
        Dictionary<Player, int> fatalCounts = [];
        foreach (Player player in Players)
        {
            int count = creatures.Count(creature => creature != player.Creature
                && EffectivePowers()
                    .Where(power => power.Owner == creature)
                    .All(power => power.ShouldOwnerDeathTriggerFatal()));
            fatalCounts[player] = count;
        }
        foreach (Creature creature in creatures)
        {
            if (!simulator.Kill(creature))
                return;
        }
        foreach ((Player player, int count) in fatalCounts)
        {
            if (count <= 0)
                continue;
            foreach (BookRepairKnife relic in RelicsOf(player)
                         .OfType<BookRepairKnife>()
                         .Where(relic => !relic.IsMelted))
            {
                simulator.Heal(player.Creature, relic.DynamicVars.Heal.BaseValue * count);
                if (simulator.HasPendingChoice)
                    return;
            }
        }
    }

    private IReadOnlyList<Creature> ApplyAct4HeartDoomDamage(
        CombatPredictionSimulator simulator,
        IReadOnlyList<Creature> creatures)
    {
        List<Creature>? filtered = null;
        for (int index = creatures.Count - 1; index >= 0; index--)
        {
            Creature creature = creatures[index];
            PowerModel? invincible = null;
            PowerModel? doom = null;
            foreach (PowerModel power in EffectivePowers())
            {
                if (!ReferenceEquals(power.Owner, creature))
                    continue;
                if (Act4HeartCompat.IsInvinciblePower(power))
                    invincible = power;
                else if (power is DoomPower doomPower)
                    doom = doomPower;
            }
            if (invincible is null || doom is null)
                continue;
            Act4HeartInvinciblePredictionState state = simulator.StateStore.Get(
                invincible, static value => Act4HeartCompat.CreateInvinciblePredictionState(value));
            decimal remainingBudget = Math.Max(0m, invincible.Amount - state.DamageReceivedThisTurn);
            if (simulator.State.GetCreature(creature).CurrentHp <= remainingBudget)
                continue;
            // 原补丁的 CreatureCmd.Damage 使用 ValueProp 6（Unblockable|Unpowered）且无伤害来源；
            // 该伤害会继续流经 Invincible 的掉血钳制并累计额度。
            simulator.Damage(
                creature,
                doom.Amount,
                ValueProp.Unblockable | ValueProp.Unpowered,
                dealer: null);
            if (simulator.HasPendingChoice)
                return filtered ?? creatures;
            filtered ??= [.. creatures];
            filtered.Remove(creature);
        }
        return filtered ?? creatures;
    }
}
