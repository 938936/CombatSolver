using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver.Engine.InCombat.Simulation;

namespace CombatSolver;

internal sealed partial class UnattendedTestRunner
{
    private static bool _act4HeartGateSubscribed;
    private static bool _act4HeartGateSubscriberActive;
    private static readonly Act4HeartMockGreenKeyHooks Act4HeartGateSubscriber = new();

    // Act 4 Heart（创意工坊 3747537811）兼容层合同。测试宿主不安装真实 Mod，用同构 mock 类型
    // 走适配层测试缝；结构自检、门禁与实际/模拟严格差分覆盖三类已审计内容。
    // 复用进程重跑时（IsAdopted 已为真）跳过“未适配拒绝”阶段，其余断言全部照跑。
    private async Task AssertAct4HeartCompatAsync(CombatState live, Player player)
    {
        // A. 结构自检必须拒绝带额外战斗 hook 的订阅器，且不得留下部分登记。
        try
        {
            Act4HeartCompat.AdoptForTesting(
                typeof(Act4HeartMockGreenKeyHooksWithCombatHook),
                typeof(Act4HeartMockMetallicize),
                typeof(Act4HeartMockRegenerate));
            throw new InvalidOperationException("结构自检接受了带额外战斗 hook 的订阅器。");
        }
        catch (Act4HeartCompat.Act4HeartAuditException)
        {
        }

        bool freshProcess = !Act4HeartCompat.IsAdopted;
        if (freshProcess)
        {
            // B. MVID 不匹配的候选程序集不得被登记（静默回落到未适配）。
            Act4HeartCompat.Adopt(typeof(Act4HeartMockMetallicize).Assembly, Guid.NewGuid());
            if (Act4HeartCompat.IsAdopted)
                throw new InvalidOperationException("MVID 不匹配的候选程序集被登记。");

            EnsureAct4HeartGateSubscription();
            // C1. 未适配时根捕获必须显式拒绝——这是引入兼容层之前的玩家可见行为。
            using (Act4HeartGateSubscriberScope())
            {
                try
                {
                    _ = CombatRootSnapshot.Capture(live);
                    throw new InvalidOperationException("未适配的 Act4Heart 订阅者没有被拒绝。");
                }
                catch (IncompatibleGameplayModException failure)
                {
                    if (failure.ModId != Act4HeartCompat.ModId)
                        throw new InvalidOperationException("拒绝异常丢失了 Act4Heart 来源。");
                }
            }
            Act4HeartCompat.AdoptForTesting(
                typeof(Act4HeartMockGreenKeyHooks),
                typeof(Act4HeartMockMetallicize),
                typeof(Act4HeartMockRegenerate));
        }
        else
        {
            EnsureAct4HeartGateSubscription();
        }
        // C2. 登记后同一订阅者必须通过门禁并完成完整根捕获。
        using (Act4HeartGateSubscriberScope())
        {
            _ = CombatRootSnapshot.Capture(live);
        }

        // 干净夹具：移除所有原生 Power，让回合末差分只包含 Act4Heart 形状的语义。
        foreach (PowerModel power in live.Creatures.SelectMany(static creature => creature.Powers).ToArray())
            await PowerCmd.Remove(power);
        Creature enemy = live.Enemies[0];

        // D. 超精英金属化：敌方回合末获得 Amount 格挡（模拟走域分支，实机走原生 Early hook）。
        await InjectPowerAsync(live, player, new UnattendedPowerInjection
        {
            PowerId = nameof(Act4HeartMockMetallicize), Target = "Enemy", TargetIndex = 0, Amount = 3,
        });
        await DiffAct4HeartEnemySideTurnEndAsync(live, player, enemy, "Metallicize", expectedBlockGain: 3, expectedHpGain: 0);

        // E. 玩家回合末的 Early 分发必须命中已登记镜像：不记录未镜像风险，非参与方无副作用。
        {
            CombatRootSnapshot root = CombatRootSnapshot.Capture(live);
            CombatPredictionSimulator child = root.ForkSimulator().Fork();
            var shadow = (SimulatedCombatState)child.State.CombatState;
            int enemyBlockBefore = child.State.GetCreature(enemy).Block;
            if (!PlayerTurnEndLifecycle.RunPhaseOne(child, shadow, player, [player.Creature]))
                throw new InvalidOperationException("玩家回合末阶段一遇到未提供的挂起选择。");
            if (child.History.HasRisk)
                throw new InvalidOperationException("已登记的金属化镜像仍记录了未镜像风险。");
            if (child.State.GetCreature(enemy).Block != enemyBlockBefore)
                throw new InvalidOperationException("非参与方的金属化镜像产生了格挡。");
        }

        // F. 超精英再生：AfterSideTurnEnd 治疗且不递减；连续两次触发各回 2 点。
        await InjectPowerAsync(live, player, new UnattendedPowerInjection
        {
            PowerId = nameof(Act4HeartMockRegenerate), Target = "Enemy", TargetIndex = 0, Amount = 2,
        });
        // 敌方先受伤，否则治疗被满血吞掉且无法区分递减语义。
        await CreatureCmd.Damage(
            new BlockingPlayerChoiceContext(),
            enemy,
            10,
            ValueProp.Unblockable | ValueProp.Unpowered,
            player.Creature,
            null,
            null);
        await DiffAct4HeartEnemySideTurnEndAsync(live, player, enemy, "Regenerate", expectedBlockGain: 3, expectedHpGain: 2);
        await DiffAct4HeartEnemySideTurnEndAsync(live, player, enemy, "RegenerateSecondTrigger", expectedBlockGain: 3, expectedHpGain: 2);

        _completedChecks.Add("Act4HeartCompat:AuditReject:MvidReject:GateReject:GateAccept:MetallicizeDiff:RegenerateDiff:NonDecrement:PlayerEndMirrorNoRisk");
    }

    private async Task DiffAct4HeartEnemySideTurnEndAsync(
        CombatState live,
        Player player,
        Creature enemy,
        string label,
        int expectedBlockGain,
        int expectedHpGain)
    {
        CombatRootSnapshot root = CombatRootSnapshot.Capture(live);
        CombatPredictionSimulator child = root.ForkSimulator().Fork();
        var shadow = (SimulatedCombatState)child.State.CombatState;
        int simulatedBlockBefore = child.State.GetCreature(enemy).Block;
        int simulatedHpBefore = child.State.GetCreature(enemy).CurrentHp;
        if (!CorePowerSupport.TriggerEnemySideTurnEndEffects(child, shadow, [enemy]))
            throw new InvalidOperationException($"{label} 的模拟敌方回合末遇到未提供的挂起选择。");
        MoveStateSnapshot predicted = CaptureSimulated(child, shadow, player, enemy);
        int simulatedBlockGain = child.State.GetCreature(enemy).Block - simulatedBlockBefore;
        int simulatedHpGain = child.State.GetCreature(enemy).CurrentHp - simulatedHpBefore;

        MoveStateSnapshot before = CaptureActual(live, player, enemy);
        await TriggerActualSideTurnEndAsync(live, CombatSide.Enemy, [enemy]);
        MoveStateSnapshot actual = CaptureActual(live, player, enemy);
        AssertSnapshotEqual(predicted, actual, "Act4Heart", label);
        int actualBlockGain = actual.EnemyBlock - before.EnemyBlock;
        int actualHpGain = actual.EnemyHp - before.EnemyHp;
        if (simulatedBlockGain != expectedBlockGain || actualBlockGain != expectedBlockGain)
            throw new InvalidOperationException(
                $"{label} 格挡增量模拟={simulatedBlockGain} 实机={actualBlockGain}，预期 {expectedBlockGain}。");
        if (simulatedHpGain != expectedHpGain || actualHpGain != expectedHpGain)
            throw new InvalidOperationException(
                $"{label} 生命增量模拟={simulatedHpGain} 实机={actualHpGain}，预期 {expectedHpGain}。");
    }

    private static void EnsureAct4HeartGateSubscription()
    {
        if (_act4HeartGateSubscribed)
            return;
        _act4HeartGateSubscribed = true;
        // 门禁订阅按 Mod id 全进程只允许一次；不活动时返回空集合，不影响其他场景的根捕获。
        ModHelper.SubscribeForRunStateHooks(
            Act4HeartCompat.ModId,
            _ => _act4HeartGateSubscriberActive
                ? [Act4HeartGateSubscriber]
                : Array.Empty<AbstractModel>());
    }

    private IDisposable Act4HeartGateSubscriberScope()
    {
        var previousMocks = AssemblyInfo.MockTypes;
        Mod mod = new()
        {
            path = "act4heart-compat-test",
            manifest = new ModManifest
            {
                id = Act4HeartCompat.ModId, name = "Act 4 Heart [Test]", affectsGameplay = true,
            },
        };
        AssemblyInfo.MockTypes = previousMocks == null ? [] : new(previousMocks);
        AssemblyInfo.MockTypes[Act4HeartGateSubscriber.GetType()] = (mod, false);
        _act4HeartGateSubscriberActive = true;
        return new Act4HeartGateSubscriberRestore(() =>
        {
            _act4HeartGateSubscriberActive = false;
            AssemblyInfo.MockTypes = previousMocks;
        });
    }

    private sealed class Act4HeartGateSubscriberRestore(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    // 以下 mock 类型照抄 Act4Heart 1.1.7 的 IL 审计行为；Type/StackType 取原 Mod 的数值 1。
    private abstract class Act4HeartMockPowerModel : PowerModel
    {
        public override PowerType Type => (PowerType)1;

        public override PowerStackType StackType => (PowerStackType)1;

        public override bool ShouldScaleInMultiplayer => true;
    }

    private sealed class Act4HeartMockMetallicize : Act4HeartMockPowerModel
    {
        public override Task BeforeSideTurnEndEarly(
            PlayerChoiceContext choiceContext,
            CombatSide side,
            IEnumerable<Creature> participants)
        {
            if (participants.Contains(Owner))
            {
                Flash();
                return CreatureCmd.GainBlock(Owner, Amount, ValueProp.Unpowered, null, false);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class Act4HeartMockRegenerate : Act4HeartMockPowerModel
    {
        public override Task AfterSideTurnEnd(
            PlayerChoiceContext choiceContext,
            CombatSide side,
            IEnumerable<Creature> participants)
        {
            if (participants.Contains(Owner) && !Owner.IsDead)
            {
                Flash();
                return CreatureCmd.Heal(Owner, Amount, true);
            }
            return Task.CompletedTask;
        }
    }

    private class Act4HeartMockGreenKeyHooks : AbstractModel
    {
        public override bool ShouldReceiveCombatHooks => true;

        public override ActMap ModifyGeneratedMapLate(IRunState runState, ActMap map, int actIndex) => map;

        public override Task BeforeCombatStart() => Task.CompletedTask;

        public override bool TryModifyRewards(Player player, List<Reward> rewards, AbstractRoom? room) => false;
    }

    private sealed class Act4HeartMockGreenKeyHooksWithCombatHook : Act4HeartMockGreenKeyHooks
    {
        public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
            => Task.CompletedTask;
    }
}
