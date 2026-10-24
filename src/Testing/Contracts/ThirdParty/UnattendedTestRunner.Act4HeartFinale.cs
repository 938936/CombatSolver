using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using System.Runtime.InteropServices;
using System.Reflection;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors;
using CombatSolver.Engine.InCombat.Simulation;

namespace CombatSolver;

internal sealed partial class UnattendedTestRunner
{
    // Act 4 Heart 第四层“终幕”合同。测试宿主不安装真实 Mod，用同构 mock 类型（照抄
    // Act4Heart 1.1.7 审计 IL）走同一审计与适配路径；登记在 runner 构造时完成（早于首个
    // 根捕获），行为开关由构造时的测试缝覆盖提供（概率 0.5，末日转伤害开启）。行动差分由
    // 请求的 monsterMoveChecks 驱动，本文件负责不可战胜钳制/累计/重置与末日转伤害的手动
    // 模拟 vs 实机差分；死亡之搏与每怪物 Rng 消费对齐在请求检查里覆盖。怪物门禁的未适配
    // 拒绝路径由 INCOMPATIBLE-MOD-TURN-SETUP-STOP 与 ContentModFailures 场景维护。
    private async Task AssertAct4HeartFinaleAsync(CombatState live, Player player)
    {
        if (!Act4HeartCompat.IsAdopted)
            throw new InvalidOperationException("Act4Heart mock 兼容层没有在 runner 构造时登记。");

        Creature heart = live.Enemies.FirstOrDefault(candidate => candidate.Monster is Act4HeartMockCorruptHeart)
            ?? throw new InvalidOperationException("终幕场景没有找到 mock 腐化之心。");

        // A. 不可战胜：本回合额度钳制、跨次累计与持有者一侧回合开始时的重置。
        await DiffAct4HeartInvincibleResetAsync(live, player, heart);

        // B. 末日转伤害（heart_doom_damages_instead_of_kills 开启）：额度未耗尽且当前生命
        //    超过剩余额度时，末日改为造成被钳制的伤害而不是击杀。实机侧用与原补丁同构的
        //    Harmony Prefix 复刻（原 Mod 未安装）。
        await DiffAct4HeartDoomConversionAsync(live, player, heart);

        _completedChecks.Add("Act4HeartFinale:InvincibleClamp:InvincibleReset:DoomConversion");
    }

    private static void EnsureAct4HeartMocksAdopted()
    {
        if (Act4HeartCompat.IsAdopted)
            return;
        Act4HeartCompat.AdoptForTesting(new Act4HeartCompat.Act4HeartAdoptedTypes(
            typeof(Act4HeartMockGreenKeyHooks),
            typeof(Act4HeartMockMetallicize),
            typeof(Act4HeartMockRegenerate),
            typeof(Act4HeartMockSpireShield),
            typeof(Act4HeartMockSpireSpear),
            typeof(Act4HeartMockCorruptHeart),
            typeof(Act4HeartMockBeatOfDeath),
            typeof(Act4HeartMockInvincible)));
    }

    private async Task DiffAct4HeartInvincibleResetAsync(CombatState live, Player player, Creature heart)
    {
        // 与 DiffAct4HeartEnemySideTurnEndAsync 同构：模拟侧走镜像，实机侧走原生命令/钩子。
        // 序列：190 全额 -> 30 被钳制到剩余额度 10 -> 持有者回合开始重置 -> 30 再次全额。
        // 建局阶段的自动求解可能已消耗过额度、改变生命；先重置预算并固定生命再取根。
        await Hook.BeforeSideTurnStart(live, CombatSide.Enemy, [heart]);
        await CreatureCmd.SetCurrentHp(heart, heart.MaxHp);
        CombatRootSnapshot root = CombatRootSnapshot.Capture(live);
        CombatPredictionSimulator child = root.ForkSimulator().Fork();
        var shadow = (SimulatedCombatState)child.State.CombatState;
        Creature simulatedHeart = shadow.Enemies.FirstOrDefault(candidate => candidate.Monster is Act4HeartMockCorruptHeart)
            ?? throw new InvalidOperationException("模拟侧没有 mock 腐化之心。");
        int invincibleAmount = shadow.EffectivePowers()
            .Where(power => ReferenceEquals(power.Owner, simulatedHeart) && Act4HeartCompat.IsInvinciblePower(power))
            .Sum(static power => power.Amount);
        int expectedInvincible = live.RunState.AscensionLevel >= 9 ? 200 : 300;
        if (invincibleAmount != expectedInvincible)
            throw new InvalidOperationException(
                $"mock 腐化之心的不可战胜额度异常：{invincibleAmount}（A{live.RunState.AscensionLevel} 预期 {expectedInvincible}）。");

        int simulatedHpStart = child.State.GetCreature(simulatedHeart).CurrentHp;
        int firstHit = invincibleAmount - 10;
        child.Damage(simulatedHeart, firstHit, ValueProp.Unblockable | ValueProp.Unpowered, dealer: null);
        if (child.State.GetCreature(simulatedHeart).CurrentHp != simulatedHpStart - firstHit)
            throw new InvalidOperationException("模拟侧第一次伤害没有全额命中（额度未使用时不应被钳制）。");
        child.Damage(simulatedHeart, 30, ValueProp.Unblockable | ValueProp.Unpowered, dealer: null);
        if (child.State.GetCreature(simulatedHeart).CurrentHp != simulatedHpStart - invincibleAmount)
            throw new InvalidOperationException("模拟侧第二次伤害没有被钳制到剩余额度 10。");
        if (!HookMirrors.BeforeSideTurnStart(child, CombatSide.Enemy, [simulatedHeart]))
            throw new InvalidOperationException("模拟侧敌方回合开始遇到未提供的挂起选择。");
        child.Damage(simulatedHeart, 30, ValueProp.Unblockable | ValueProp.Unpowered, dealer: null);
        if (child.State.GetCreature(simulatedHeart).CurrentHp != simulatedHpStart - invincibleAmount - 30)
            throw new InvalidOperationException("模拟侧回合开始后额度没有重置。");
        int predictedHp = child.State.GetCreature(simulatedHeart).CurrentHp;
        MoveStateSnapshot predicted = CaptureSimulated(child, shadow, player, heart);

        int actualHpStart = heart.CurrentHp;
        await CreatureCmd.Damage(new BlockingPlayerChoiceContext(), heart, firstHit, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
        await CreatureCmd.Damage(new BlockingPlayerChoiceContext(), heart, 30, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
        await Hook.BeforeSideTurnStart(live, CombatSide.Enemy, [heart]);
        await CreatureCmd.Damage(new BlockingPlayerChoiceContext(), heart, 30, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
        if (heart.CurrentHp != predictedHp)
            throw new InvalidOperationException(
                $"不可战胜重置差分：实机 {heart.CurrentHp}，模拟 {predictedHp}。");
        if (actualHpStart - heart.CurrentHp != invincibleAmount + 30)
            throw new InvalidOperationException($"实机侧三次伤害总量异常：{actualHpStart - heart.CurrentHp}。");
        MoveStateSnapshot actual = CaptureActual(live, player, heart);
        AssertSnapshotEqual(predicted, actual, "Act4HeartFinale", "InvincibleReset");
    }

    private async Task DiffAct4HeartDoomConversionAsync(CombatState live, Player player, Creature heart)
    {
        // 先注入末日并把生命压到超过剩余免疫额度的位置；末日数值改为被钳制的伤害而不是击杀。
        // 注入目标用腐化之心的实际下标（默认遭遇敌人排在它前面）。
        int heartIndex = live.Enemies.ToList().IndexOf(heart);
        if (heartIndex < 0)
            throw new InvalidOperationException("腐化之心不在敌方列表里。");
        await InjectPowerAsync(live, player, new UnattendedPowerInjection
        {
            PowerId = "DOOM_POWER", Target = "Enemy", TargetIndex = heartIndex, Amount = 600,
        });
        await CreatureCmd.SetCurrentHp(heart, 500);
        // 重置差分留下的已用额度先清零，末日差分从全新额度开始。
        await Hook.BeforeSideTurnStart(live, CombatSide.Enemy, [heart]);
        CombatRootSnapshot root = CombatRootSnapshot.Capture(live);
        CombatPredictionSimulator child = root.ForkSimulator().Fork();
        var shadow = (SimulatedCombatState)child.State.CombatState;
        Creature simulatedHeart = shadow.Enemies.FirstOrDefault(candidate => candidate.Monster is Act4HeartMockCorruptHeart)
            ?? throw new InvalidOperationException("模拟侧没有 mock 腐化之心。");
        int invincibleAmount = shadow.EffectivePowers()
            .Where(power => ReferenceEquals(power.Owner, simulatedHeart) && Act4HeartCompat.IsInvinciblePower(power))
            .Sum(static power => power.Amount);
        int expectedDoomLoss = invincibleAmount - 180;
        if (expectedDoomLoss <= 0)
            throw new InvalidOperationException($"末日差分的免疫额度不足：{invincibleAmount}。");
        child.Damage(simulatedHeart, 180, ValueProp.Unblockable | ValueProp.Unpowered, dealer: null);
        int simulatedHpBefore = child.State.GetCreature(simulatedHeart).CurrentHp;
        if (!CorePowerSupport.TriggerEnemySideTurnEndEffects(child, shadow, [simulatedHeart]))
            throw new InvalidOperationException("模拟侧敌方回合末遇到未提供的挂起选择。");
        int simulatedHpAfter = child.State.GetCreature(simulatedHeart).CurrentHp;
        if (simulatedHeart.IsDead || simulatedHpAfter != simulatedHpBefore - expectedDoomLoss)
            throw new InvalidOperationException(
                $"模拟侧末日转伤害异常：存活={simulatedHeart.IsDead}，" +
                $"掉血 {simulatedHpBefore - simulatedHpAfter}（预期 {expectedDoomLoss}）；" +
                $"hpBefore={simulatedHpBefore} hpAfter={simulatedHpAfter} " +
                $"flag={shadow.Act4HeartDoomDamagesInsteadOfKills} invincible={invincibleAmount} " +
                $"liveHp={heart.CurrentHp} liveDead={heart.IsDead}。");
        MoveStateSnapshot predicted = CaptureSimulated(child, shadow, player, heart);

        // 实机侧安装与原补丁同构的 DoomKill Prefix（测试进程内安装与卸载）。
        Harmony doomPatch = new("CombatSolver.Testing.Act4HeartDoom." + _request.RunId);
        MethodInfo original = AccessTools.Method(typeof(DoomPower), nameof(DoomPower.DoomKill))
            ?? throw new MissingMethodException(nameof(DoomPower), nameof(DoomPower.DoomKill));
        try
        {
            doomPatch.Patch(original,
                prefix: new HarmonyMethod(typeof(UnattendedTestRunner), nameof(Act4HeartMockDoomKillPrefix)));
            await CreatureCmd.Damage(new BlockingPlayerChoiceContext(), heart, 180, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
            int actualHpBefore = heart.CurrentHp;
            await TriggerActualSideTurnEndAsync(live, CombatSide.Enemy, [heart]);
            int actualHpAfter = heart.CurrentHp;
            if (heart.IsDead || actualHpAfter != actualHpBefore - expectedDoomLoss)
                throw new InvalidOperationException(
                    $"实机侧末日转伤害异常：存活={heart.IsDead}，" +
                    $"掉血 {actualHpBefore - actualHpAfter}（预期 {expectedDoomLoss}）。");
            MoveStateSnapshot actual = CaptureActual(live, player, heart);
            AssertSnapshotEqual(predicted, actual, "Act4HeartFinale", "DoomConversion");
        }
        finally
        {
            doomPatch.UnpatchAll(doomPatch.Id);
        }
    }

    // 与 Act4Heart.HeartHooks.PreventDoomWhenInvincible_Before_DoomKill 同构的 Prefix 复刻
    // （含其未 await 的即发即忘伤害调用）；配置读取走兼容层测试缝，与模拟侧同一来源。
    private static bool Act4HeartMockDoomKillPrefix(ref IReadOnlyList<Creature> creatures)
    {
        if (!Act4HeartCompat.ReadDoomDamagesInsteadOfKills())
            return true;
        List<Creature>? filtered = null;
        for (int index = creatures.Count - 1; index >= 0; index--)
        {
            Creature creature = creatures[index];
            Act4HeartMockInvincible? power = creature.GetPower<Act4HeartMockInvincible>();
            if (power == null || creature.CurrentHp <= power.null_amount)
                continue;
            DoomPower doom = creature.GetPower<DoomPower>()
                ?? throw new InvalidOperationException($"末日名单里的 {creature.Name} 没有末日 Power。");
            _ = CreatureCmd.Damage(
                new ThrowingPlayerChoiceContext(),
                creature,
                doom.Amount,
                ValueProp.Unblockable | ValueProp.Unpowered,
                null,
                null);
            (filtered ??= [.. creatures]).Remove(creature);
        }
        if (filtered != null)
            creatures = filtered;
        return true;
    }

    // —— 以下 mock 怪物照抄 Act4Heart 1.1.7 审计 IL（数值/分支/私有字段名一致）——

    private sealed class Act4HeartMockSpireShield : MonsterModel
    {
        public override int MinInitialHp => AscensionHelper.GetValueIfAscension((MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel)8, 125, 110);

        public override int MaxInitialHp => MinInitialHp;

        private byte state;

        protected override MonsterMoveStateMachine GenerateMoveStateMachine()
        {
            MoveState bash = new("BASH_MOVE", BashMove,
                new SingleAttackIntent(14), new DebuffIntent(false));
            MoveState fortify = new("FORTIFY_MOVE", FortifyMove, new DefendIntent());
            MoveState smash = new("SMASH_MOVE", SmashMove,
                new SingleAttackIntent(38), new DefendIntent());
            RandomBranchState randomBranch = new("RANDOM_BRANCH");
            ConditionalBranchState postAttack = new("POST_ATTACK_BRANCH");
            randomBranch.AddBranch(bash, 0);
            randomBranch.AddBranch(fortify, 0);
            state = 0;
            bash.FollowUpState = postAttack;
            fortify.FollowUpState = postAttack;
            postAttack.AddState(bash, () => (state & 1) == 0);
            postAttack.AddState(fortify, () => (state & 2) == 0);
            postAttack.AddState(smash, () => (state & 3) == 3);
            smash.FollowUpState = randomBranch;
            return new MonsterMoveStateMachine([bash, fortify, smash, randomBranch, postAttack], randomBranch);
        }

        public override async Task AfterAddedToRoom()
        {
            await PowerCmd.Apply<BackAttackLeftPower>(
                new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null, false);
            await PowerCmd.Apply<ArtifactPower>(
                new ThrowingPlayerChoiceContext(), Creature, AscensionHelper.GetValueIfAscension((MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel)9, 2, 1), Creature, null, false);
        }

        private async Task BashMove(IReadOnlyList<Creature> targets)
        {
            state |= 1;
            await DamageCmd.Attack(14).FromMonster(this).Execute(null);
            foreach (Creature creature in targets)
            {
                if (creature.IsPlayer
                    && creature.Player!.PlayerCombatState!.OrbQueue.Capacity > 0
                    && Rng.NextFloat(1f) < Act4HeartCompat.ReadShieldFocusDownOdds())
                {
                    await PowerCmd.Apply<FocusPower>(new ThrowingPlayerChoiceContext(), creature, -1m, Creature, null, false);
                }
                else
                {
                    await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), creature, -1m, Creature, null, false);
                }
            }
        }

        private async Task FortifyMove(IReadOnlyList<Creature> targets)
        {
            state |= 2;
            await Task.WhenAll(Creature.CombatState!.GetTeammatesOf(Creature)
                .Select(teammate => CreatureCmd.GainBlock(teammate, 30m, ValueProp.Move, null, false)));
        }

        private async Task SmashMove(IReadOnlyList<Creature> targets)
        {
            state = 0;
            AttackCommand attackCommand = await DamageCmd.Attack(38).FromMonster(this).Execute(null);
            if (!AscensionHelper.HasAscension((MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel)9))
            {
                int total = 0;
                foreach (List<DamageResult> results in attackCommand.Results)
                    foreach (DamageResult result in results)
                        total += result.TotalDamage;
                await CreatureCmd.GainBlock(Creature, total, ValueProp.Unpowered, null, false);
            }
            else
            {
                await CreatureCmd.GainBlock(Creature, 99m, ValueProp.Move, null, false);
            }
        }
    }

    private sealed class Act4HeartMockSpireSpear : MonsterModel
    {
        public override int MinInitialHp => AscensionHelper.GetValueIfAscension((MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel)8, 180, 160);

        public override int MaxInitialHp => MinInitialHp;

        private byte state;

        protected override MonsterMoveStateMachine GenerateMoveStateMachine()
        {
            MoveState burnStrike = new("BURN_STRIKE_MOVE", BurnStrikeMove,
                new MultiAttackIntent(6, 2), new StatusIntent(2));
            MoveState skewer = new("SKEWER_MOVE", SkewerMove, new MultiAttackIntent(10, 4));
            MoveState piercer = new("PIERCER_MOVE", PiercerMove, new BuffIntent());
            RandomBranchState randomBranch = new("RANDOM_BRANCH");
            ConditionalBranchState postAttack = new("POST_ATTACK_BRANCH");
            state = 2;
            burnStrike.FollowUpState = postAttack;
            postAttack.AddState(burnStrike, () => (state & 1) == 0);
            postAttack.AddState(piercer, () => (state & 2) == 0);
            postAttack.AddState(skewer, () => (state & 3) == 3);
            skewer.FollowUpState = randomBranch;
            randomBranch.AddBranch(burnStrike, 0);
            randomBranch.AddBranch(piercer, 0);
            piercer.FollowUpState = postAttack;
            return new MonsterMoveStateMachine([burnStrike, skewer, piercer, randomBranch, postAttack], burnStrike);
        }

        public override async Task AfterAddedToRoom()
        {
            await PowerCmd.Apply<SurroundedPower>(
                new ThrowingPlayerChoiceContext(), CombatState!.GetOpponentsOf(Creature), 1m, Creature, null, false);
            await PowerCmd.Apply<BackAttackRightPower>(
                new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null, false);
            await PowerCmd.Apply<ArtifactPower>(
                new ThrowingPlayerChoiceContext(), Creature, AscensionHelper.GetValueIfAscension((MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel)9, 2, 1), Creature, null, false);
        }

        private async Task BurnStrikeMove(IReadOnlyList<Creature> targets)
        {
            state |= 1;
            await DamageCmd.Attack(6).WithHitCount(2).FromMonster(this).Execute(null);
            await CardPileCmd.AddToCombatAndPreview<Burn>(
                targets,
                AscensionHelper.HasAscension((MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel)9) ? PileType.Draw : PileType.Discard,
                2,
                null,
                CardPilePosition.Top);
        }

        private async Task SkewerMove(IReadOnlyList<Creature> targets)
        {
            state = 0;
            await DamageCmd.Attack(10).WithHitCount(4).FromMonster(this).Execute(null);
        }

        private async Task PiercerMove(IReadOnlyList<Creature> targets)
        {
            state |= 2;
            await Task.WhenAll(Creature.CombatState!.GetTeammatesOf(Creature)
                .Select(teammate => PowerCmd.Apply<StrengthPower>(
                    new ThrowingPlayerChoiceContext(), teammate, 2m, Creature, null, false)));
        }
    }

    private sealed class Act4HeartMockCorruptHeart : MonsterModel
    {
        public override int MinInitialHp => AscensionHelper.GetValueIfAscension((MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel)8, 800, 750);

        public override int MaxInitialHp => MinInitialHp;

        private byte state;

        private int buff_counter;

        protected override MonsterMoveStateMachine GenerateMoveStateMachine()
        {
            MoveState debilitate = new("DEBILITATE_MOVE", DebilitateMove,
                new DebuffIntent(true), new StatusIntent(5));
            MoveState bloodShots = new("BLOOD_SHOTS_MOVE", BloodShotsMove, new MultiAttackIntent(2, 15));
            MoveState echo = new("ECHO_MOVE", EchoMove, new SingleAttackIntent(45));
            MoveState buff = new("BUFF_MOVE", BuffMove, new BuffIntent());
            RandomBranchState randomAttack = new("RANDOM_ATTACK_BRANCH");
            ConditionalBranchState postAttack = new("POST_ATTACK_BRANCH");
            debilitate.FollowUpState = randomAttack;
            randomAttack.AddBranch(bloodShots, 0);
            randomAttack.AddBranch(echo, 0);
            state = 0;
            bloodShots.FollowUpState = postAttack;
            echo.FollowUpState = postAttack;
            postAttack.AddState(bloodShots, () => (state & 1) == 0);
            postAttack.AddState(echo, () => (state & 2) == 0);
            postAttack.AddState(buff, () => (state & 3) == 3);
            buff.FollowUpState = randomAttack;
            return new MonsterMoveStateMachine(
                [debilitate, bloodShots, echo, buff, randomAttack, postAttack], debilitate);
        }

        public override async Task AfterAddedToRoom()
        {
            await PowerCmd.Apply<Act4HeartMockBeatOfDeath>(
                new ThrowingPlayerChoiceContext(), Creature, AscensionHelper.GetValueIfAscension((MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel)9, 2, 1), Creature, null, false);
            await PowerCmd.Apply<Act4HeartMockInvincible>(
                new ThrowingPlayerChoiceContext(), Creature, AscensionHelper.GetValueIfAscension((MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel)9, 200m, 300m), Creature, null, false);
        }

        public override Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
            => Task.CompletedTask;

        private async Task DebilitateMove(IReadOnlyList<Creature> targets)
        {
            state = 0;
            await PowerCmd.Apply<VulnerablePower>(new ThrowingPlayerChoiceContext(), targets, 2m, Creature, null, false);
            await PowerCmd.Apply<WeakPower>(new ThrowingPlayerChoiceContext(), targets, 2m, Creature, null, false);
            await PowerCmd.Apply<FrailPower>(new ThrowingPlayerChoiceContext(), targets, 2m, Creature, null, false);
            foreach (Creature creature in targets)
            {
                Player player = creature.Player ?? creature.PetOwner
                    ?? throw new InvalidOperationException("虚弱打击的目标不是玩家。");
                await CardPileCmd.AddGeneratedCardToCombat(CombatState!.CreateCard<Dazed>(player), PileType.Draw, null, CardPilePosition.Random);
                await CardPileCmd.AddGeneratedCardToCombat(CombatState!.CreateCard<Slimed>(player), PileType.Draw, null, CardPilePosition.Random);
                await CardPileCmd.AddGeneratedCardToCombat(CombatState!.CreateCard<Wound>(player), PileType.Draw, null, CardPilePosition.Random);
                await CardPileCmd.AddGeneratedCardToCombat(CombatState!.CreateCard<Burn>(player), PileType.Draw, null, CardPilePosition.Random);
                await CardPileCmd.AddGeneratedCardToCombat(CombatState!.CreateCard<global::MegaCrit.Sts2.Core.Models.Cards.Void>(player), PileType.Draw, null, CardPilePosition.Random);
            }
        }

        private async Task BloodShotsMove(IReadOnlyList<Creature> targets)
        {
            state |= 1;
            await DamageCmd.Attack(2).WithHitCount(15).FromMonster(this).Execute(null);
        }

        private async Task EchoMove(IReadOnlyList<Creature> targets)
        {
            state |= 2;
            await DamageCmd.Attack(45).FromMonster(this).Execute(null);
        }

        private async Task BuffMove(IReadOnlyList<Creature> targets)
        {
            state = 0;
            if (Creature.GetPowerAmount<StrengthPower>() < 0)
                await PowerCmd.Remove<StrengthPower>(Creature);
            await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), Creature, 2m, Creature, null, false);
            buff_counter++;
            switch (buff_counter)
            {
                case 1:
                    await PowerCmd.Apply<ArtifactPower>(new ThrowingPlayerChoiceContext(), Creature, 2m, Creature, null, false);
                    break;
                case 2:
                    await PowerCmd.Apply<Act4HeartMockBeatOfDeath>(new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null, false);
                    break;
                case 3:
                    await PowerCmd.Apply<PainfulStabsPower>(new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null, false);
                    break;
                case 4:
                    await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), Creature, 10m, Creature, null, false);
                    break;
                default:
                    await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), Creature, 50m, Creature, null, false);
                    break;
            }
        }
    }

    private abstract class Act4HeartMockFinalePower : PowerModel
    {
        // 原 Mod 的 BeatOfDeathPower / InvinciblePower：Type=Buff(1)、StackType=Counter(1)。
        public override PowerType Type => (PowerType)1;

        public override PowerStackType StackType => (PowerStackType)1;
    }

    private sealed class Act4HeartMockBeatOfDeath : Act4HeartMockFinalePower
    {
        public override Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
        {
            Flash();
            return CreatureCmd.Damage(context, cardPlay.Card.Owner.Creature, Amount, ValueProp.Unpowered, Owner);
        }
    }

    private sealed class Act4HeartMockInvincible : Act4HeartMockFinalePower
    {
        internal int null_amount => (int)Math.Max(0m, Amount - data[null]);

        private Data data => GetInternalData<Data>();

        protected override object? InitInternalData() => new Data(split: false);

        public override decimal ModifyHpLostAfterOstyLate(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
        {
            if (target != Owner || amount == 0m)
                return amount;
            return Math.Min(amount, Amount - data[dealer]);
        }

        public override Task AfterDamageReceived(PlayerChoiceContext context, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
        {
            if (target == Owner && !result.WasFullyBlocked)
            {
                data[dealer] += result.UnblockedDamage;
                InvokeDisplayAmountChanged();
            }
            return Task.CompletedTask;
        }

        public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
        {
            if (side == Owner.Side)
            {
                data.Reset();
                InvokeDisplayAmountChanged();
                Owner.HpDisplay = HpDisplay.Normal;
            }
            return Task.CompletedTask;
        }

        private sealed class Data(bool split)
        {
            // 字段名与原 Mod 一致（含拼写），兼容层的根捕获按名反射读取。
            private readonly Dictionary<Creature, decimal>? damage_recieved_this_turn_split = split ? [] : null;

            private decimal damage_recieved_this_turn_shared;

            internal decimal this[Creature? dealer]
            {
                get
                {
                    if (damage_recieved_this_turn_split == null)
                        return damage_recieved_this_turn_shared;
                    dealer ??= RunManager.Instance.State!.Players[0].Creature;
                    return CollectionsMarshal.GetValueRefOrAddDefault(damage_recieved_this_turn_split, dealer, out _);
                }
                set
                {
                    if (damage_recieved_this_turn_split == null)
                    {
                        damage_recieved_this_turn_shared = value;
                        return;
                    }
                    dealer ??= RunManager.Instance.State!.Players[0].Creature;
                    damage_recieved_this_turn_split[dealer] = value;
                }
            }

            internal void Reset()
            {
                if (damage_recieved_this_turn_split != null)
                    damage_recieved_this_turn_split.Clear();
                else
                    damage_recieved_this_turn_shared = 0m;
            }
        }
    }
}
