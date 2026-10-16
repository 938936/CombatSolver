using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.TurnEnd;
using CombatSolver.Engine.InCombat.Simulation;

namespace CombatSolver;

/// <summary>
/// Act 4 Heart（创意工坊 3747537811）前三层兼容层。该 Mod 在前三层只引入三类与求解器
/// 相关的内容：GreenKeyHooks 订阅器（地图标记超精锐、战前 buff、战后奖励，均不在模拟窗口内）、
/// MetallicizePowerA4h（回合末早期获得格挡，等同原版 Plating 语义）和 RegeneratePowerA4h
/// （AfterSideTurnEnd 治疗且不递减）。第四层怪物仍走既有的第三方怪物拒绝路径。
///
/// 登记以双保险钉死：程序集 MVID 必须等于审计值，且三类类型的 AbstractModel hook
/// 覆盖集必须与审计时完全一致。任一检查失败则一个都不登记，回落到默认的
/// “暂未适配”拒绝路径；Mod 更新后需要重新审计。
/// </summary>
internal static class Act4HeartCompat
{
    internal const string ModId = "Act4Heart";
    private const string ModAssemblyName = "Act4Heart";

    /// <summary>Act4Heart 1.1.7（创意工坊 3747537811）审计时的程序集 MVID。</summary>
    private static readonly Guid AuditedModuleId = new("34cb40b4-09a2-48d4-8da0-09e7a0cc054a");

    private const string GreenKeyHooksTypeName = "Act4Heart.Keys.GreenKeyHooks";
    private const string MetallicizePowerTypeName = "Act4Heart.Powers.MetallicizePowerA4h";
    private const string RegeneratePowerTypeName = "Act4Heart.Powers.RegeneratePowerA4h";

    private static readonly SortedSet<string> ExpectedGreenKeyHooks =
        ["BeforeCombatStart", "ModifyGeneratedMapLate", "TryModifyRewards"];
    // PowerModel 本身覆写 AbstractModel 的克隆框架成员（AfterCloned/DeepCloneFields），
    // 每个 Power 子类的链上覆写集都会带上它们；第三方克隆走通用路径，故一并钉死。
    private static readonly SortedSet<string> ExpectedMetallicizeHooks =
        ["AfterCloned", "BeforeSideTurnEndEarly", "DeepCloneFields"];
    private static readonly SortedSet<string> ExpectedRegenerateHooks =
        ["AfterCloned", "AfterSideTurnEnd", "DeepCloneFields"];

    private static Type? _greenKeyHooksType;
    private static Type? _metallicizePowerType;
    private static Type? _regeneratePowerType;
    private static bool _initializeAttempted;

    /// <summary>审计失败的原因；仅由本类与测试缝抛出，<see cref="Adopt"/> 按未适配处理。</summary>
    internal sealed class Act4HeartAuditException(string reason) : Exception(reason);

    internal static bool IsAdopted => _greenKeyHooksType is not null;

    /// <summary>
    /// 在模型注册表初始化后调用：Mod 已安装则按审计钉死值登记。每个进程只尝试一次；
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
            RegisterAuditedTypes(
                ResolveHookType(assembly, GreenKeyHooksTypeName, typeof(AbstractModel)),
                ResolveHookType(assembly, MetallicizePowerTypeName, typeof(PowerModel)),
                ResolveHookType(assembly, RegeneratePowerTypeName, typeof(PowerModel)));
        }
        catch (Act4HeartAuditException ex)
        {
            Entry.Logger.Warn(
                $"[CombatSolver/Act4Heart] AUDIT_REJECTED mvid={assembly.ManifestModule.ModuleVersionId} reason={ex.Message}");
        }
    }

    /// <summary>
    /// 测试缝：跳过程序集发现与 MVID（mock 类型位于测试程序集），但执行同样的结构自检并登记。
    /// 只允许在 mock 类型尚未登记时调用一次；复用进程里由测试自行跳过。
    /// </summary>
    internal static void AdoptForTesting(Type greenKeyHooks, Type metallicize, Type regenerate)
        => RegisterAuditedTypes(greenKeyHooks, metallicize, regenerate);

    private static Type ResolveHookType(Assembly assembly, string typeName, Type baseType)
    {
        Type? type = assembly.GetType(typeName);
        if (type is null)
            throw new Act4HeartAuditException($"类型 {typeName} 不存在。");
        if (type.IsAbstract || !baseType.IsAssignableFrom(type))
            throw new Act4HeartAuditException($"类型 {typeName} 不是具体的 {baseType.Name}。");
        return type;
    }

    private static void RegisterAuditedTypes(
        Type greenKeyHooks,
        Type metallicize,
        Type regenerate)
    {
        if (IsAdopted)
            throw new Act4HeartAuditException("兼容层已登记，拒绝重复登记。");
        VerifyHookOverrides(greenKeyHooks, ExpectedGreenKeyHooks, GreenKeyHooksTypeName);
        VerifyHookOverrides(metallicize, ExpectedMetallicizeHooks, MetallicizePowerTypeName);
        VerifyHookOverrides(regenerate, ExpectedRegenerateHooks, RegeneratePowerTypeName);

        PredictionModHookSubscriberCapture.RegisterConfirmedSubscriber(greenKeyHooks, ModId);
        BeforeSideTurnEndMirrors.RegisterEarlyMirror(metallicize, HandleMetallicizeMirror);
        _greenKeyHooksType = greenKeyHooks;
        _metallicizePowerType = metallicize;
        _regeneratePowerType = regenerate;
        Entry.Logger.Info(
            $"[CombatSolver/Act4Heart] ADOPTED green={greenKeyHooks.FullName} " +
            $"metallicize={metallicize.FullName} regenerate={regenerate.FullName}");
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

    /// <summary>等价 PlatingPower.BeforeSideTurnEndEarly：参与方的持有者获得 Amount 点格挡（ValueProp 4）。</summary>
    private static void HandleMetallicizeMirror(AbstractModel model, BeforeSideTurnEndMirrorContext context)
    {
        var power = (PowerModel)model;
        if (context.Participants.Contains(power.Owner))
            context.Simulator.GainBlock(power.Owner, power.Amount, ValueProp.Unpowered);
    }

    /// <summary>是否为已审计的 MetallicizePowerA4h（精确类型匹配；Mod 更新引入子类时不匹配）。</summary>
    internal static bool IsMetallicizePower(PowerModel power)
        => _metallicizePowerType is { } type && power.GetType() == type;

    /// <summary>是否为已审计的 RegeneratePowerA4h（精确类型匹配；Mod 更新引入子类时不匹配）。</summary>
    internal static bool IsRegeneratePower(PowerModel power)
        => _regeneratePowerType is { } type && power.GetType() == type;

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
