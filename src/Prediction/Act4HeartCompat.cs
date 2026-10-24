using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Damage;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.TurnEnd;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.TurnStart;
using CombatSolver.Engine.InCombat.Simulation;

namespace CombatSolver;

/// <summary>
/// Act 4 Heart（创意工坊 3747537811）内置兼容层。前三层只引入三类与求解器相关的内容：
/// GreenKeyHooks 订阅器（地图标记超精锐、战前 buff、战后奖励，均不在模拟窗口内）、
/// MetallicizePowerA4h（回合末早期获得格挡，等同原版 Plating 语义）和 RegeneratePowerA4h
/// （AfterSideTurnEnd 治疗且不递减）。第四层“终幕”包含三场战斗内容：SpireShield / SpireSpear
/// （矛与盾）与 CorruptHeart（腐化之心），各自的行动效果、私有 AI 字段、BeatOfDeathPower
/// （每次出牌对出牌者造成 Amount 伤害）与 InvinciblePower（按回合累计的掉血上限，语义等同
/// 原版 HardenedShell，但重置只发生在持有者一侧回合开始）在通过同一审计后由本层镜像。
///
/// 登记以双保险钉死：程序集 MVID 必须等于审计值，且各类型的 AbstractModel hook
/// 覆盖集必须与审计时完全一致（怪物另要求声明 GenerateMoveStateMachine）。
/// 任一检查失败则一个都不登记，回落到默认的“暂未适配”拒绝路径；Mod 更新后需要重新审计。
/// </summary>
internal static class Act4HeartCompat
{
    internal const string ModId = "Act4Heart";
    private const string ModAssemblyName = "Act4Heart";
    private const string MonsterMachineMethodName = "GenerateMoveStateMachine";

    /// <summary>Act4Heart 1.1.7（创意工坊 3747537811）审计时的程序集 MVID。</summary>
    private static readonly Guid AuditedModuleId = new("34cb40b4-09a2-48d4-8da0-09e7a0cc054a");

    private const string GreenKeyHooksTypeName = "Act4Heart.Keys.GreenKeyHooks";
    private const string MetallicizePowerTypeName = "Act4Heart.Powers.MetallicizePowerA4h";
    private const string RegeneratePowerTypeName = "Act4Heart.Powers.RegeneratePowerA4h";
    private const string SpireShieldTypeName = "Act4Heart.SpireShield";
    private const string SpireSpearTypeName = "Act4Heart.SpireSpear";
    private const string CorruptHeartTypeName = "Act4Heart.CorruptHeart";
    private const string BeatOfDeathPowerTypeName = "Act4Heart.Powers.BeatOfDeathPower";
    private const string InvinciblePowerTypeName = "Act4Heart.Powers.InvinciblePower";

    private static readonly SortedSet<string> ExpectedGreenKeyHooks =
        ["BeforeCombatStart", "ModifyGeneratedMapLate", "TryModifyRewards"];
    // PowerModel 本身覆写 AbstractModel 的克隆框架成员（AfterCloned/DeepCloneFields），
    // 每个 Power 子类的链上覆写集都会带上它们；第三方克隆走通用路径，故一并钉死。
    private static readonly SortedSet<string> ExpectedMetallicizeHooks =
        ["AfterCloned", "BeforeSideTurnEndEarly", "DeepCloneFields"];
    private static readonly SortedSet<string> ExpectedRegenerateHooks =
        ["AfterCloned", "AfterSideTurnEnd", "DeepCloneFields"];
    // SpireShield / SpireSpear 只覆写 MonsterModel 成员（MinInitialHp、GenerateMoveStateMachine、
    // AfterAddedToRoom 均不在 AbstractModel 上），hook 覆写集为空；行动结构由 MVID 钉死。
    private static readonly SortedSet<string> ExpectedEmptyHooks = [];
    private static readonly SortedSet<string> ExpectedCorruptHeartHooks = ["AfterDeath"];
    private static readonly SortedSet<string> ExpectedBeatOfDeathHooks =
        ["AfterCardPlayed", "AfterCloned", "DeepCloneFields"];
    private static readonly SortedSet<string> ExpectedInvincibleHooks =
        ["AfterCloned", "AfterDamageReceived", "BeforeSideTurnStart", "DeepCloneFields", "ModifyHpLostAfterOstyLate"];

    private static Type? _greenKeyHooksType;
    private static Type? _metallicizePowerType;
    private static Type? _regeneratePowerType;
    private static Type? _spireShieldType;
    private static Type? _spireSpearType;
    private static Type? _corruptHeartType;
    private static Type? _beatOfDeathPowerType;
    private static Type? _invinciblePowerType;
    private static bool _initializeAttempted;

    // 运行期配置读取（Act4Heart.ModMain.current_config -> Dolso.Config 字段）；真实程序集 Adopt 时解析。
    private static PropertyInfo? _modMainCurrentConfigProperty;
    private static FieldInfo? _shieldOddsField;
    private static FieldInfo? _doomFlagField;
    // 测试缝配置覆盖：测试宿主不加载真实 Mod，由夹具显式提供。
    private static float? _testShieldFocusDownOddsOverride;
    private static bool? _testDoomDamagesInsteadOfKillsOverride;

    /// <summary>审计失败的原因；仅由本类与测试缝抛出，<see cref="Adopt"/> 按未适配处理。</summary>
    internal sealed class Act4HeartAuditException(string reason) : Exception(reason);

    internal static bool IsAdopted => _greenKeyHooksType is not null;

    /// <summary>第四层已审计怪物的种类；按精确运行时类型判定。</summary>
    internal enum FinaleMonsterKind
    {
        None,
        SpireShield,
        SpireSpear,
        CorruptHeart,
    }

    /// <summary>在模型注册表初始化后调用：Mod 已安装则按审计钉死值登记。每个进程只尝试一次；
    /// 未安装时保持未登记（订阅者门禁自然拒绝 GreenKeyHooks，但该 Mod 未安装时也不会出现）。
    /// </summary>
    internal static void Initialize()
    {
        if (_initializeAttempted)
            return;
        _initializeAttempted = true;
        Assembly? candidate = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly =>
            string.Equals(assembly.GetName().Name, ModAssemblyName, StringComparison.Ordinal));
        if (candidate is null)
            return;
        Adopt(candidate, AuditedModuleId);
    }

    /// <summary>
    /// 按给定 MVID 审计并登记候选程序集。MVID 不匹配、类型缺失、结构变化都按未适配处理；
    /// 未预期的异常继续传播（见仓库错误处理约束）。
    /// </summary>
    internal static void Adopt(Assembly assembly, Guid expectedModuleId)
    {
        try
        {
            if (assembly.ManifestModule.ModuleVersionId != expectedModuleId)
            {
                throw new Act4HeartAuditException(
                    $"MVID {assembly.ManifestModule.ModuleVersionId} 与审计值 {expectedModuleId} 不一致。");
            }
            ResolveConfigAccessors(assembly);
            RegisterAuditedTypes(new Act4HeartAdoptedTypes(
                ResolveHookType(assembly, GreenKeyHooksTypeName, typeof(AbstractModel)),
                ResolveHookType(assembly, MetallicizePowerTypeName, typeof(PowerModel)),
                ResolveHookType(assembly, RegeneratePowerTypeName, typeof(PowerModel)),
                ResolveHookType(assembly, SpireShieldTypeName, typeof(MonsterModel)),
                ResolveHookType(assembly, SpireSpearTypeName, typeof(MonsterModel)),
                ResolveHookType(assembly, CorruptHeartTypeName, typeof(MonsterModel)),
                ResolveHookType(assembly, BeatOfDeathPowerTypeName, typeof(PowerModel)),
                ResolveHookType(assembly, InvinciblePowerTypeName, typeof(PowerModel))));
        }
        catch (Act4HeartAuditException ex)
        {
            Entry.Logger.Warn(
                $"[CombatSolver/Act4Heart] AUDIT_REJECTED mvid={assembly.ManifestModule.ModuleVersionId} reason={ex.Message}");
        }
    }

    /// <summary>测试缝：跳过程序集发现与 MVID（mock 类型位于测试程序集），但执行同样的结构自检并登记。
    /// 只允许在 mock 类型尚未登记时调用一次；复用进程里由测试自行跳过。
    /// </summary>
    internal static void AdoptForTesting(Act4HeartAdoptedTypes types)
        => RegisterAuditedTypes(types);

    /// <summary>测试缝：只执行与登记完全相同的结构自检，不写入任何登记表。
    /// 供复用进程在已登记状态下验证审计拒绝路径（含 hook 覆盖集与出招表声明检查）。</summary>
    internal static void VerifyAuditedTypesForTesting(Act4HeartAdoptedTypes types)
    {
        VerifyHookOverrides(types.GreenKeyHooks, ExpectedGreenKeyHooks, GreenKeyHooksTypeName);
        VerifyHookOverrides(types.Metallicize, ExpectedMetallicizeHooks, MetallicizePowerTypeName);
        VerifyHookOverrides(types.Regenerate, ExpectedRegenerateHooks, RegeneratePowerTypeName);
        VerifyHookOverrides(types.SpireShield, ExpectedEmptyHooks, SpireShieldTypeName);
        VerifyHookOverrides(types.SpireSpear, ExpectedEmptyHooks, SpireSpearTypeName);
        VerifyHookOverrides(types.CorruptHeart, ExpectedCorruptHeartHooks, CorruptHeartTypeName);
        VerifyHookOverrides(types.BeatOfDeath, ExpectedBeatOfDeathHooks, BeatOfDeathPowerTypeName);
        VerifyHookOverrides(types.Invincible, ExpectedInvincibleHooks, InvinciblePowerTypeName);
        VerifyMonsterMachine(types.SpireShield, SpireShieldTypeName);
        VerifyMonsterMachine(types.SpireSpear, SpireSpearTypeName);
        VerifyMonsterMachine(types.CorruptHeart, CorruptHeartTypeName);
    }

    /// <summary>测试缝：为不加载真实 Mod 的宿主显式提供两项行为开关的当前值。
    /// 覆盖值按次设置即时生效；传 null 清除，恢复真实程序集读取路径。</summary>
    internal static void SetTestConfigOverridesForTesting(float? shieldFocusDownOdds, bool? doomDamagesInsteadOfKills)
    {
        _testShieldFocusDownOddsOverride = shieldFocusDownOdds;
        _testDoomDamagesInsteadOfKillsOverride = doomDamagesInsteadOfKills;
    }

    internal sealed record Act4HeartAdoptedTypes(
        Type GreenKeyHooks,
        Type Metallicize,
        Type Regenerate,
        Type SpireShield,
        Type SpireSpear,
        Type CorruptHeart,
        Type BeatOfDeath,
        Type Invincible);

    private static Type ResolveHookType(Assembly assembly, string typeName, Type baseType)
    {
        Type? type = assembly.GetType(typeName);
        if (type is null)
            throw new Act4HeartAuditException($"类型 {typeName} 不存在。");
        if (type.IsAbstract || !baseType.IsAssignableFrom(type))
            throw new Act4HeartAuditException($"类型 {typeName} 不是具体的 {baseType.Name}。");
        return type;
    }

    private static void RegisterAuditedTypes(Act4HeartAdoptedTypes types)
    {
        if (IsAdopted)
            throw new Act4HeartAuditException("兼容层已登记，拒绝重复登记。");
        VerifyHookOverrides(types.GreenKeyHooks, ExpectedGreenKeyHooks, GreenKeyHooksTypeName);
        VerifyHookOverrides(types.Metallicize, ExpectedMetallicizeHooks, MetallicizePowerTypeName);
        VerifyHookOverrides(types.Regenerate, ExpectedRegenerateHooks, RegeneratePowerTypeName);
        VerifyHookOverrides(types.SpireShield, ExpectedEmptyHooks, SpireShieldTypeName);
        VerifyHookOverrides(types.SpireSpear, ExpectedEmptyHooks, SpireSpearTypeName);
        VerifyHookOverrides(types.CorruptHeart, ExpectedCorruptHeartHooks, CorruptHeartTypeName);
        VerifyHookOverrides(types.BeatOfDeath, ExpectedBeatOfDeathHooks, BeatOfDeathPowerTypeName);
        VerifyHookOverrides(types.Invincible, ExpectedInvincibleHooks, InvinciblePowerTypeName);
        VerifyMonsterMachine(types.SpireShield, SpireShieldTypeName);
        VerifyMonsterMachine(types.SpireSpear, SpireSpearTypeName);
        VerifyMonsterMachine(types.CorruptHeart, CorruptHeartTypeName);

        PredictionModHookSubscriberCapture.RegisterConfirmedSubscriber(types.GreenKeyHooks, ModId);
        BeforeSideTurnEndMirrors.RegisterEarlyMirror(types.Metallicize, HandleMetallicizeMirror);
        AfterCardPlayedMirrors.RegisterMirror(types.BeatOfDeath, HandleBeatOfDeath);
        AfterDamageReceivedMirrors.RegisterMirror(types.Invincible, HandleInvincibleAfterDamageReceived);
        ModifyHpLostMirrors.RegisterAfterOstyLateMirror(types.Invincible, HandleInvincibleModifyHpLost);
        BeforeSideTurnStartMirrors.Register(types.Invincible, HandleInvincibleBeforeSideTurnStart);
        // BUFF_MOVE 会在搜索中给已存在的死亡之搏叠加层数，叠加路径克隆规范实例并读取其
        // DynamicVars；规范实例必须在主线程建根时提前物化。
        PowerDynamicVarWarmup.RegisterAdaptedCanonicalPower(types.BeatOfDeath);
        _greenKeyHooksType = types.GreenKeyHooks;
        _metallicizePowerType = types.Metallicize;
        _regeneratePowerType = types.Regenerate;
        _spireShieldType = types.SpireShield;
        _spireSpearType = types.SpireSpear;
        _corruptHeartType = types.CorruptHeart;
        _beatOfDeathPowerType = types.BeatOfDeath;
        _invinciblePowerType = types.Invincible;
        Entry.Logger.Info(
            $"[CombatSolver/Act4Heart] ADOPTED green={types.GreenKeyHooks.FullName} " +
            $"metallicize={types.Metallicize.FullName} regenerate={types.Regenerate.FullName} " +
            $"shield={types.SpireShield.FullName} spear={types.SpireSpear.FullName} " +
            $"heart={types.CorruptHeart.FullName} beat={types.BeatOfDeath.FullName} " +
            $"invincible={types.Invincible.FullName}");
    }

    private static void VerifyHookOverrides(Type type, SortedSet<string> expected, string typeName)
    {
        SortedSet<string> actual = PredictionModHookSubscriberInertness.CollectHookOverrides(type);
        if (!actual.SetEquals(expected))
        {
            throw new Act4HeartAuditException(
                $"{typeName} 的 hook 覆盖集（{string.Join(",", actual)}）与审计值（{string.Join(",", expected)}）不一致。");
        }
    }

    private static void VerifyMonsterMachine(Type type, string typeName)
    {
        if (AccessTools.DeclaredMethod(type, MonsterMachineMethodName) is null)
            throw new Act4HeartAuditException($"{typeName} 没有声明 {MonsterMachineMethodName}。");
    }

    private static void ResolveConfigAccessors(Assembly assembly)
    {
        Type? modMain = assembly.GetType("Act4Heart.ModMain")
            ?? throw new Act4HeartAuditException("类型 Act4Heart.ModMain 不存在。");
        _modMainCurrentConfigProperty = modMain.GetProperty(
            "current_config",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new Act4HeartAuditException("Act4Heart.ModMain.current_config 不存在。");
        Type? configType = assembly.GetType("Dolso.Config")
            ?? throw new Act4HeartAuditException("类型 Dolso.Config 不存在。");
        const BindingFlags fieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        _shieldOddsField = configType.GetField("spire_shield_orbs_focus_down_odds", fieldFlags)
            ?? throw new Act4HeartAuditException("Dolso.Config.spire_shield_orbs_focus_down_odds 不存在。");
        _doomFlagField = configType.GetField("heart_doom_damages_instead_of_kills", fieldFlags)
            ?? throw new Act4HeartAuditException("Dolso.Config.heart_doom_damages_instead_of_kills 不存在。");
    }

    private static object ReadCurrentConfig()
    {
        if (_testShieldFocusDownOddsOverride is not null || _testDoomDamagesInsteadOfKillsOverride is not null)
            throw new InvalidOperationException("测试配置覆盖存在时不应读取真实 Act4Heart 配置。");
        if (_modMainCurrentConfigProperty is null)
            throw new InvalidOperationException("Act4Heart 配置读取器未初始化。");
        return _modMainCurrentConfigProperty.GetValue(null)
            ?? throw new InvalidOperationException("Act4Heart 当前配置为空。");
    }

    /// <summary>SpireShield 猛击对有球玩家降低专注的概率（原 Mod 配置，根捕获时读取）。</summary>
    internal static float ReadShieldFocusDownOdds()
    {
        if (_testShieldFocusDownOddsOverride is { } testOdds)
            return testOdds;
        return (float)_shieldOddsField!.GetValue(ReadCurrentConfig())!;
    }

    /// <summary>末日改为伤害而非击杀（原 Mod 配置，根捕获时读取；默认关闭）。</summary>
    internal static bool ReadDoomDamagesInsteadOfKills()
    {
        if (_testDoomDamagesInsteadOfKillsOverride is { } testFlag)
            return testFlag;
        return (bool)_doomFlagField!.GetValue(ReadCurrentConfig())!;
    }

    /// <summary>等价 PlatingPower.BeforeSideTurnEndEarly：参与方的持有者获得 Amount 点格挡（ValueProp 4）。</summary>
    private static void HandleMetallicizeMirror(AbstractModel model, BeforeSideTurnEndMirrorContext context)
    {
        var power = (PowerModel)model;
        if (context.Participants.Contains(power.Owner))
            context.Simulator.GainBlock(power.Owner, power.Amount, ValueProp.Unpowered);
    }

    /// <summary>等价 BeatOfDeathPower.AfterCardPlayed：出牌者的生物受到 Amount 点伤害
    /// （ValueProp.Unpowered，伤害来源为 Power 持有者；Flash 与音效为表现层，不镜像）。</summary>
    private static void HandleBeatOfDeath(AbstractModel model, AfterCardPlayedMirrorContext context)
    {
        var power = (PowerModel)model;
        context.Simulator.Damage(
            context.PreviewCard.Owner.Creature,
            power.Amount,
            ValueProp.Unpowered,
            power.Owner);
    }

    /// <summary>等价 InvinciblePower.ModifyHpLostAfterOstyLate：单次掉血被钳制在本回合剩余额度内。</summary>
    private static decimal HandleInvincibleModifyHpLost(AbstractModel model, ModifyHpLostMirrorContext context)
    {
        var power = (PowerModel)model;
        if (context.Target != power.Owner || context.Amount == 0m)
            return context.Amount;
        Act4HeartInvinciblePredictionState state = context.StateStore.Get(
            power, static value => CreateInvinciblePredictionState(value));
        return Math.Min(context.Amount, power.Amount - state.DamageReceivedThisTurn);
    }

    /// <summary>等价 InvinciblePower.AfterDamageReceived：按未阻挡伤害累计本回合额度，
    /// 额度用尽后血条切换为无限显示（HpDisplay=1 与原版 HardenedShell 相同）。</summary>
    private static void HandleInvincibleAfterDamageReceived(AbstractModel model, AfterDamageReceivedMirrorContext context)
    {
        var power = (PowerModel)model;
        if (context.Target != power.Owner || context.Result.WasFullyBlocked)
            return;
        Act4HeartInvinciblePredictionState state = context.StateStore.Get(
            power, static value => CreateInvinciblePredictionState(value));
        state.DamageReceivedThisTurn += context.Result.UnblockedDamage;
        if (state.DamageReceivedThisTurn >= power.Amount)
            context.State.GetCreature(power.Owner).HpDisplay = HpDisplay.InfiniteWithNumbers;
    }

    /// <summary>等价 InvinciblePower.BeforeSideTurnStart：持有者一侧回合开始时清空本回合额度
    /// 并恢复正常血条显示（与原版 HardenedShell 不同，这里带 side 检查）。</summary>
    private static void HandleInvincibleBeforeSideTurnStart(AbstractModel model, BeforeSideTurnStartMirrorContext context)
    {
        var power = (PowerModel)model;
        if (context.Side != power.Owner.Side)
            return;
        Act4HeartInvinciblePredictionState state = context.Simulator.StateStore.Get(
            power, static value => CreateInvinciblePredictionState(value));
        state.DamageReceivedThisTurn = 0;
        context.State.GetCreature(power.Owner).HpDisplay = HpDisplay.Normal;
    }

    /// <summary>是否为已审计的 MetallicizePowerA4h（精确类型匹配；Mod 更新引入子类时不匹配）。</summary>
    internal static bool IsMetallicizePower(PowerModel power)
        => _metallicizePowerType is { } type && power.GetType() == type;

    /// <summary>是否为已审计的 RegeneratePowerA4h（精确类型匹配；Mod 更新引入子类时不匹配）。</summary>
    internal static bool IsRegeneratePower(PowerModel power)
        => _regeneratePowerType is { } type && power.GetType() == type;

    /// <summary>是否为已审计的 InvinciblePower（精确类型匹配）。</summary>
    internal static bool IsInvinciblePower(PowerModel power)
        => _invinciblePowerType is { } type && power.GetType() == type;

    /// <summary>已审计 BeatOfDeathPower 的运行时类型；未登记时为 null。</summary>
    internal static Type? BeatOfDeathPowerType => _beatOfDeathPowerType;

    /// <summary>是否为已审计的第四层怪物（精确类型匹配；供怪物门禁放行判定）。</summary>
    internal static bool IsAdoptedFinaleMonster(Type type)
        => type == _spireShieldType || type == _spireSpearType || type == _corruptHeartType;

    /// <summary>已审计第四层怪物的种类；按精确运行时类型判定。</summary>
    internal static FinaleMonsterKind GetFinaleMonsterKind(MonsterModel monster)
    {
        Type type = monster.GetType();
        if (type == _spireShieldType)
            return FinaleMonsterKind.SpireShield;
        if (type == _spireSpearType)
            return FinaleMonsterKind.SpireSpear;
        if (type == _corruptHeartType)
            return FinaleMonsterKind.CorruptHeart;
        return FinaleMonsterKind.None;
    }

    /// <summary>从实机 Power 实例读取 InvinciblePower 的内部本回合累计伤害（单人共享计数）。
    /// 多人分池计数不受支持，显式失败。</summary>
    private static decimal ReadLiveInvincibleDamageReceived(PowerModel livePower)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object? data = typeof(PowerModel).GetField("_internalData", flags)?.GetValue(livePower);
        if (data is null)
            return 0m;
        FieldInfo? splitField = data.GetType().GetField("damage_recieved_this_turn_split", flags);
        if (splitField?.GetValue(data) is not null)
            throw new InvalidOperationException(
                "InvinciblePower 的多人分池计数不受单人求解器支持。");
        FieldInfo? sharedField = data.GetType().GetField("damage_recieved_this_turn_shared", flags)
            ?? throw new InvalidOperationException("InvinciblePower 内部状态缺少共享计数字段。");
        return (decimal)sharedField.GetValue(data)!;
    }

    /// <summary>为分支 Power 实例创建预测状态。仅在根捕获（实机实例）与兜底路径（分支克隆，
    /// 其内部数据已被克隆重置为初值）上调用；分支内正常路径读取的是已 Fork 的状态条目。</summary>
    internal static Act4HeartInvinciblePredictionState CreateInvinciblePredictionState(PowerModel power)
        => new(ReadLiveInvincibleDamageReceived(power));

    /// <summary>敌方回合末域代码读取的 A4h 金属化格挡总量（同持有者多实例求和）。</summary>
    internal static int EnemyMetallicizeAmount(SimulatedCombatState combat, Creature enemy)
    {
        if (_metallicizePowerType is not { } type)
            return 0;
        int total = 0;
        foreach (PowerModel power in combat.EffectivePowers())
        {
            if (power.Amount > 0 && power.GetType() == type && ReferenceEquals(power.Owner, enemy))
                total += power.Amount;
        }
        return total;
    }
}

/// <summary>InvinciblePower 的分支状态：本回合已承受的未阻挡伤害（单人共享计数）。</summary>
internal sealed class Act4HeartInvinciblePredictionState(decimal damageReceivedThisTurn)
    : IPredictionStateForkable
{
    public decimal DamageReceivedThisTurn { get; set; } = damageReceivedThisTurn;

    public object Fork(PredictionForkContext context) => MemberwiseClone();
}
