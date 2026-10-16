using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using CombatSolver.Engine.Common;
using STS2RitsuLib;

namespace CombatSolver;

internal sealed class PredictionModHookSubscriberCapture
{
    private const string LoadoutMaxHandSizeModifierTypeName =
        "Loadout.Services.TildeKey.LoadoutMaxHandSizeModifier";
    private const string LoadoutEveryCardFreeCombatHookTypeName =
        "Loadout.Services.TildeKey.LoadoutEveryCardFreeCombatHook";
    private const string LoadoutPowerGiverSummonHookTypeName =
        "Loadout.Services.PowerGiver.PowerGiverSummonHook";
    private static readonly HashSet<string> KnownPreRootSubscriberTypeNames =
    [
        LoadoutMaxHandSizeModifierTypeName,
        LoadoutEveryCardFreeCombatHookTypeName,
        "Loadout.Services.TildeKey.LoadoutKillAllMonstersCombatHook",
        "Loadout.Services.PowerGiver.PowerGiverCombatStartHook",
    ];

    public AbstractModel[] RunSubscribers { get; }
    public AbstractModel[] CombatSubscribers { get; }
    public IReadOnlyDictionary<Player, int> MaxHandSizes { get; }
    public IReadOnlySet<Player> EveryCardFreePlayers { get; }
    public bool HasBaseLibCardModifiers { get; }
    public bool HasInactiveLoadoutSummonPowers { get; }
    // Capture validated the source and settings of every subscriber. These
    // Loadout hooks only configure pre-root state, hand limits or free costs;
    // an empty summon-power configuration cannot add powers later in combat.
    internal bool HasOnlyNonHealingLoadoutSubscribers
        => RunSubscribers.All(IsNonHealingLoadoutSubscriber)
            && CombatSubscribers.All(IsNonHealingLoadoutSubscriber);

    private bool IsNonHealingLoadoutSubscriber(AbstractModel subscriber)
    {
        Type type = subscriber.GetType();
        var source = AssemblyInfo.ModForType(type, out bool isBaseGame);
        return !isBaseGame && source?.manifest?.id == "Loadout"
            && type.FullName is { } name
            && (KnownPreRootSubscriberTypeNames.Contains(name)
                // This sealed hook only captures its snapshot in BeforeCombatStart.
                || name == "Loadout.Services.Morphing.BottledMonsterMorphCombatSnapshotHook"
                || name == LoadoutPowerGiverSummonHookTypeName && HasInactiveLoadoutSummonPowers);
    }
    public AdaptedOnPlaySnapshot? AdaptedOnPlay { get; private init; }

    private static readonly Guid AuditedLoadoutMvid = new("3f51fce1-7ec3-4116-b440-2c31eb754731");
    internal bool HasCertifiedNonHealingSubscribers { get; }
    internal bool IsCertifiedNonHealingSubscriberSource(AbstractModel source)
        => HasCertifiedNonHealingSubscribers && IsAuditedNonHealingSubscriberType(source.GetType());
    private bool IsAuditedNonHealingSubscriberType(Type type)
        => type.Module.ModuleVersionId == AuditedLoadoutMvid
            && type.FullName is { } name
            && (name is "Loadout.Services.Morphing.BottledMonsterMorphCombatSnapshotHook"
                or "Loadout.Services.PowerGiver.PowerGiverCombatStartHook"
                or "Loadout.Services.TildeKey.LoadoutKillAllMonstersCombatHook"
                or "Loadout.Services.TildeKey.LoadoutEveryCardFreeCombatHook"
                || name == LoadoutPowerGiverSummonHookTypeName && HasInactiveLoadoutSummonPowers);
    public MirroredHookListenerFilter MirroredHookFilter { get; } = MirroredHookListenerFilter.Capture();

    // 由适配层（当前只有 Act4HeartCompat）在审计通过后登记的已确认订阅者。
    // 键是精确 Type：适配层按 MVID 钉死了来源程序集，不同程序集的同类名类型不会命中；
    // 值是登记时的 Mod id，验证时仍要与运行时来源一致。
    private static readonly Dictionary<Type, string> ConfirmedGameplaySubscribers = [];

    /// <summary>
    /// 登记一个已通过结构审计的第三方 gameplay 订阅器，令 <see cref="ValidateSubscriber"/> 放行。
    /// 只允许在根捕获前调用；重复登记是幂等的空操作。
    /// </summary>
    internal static void RegisterConfirmedSubscriber(Type type, string modId)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!typeof(AbstractModel).IsAssignableFrom(type) || type.IsAbstract)
            throw new ArgumentException($"已确认订阅者必须是具体的 AbstractModel：{type.FullName}。", nameof(type));
        if (ConfirmedGameplaySubscribers.TryAdd(type, modId))
            Entry.Logger.Info($"[CombatSolver/Test] MOD_HOOK_SUBSCRIBER_CONFIRMED mod={modId} type={type.FullName}");
    }

    private PredictionModHookSubscriberCapture(
        AbstractModel[] runSubscribers,
        AbstractModel[] combatSubscribers,
        IReadOnlyDictionary<Player, int> maxHandSizes,
        IReadOnlySet<Player> everyCardFreePlayers,
        bool hasBaseLibCardModifiers,
        bool hasInactiveLoadoutSummonPowers)
    {
        RunSubscribers = runSubscribers;
        CombatSubscribers = combatSubscribers;
        MaxHandSizes = maxHandSizes;
        EveryCardFreePlayers = everyCardFreePlayers;
        HasBaseLibCardModifiers = hasBaseLibCardModifiers;
        HasInactiveLoadoutSummonPowers = hasInactiveLoadoutSummonPowers;
        HasCertifiedNonHealingSubscribers = !hasBaseLibCardModifiers
            && runSubscribers.Concat(combatSubscribers).All(source =>
                IsAuditedNonHealingSubscriberType(source.GetType()));
    }

    public static PredictionModHookSubscriberCapture Capture(
        RunState runState,
        CombatState combat)
    {
        AbstractModel[] runSubscribers = ModHelper.IterateAllRunStateSubscribers(runState).ToArray();
        AbstractModel[] combatSubscribers = ModHelper.IterateAllCombatStateSubscribers(combat).ToArray();
        bool hasBaseLibCardModifiers = combatSubscribers.Any(PredictionModModelSupport.IsBaseLibCardModifier);
        PredictionModModelSupport.RegisterBaseLibCardModifierSources(combatSubscribers);

        foreach (AbstractModel subscriber in runSubscribers)
            ValidateSubscriber(subscriber, "run");
        foreach (AbstractModel subscriber in combatSubscribers)
            ValidateSubscriber(subscriber, "combat");
        AdaptedOnPlaySnapshot? onPlay = PredictionModPatchAudit.CaptureCardOnPlay(EnumerateAuditableCards(runState, combat));

        Dictionary<Player, int> maxHandSizes = [];
        foreach (Player player in combat.Players)
        {
            int maxHandSize = RitsuLibFramework.GetMaxHandSize(player);
            if (maxHandSize < 0)
                throw new InvalidOperationException($"RitsuLib returned max hand size {maxHandSize}.");
            maxHandSizes.Add(player, maxHandSize);
        }

        IReadOnlySet<Player> everyCardFreePlayers = CaptureEveryCardFreePlayers(
            combatSubscribers,
            combat.Players);

        return new PredictionModHookSubscriberCapture(
            runSubscribers,
            combatSubscribers,
            maxHandSizes,
            everyCardFreePlayers,
            hasBaseLibCardModifiers,
            combatSubscribers.Any(subscriber =>
                subscriber.GetType().FullName == LoadoutPowerGiverSummonHookTypeName))
            { AdaptedOnPlay = onPlay };
    }

    public static string? CaptureLiveLoadoutSummonPowerState(CombatState combat)
    {
        AbstractModel? hook = ModHelper.IterateAllCombatStateSubscribers(combat)
            .SingleOrDefault(subscriber =>
                subscriber.GetType().FullName == LoadoutPowerGiverSummonHookTypeName);
        if (hook is null)
            return null;

        Type type = hook.GetType();
        var mod = AssemblyInfo.ModForType(type, out bool isBaseGame);
        var manifest = mod?.manifest;
        if (isBaseGame || manifest is null
            || !string.Equals(manifest.id, "Loadout", StringComparison.Ordinal))
            return "unknown_source";
        return ReadLoadoutMonsterPowerCounters(type).Count == 0 ? "empty" : "configured";
    }

    private static IReadOnlyDictionary<string, int> ReadLoadoutMonsterPowerCounters(Type hookType)
    {
        Assembly assembly = hookType.Assembly;
        Type scopeType = assembly.GetType("Loadout.Services.Targets.LoadoutTargetScope")
            ?? throw new PredictionUnsupportedException("Loadout PowerGiver target scope contract changed.");
        Type selectionType = assembly.GetType("Loadout.Services.Targets.LoadoutTargetSelection")
            ?? throw new PredictionUnsupportedException("Loadout PowerGiver target selection contract changed.");
        Type serviceType = assembly.GetType("Loadout.Services.PowerGiver.PowerGiverStateService")
            ?? throw new PredictionUnsupportedException("Loadout PowerGiver state service contract changed.");
        if (!scopeType.IsEnum || !Enum.IsDefined(scopeType, "AllMonsters"))
            throw new PredictionUnsupportedException("Loadout PowerGiver monster target contract changed.");
        ConstructorInfo constructor = selectionType.GetConstructor([scopeType, typeof(ulong?)])
            ?? throw new PredictionUnsupportedException("Loadout PowerGiver target selection contract changed.");
        MethodInfo snapshotMethod = serviceType.GetMethod(
            "GetCountersSnapshot", BindingFlags.Public | BindingFlags.Static,
            binder: null, types: [selectionType], modifiers: null)
            ?? throw new PredictionUnsupportedException("Loadout PowerGiver counter snapshot contract changed.");
        object target = constructor.Invoke([Enum.Parse(scopeType, "AllMonsters"), null]);
        return snapshotMethod.Invoke(null, [target]) as IReadOnlyDictionary<string, int>
            ?? throw new PredictionUnsupportedException("Loadout PowerGiver returned an unsupported counter snapshot.");
    }

    public void AppendCardAttachedListeners(
        IEnumerable<CardModel> cards,
        List<AbstractModel> listeners)
    {
        foreach (CardModel card in cards)
            PredictionModModelSupport.AppendCardAttachedListeners(card, listeners);
    }

    private static IReadOnlySet<Player> CaptureEveryCardFreePlayers(
        IReadOnlyList<AbstractModel> combatSubscribers,
        IReadOnlyList<Player> players)
    {
        AbstractModel? hook = combatSubscribers.SingleOrDefault(subscriber =>
            subscriber.GetType().FullName == LoadoutEveryCardFreeCombatHookTypeName);
        if (hook is null)
            return new HashSet<Player>();

        HashSet<Player> result = [];
        foreach (Player player in players)
        {
            CardModel probe = player.PlayerCombatState?.AllCards
                .FirstOrDefault(card => !card.IsCanonical)
                ?? throw new PredictionUnsupportedException(
                    $"Cannot capture Loadout every-card-free state for player {player.NetId} without a combat card.");
            const decimal originalCost = 1m;
            bool modified = hook.TryModifyEnergyCostInCombatLate(
                probe,
                originalCost,
                out decimal modifiedCost);
            if (modified)
            {
                if (modifiedCost != 0m)
                {
                    throw new PredictionUnsupportedException(
                        $"Loadout every-card-free hook returned unsupported cost {modifiedCost}.");
                }
                result.Add(player);
            }
            else if (modifiedCost != originalCost)
            {
                throw new PredictionUnsupportedException(
                    $"Loadout every-card-free hook changed an inactive cost to {modifiedCost}.");
            }
        }
        return result;
    }

    /// <summary>
    /// Every card type the root can reach without in-combat generation. Generated card types are not knowable at
    /// capture time, so they stay outside this audit.
    /// </summary>
    private static IEnumerable<CardModel> EnumerateAuditableCards(RunState runState, CombatState combat)
    {
        foreach (Player player in combat.Players)
        {
            foreach (CardModel card in player.PlayerCombatState?.AllCards ?? [])
                yield return card;
        }
        foreach (Player player in runState.Players)
        {
            foreach (CardModel card in player.Deck.Cards)
                yield return card;
        }
    }

    private static void ValidateSubscriber(
        AbstractModel subscriber,
        string scope)
    {
        Type type = subscriber.GetType();
        var mod = AssemblyInfo.ModForType(type, out bool isBaseGame);
        var manifest = mod?.manifest;
        if (type.FullName == LoadoutPowerGiverSummonHookTypeName
            && !isBaseGame
            && manifest is not null
            && string.Equals(manifest.id, "Loadout", StringComparison.Ordinal))
        {
            if (ReadLoadoutMonsterPowerCounters(type).Count == 0)
                return;
            throw new IncompatibleGameplayModException(
                "Loadout", manifest.name ?? string.Empty,
                "PowerGiver summon powers are configured",
                scope);
        }
        if (PredictionModModelSupport.IsBaseLibCardModifier(subscriber)
            || KnownPreRootSubscriberTypeNames.Contains(type.FullName ?? string.Empty)
            || (!isBaseGame && mod?.manifest?.affectsGameplay is false))
        {
            return;
        }
        // 只覆写了地图/进幕/事件/休息处/商店这类战斗外 hook 的订阅器不可能改变战斗模拟结果，
        // 不必把整个 Mod 判为不兼容。任何战斗 hook 覆写（含继承来的）仍走下面的拒绝路径。
        if (PredictionModHookSubscriberInertness.IsCombatInert(type, out string overriddenHooks))
        {
            Entry.Logger.Info(
                $"[CombatSolver/Test] MOD_HOOK_SUBSCRIBER_COMBAT_INERT scope={scope} " +
                $"mod={mod?.manifest?.id ?? "-"} type={type.FullName} hooks={overriddenHooks}");
            return;
        }
        // 适配层按 MVID 与结构钉死后确认的订阅者：其战斗效果已由镜像/域分支覆盖。
        if (ConfirmedGameplaySubscribers.TryGetValue(type, out string? confirmedModId)
            && !isBaseGame
            && string.Equals(mod?.manifest?.id, confirmedModId, StringComparison.Ordinal))
        {
            Entry.Logger.Info(
                $"[CombatSolver/Test] MOD_HOOK_SUBSCRIBER_CONFIRMED_PASS scope={scope} " +
                $"mod={mod?.manifest?.id ?? "-"} type={type.FullName}");
            return;
        }
        if (!isBaseGame
            && mod?.manifest?.id is { Length: > 0 } modId
            && !string.Equals(modId, Entry.ModId, StringComparison.OrdinalIgnoreCase))
        {
            throw new IncompatibleGameplayModException(
                modId,
                mod.manifest.name ?? string.Empty,
                $"ModHelper subscriber {type.FullName ?? type.Name}",
                scope);
        }
        throw new PredictionUnsupportedException(
            $"Unsupported gameplay ModHelper {scope} subscriber {type.FullName}.");
    }
}
