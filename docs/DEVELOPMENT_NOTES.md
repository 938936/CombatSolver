# CombatSolver 开发笔记

这里只记录当前未发布的行为变化。发布定稿后将该批次完整移入历史卷。

0.50.1 本地定稿的智能药水修复及 PR #213、#224、#226 见[历史卷22](archive/development/volume-22.md)，玩家说明见[0.50.1 更新日志](releases/0.50.1-RELEASE_NOTES.md)。外部发布状态以对应标签及发布记录为准。

0.50.0 的 PR #207、PR #211、智能药水与组合补搜定稿及性能研究见[历史卷20](archive/development/volume-20.md)。其他记录见[归档索引](archive/development/README.md)。

## 下一版本（开发中）

### Q015 路线保留（PR #227）

在既有 Beam 名额内，按用药数量保留已降低敌方格挡、仍有合法攻击且敌方剩余生命不超过可执行手牌估值的收尾续路；合法攻击数沿现有手牌遍历冻结。该估值用于保路资格，实际攻击由模拟器结算。Discard／DiscardAndDraw 上下文优先保留预计安全的进攻续路。Control 在原有结束回合探测名额中保留一个可能由延迟伤害获胜的代表，再由实际试算决定。旧敌退出且新敌入场的 AliveEnemyMask 变化进入 RevivalWindow 通道。状态等价、终局政策、预算和战斗语义沿用原实现。

Testing 在完整原生检查点已核验、当前手牌上限为默认 10 时迁移旧报告缺失的字段，其他续用字段继续严格比较。录制预测路径诊断支持显式指定候选池观察步数。贡献者的五主题自主搜索、原生部署、固定哨兵及未验证范围见[Q015 记录](issues/q015-route-quality.md)。

### 计划续搜复用完整胜利证明（PR #229）

严格组件认证根将已有及首成员新发现的完整胜利传给后续计划，沿原回复上界与政策门禁剪枝。Runtime 在重建 NoGC 的检查点请求一次压缩完整回收，确认新的完成信息与弱引用哨兵后重建区域。贡献者的 Linux 11 根交错性能数据、原生合同、失败及平台限制见[性能记录](performance/plan-witness-propagation-20261006.md)。

### Smart 药水审计独立成员时间（PR #230）

Smart 无强制用药、主结果无药且根有可搜索药水时，在主搜索耗尽时间后为首个可选药水层提供软预算的 1/5（1–30 秒）。该层按软预算保留结果，关联取消令牌提供 1/6 收尾时间，显式层数上限固定为一层；其他政策继续沿原入口执行。后置开局前缀补搜使用请求账本。已耗尽请求会增加这一层的实际耗时。根因、原包数据与验证范围见[Q011 记录](issues/q011-worldlines-20261006.md)。

### Act 4 Heart 前三层兼容层

为 Act 4 Heart（创意工坊 3747537811，1.1.7）添加第一方审计适配，求解器在该 Mod 安装时可正常求解前三层战斗；第四层怪物维持既有的第三方怪物显式拒绝。装 Mod 后前三层实际引入的第三方内容与处理：

- `GreenKeyHooks` 订阅器（地图标记超精英、战前 buff、战后奖励）：`TryModifyRewards` 不在惰性清单内，此前是整体拒绝的唯一硬阻塞。新增 `PredictionModHookSubscriberCapture.RegisterConfirmedSubscriber(Type, modId)` 确认表，由适配层在审计通过后登记；验证时同时核对运行时来源 Mod id。
- `MetallicizePowerA4h`（`BeforeSideTurnEndEarly` 获得格挡，IL 逐指令比对与原版 Plating 等价，ValueProp 4）：玩家侧经 `BeforeSideTurnEndMirrors.RegisterEarlyMirror(Type, handler)` 按监听顺序派发（消除伪 MethodNotMirrored 风险）；敌方回合末域代码 `CorePowerSupport.TriggerEnemySideTurnEndEffects` 在 Plating 之后结算同相位格挡。
- `RegeneratePowerA4h`（`AfterSideTurnEnd` 治疗且**不递减**）：`EndTurnPowerSupport.TriggerRegular` 在监听器循环内该 Power 自己的位置增加精确类型分支，双侧通用。

审计双保险：程序集 MVID 钉死（`34cb40b4-09a2-48d4-8da0-09e7a0cc054a`）+ 三类型的 AbstractModel hook 覆盖集精确匹配（Power 子类的链上覆写集包含原版 `PowerModel` 的克隆框架成员 `AfterCloned`/`DeepCloneFields`）。任一失败一个都不登记并回落到默认「暂未适配」拒绝；Mod 更新后需重新审计。登记在 `Entry` 的 `ModelRegistryInitializedEvent`（或注册表已冻结）后完成，早于首个根捕获。`PredictionModHookSubscriberInertness` 抽出共享的 `CollectHookOverrides` 作为覆盖集权威实现。

已知近似（策略层非模拟层，记录不修）：`ActEndingBossPolicy.ResolveHpRelief` 读 live `Acts.Count`，装 Mod 后第三幕首领自动按 ActClearHeal（保留 20% 治疗价值）处理，而 Mod 实际进入第四层并回复 100%（无 A2）/75%（A2，保留 0%/25%）。装 Mod 的第三层首领收尾会偏保守，属可接受的策略近似，不做 Mod 专属策略。`PotionRewardOutlook` 同样读 live 值自适应。

验证：`ACT4HEART-COMPAT` 无头场景（mock 同构类型经测试缝走同一审计与登记路径）覆盖结构自检拒绝、MVID 拒绝、未适配门禁拒绝、登记后放行、金属化/再生模拟 vs 实机严格差分、再生不递减、玩家侧 Early 分发无未镜像风险，并完整跑通当回合搜索与部署；`MIRRORED-HOOK-FILTER`、`SMOKE-001`＋`-VerifyPredictionFailureBoundaries` 回归通过。结构门禁通过。测试宿主不安装真实 Mod；真实程序集的 MVID/结构路径只能由审计常量本身约束，未在真机验证。

### Act 4 Heart 第四层「终幕」内容适配

在同一 MVID 审计层内扩展第四层三怪的完整模拟支持（用户显式要求）。`ValidateMonsterModels` 对已审计的 `SpireShield`/`SpireSpear`/`CorruptHeart` 精确类型放行怪物来源门禁（其 `GenerateMoveStateMachine` 上的外来补丁仍拒绝），行动效果进入 `MonsterMoveEffects`（按 `Act4HeartCompat.GetFinaleMonsterKind` 的已审计类型分派，不用类型名，避免测试 mock 与真实 Mod 类型同名冲突）：

- 怪物私有 AI 字段（`state` 字节、腐化之心的 `buff_counter`）按 `GetMonsterInt` 惯例进 `_monsterIntStates`：`MaterializeRoot` 经 `DescribePredictedMonsterState` 惰性捕获，Fork 隔离，进入状态指纹与续用 MS 段。三怪的 `POST_ATTACK_BRANCH` 在 `BranchMonsterAi.ResolveConditional` 按分支 `state` 重解析（先补第一攻击位、再补第二位、双齐转重置行动）。
- `SpireShield` 猛击对有球玩家读取怪物自带 `MonsterModel.Rng`（原版仅外观用，该 Mod 用作战斗判定）。根捕获时把实机实例克隆进新的 `_monsterCombatRngs`（`SimulatedCombatState` 成员），Fork 逐条深拷贝，镜像在消耗点调用 `NextFloat(1f)`；状态进指纹（`AppendMonsterStateFingerprint` 的 `r` 段）与续用描述。降专注概率 `spire_shield_orbs_focus_down_odds` 根捕获时经反射读取 `Act4Heart.ModMain.current_config`（测试缝显式覆盖）。重击非 A9 获得等于攻击总伤害的格挡（`MonsterMoveEffects.Apply` 新增 `attackTotalDamage` 参数，由 `MonsterMoveSemantics` 从攻击上下文累计），A9 固定 99；两个分支的 ValueProp 按审计 IL 分别为 4/8。
- `BeatOfDeathPower`（每次出牌对出牌者造成 Amount 伤害）经 `AfterCardPlayedMirrors.RegisterMirror(Type, handler)` 登记；`InvinciblePower`（按回合累计的掉血上限，语义等同原版 HardenedShell 但重置带 side 检查、钳制相位为 AfterOstyLate）经 `AfterDamageReceivedMirrors.RegisterMirror`、`ModifyHpLostMirrors.RegisterAfterOstyLateMirror`、`BeforeSideTurnStartMirrors.Register(Type, handler)` 三处登记；隐藏状态 `damage_recieved_this_turn_shared` 按名反射搬进 `Act4HeartInvinciblePredictionState`（多人分池显式拒绝）。`BUFF_MOVE` 在搜索中给已存在的死亡之搏叠层会克隆规范实例，`BeatOfDeathPower` 需 `PowerDynamicVarWarmup.RegisterAdaptedCanonicalPower` 预热。
- 该 Mod 的 `DoomKill` HookBefore 补丁（`heart_doom_damages_instead_of_kills` 开启时，持有 Invincible 且当前生命超过剩余额度的生物改为承受末日数值伤害）在 `SimulatedCombatState.DoomKill` 建模：倒序转换、死亡统计按过滤后名单；开关根捕获时同源读取，默认关闭时零行为差异。

行动效果数值为 1.1.7 审计 IL 常量（MVID 钉死）；燃身打击的灼烧目标牌堆与重击格挡分支读根捕获的 `AscensionLevel`（`SimulatedCombatState` 新增快照字段，构造时从 `RunState` 读取）。三怪的私有 `state` 字节在原版行动体里先于攻击更新（`state |= N` 位于 `DamageCmd` 之前），镜像相应放在 `ApplyBeforeAttack`：玩家死于攻击中途时 `ApplyForecastMove` 跳过效果段，但状态推进与实机保持一致，否则条件分支在死亡边界分叉。

#### 第四层终局战损「未知」修复（玩家报告 10241533）

玩家报告：腐化之心 Boss 战求解器找到完整胜利路线，但「预计战损」显示「未知」，逐回合详细操作正常。根因：真实 `CorruptHeart` 覆写 `AfterDeath`（审计 IL 仅更新音乐，纯表现），兼容层未登记该镜像；模拟击杀心脏时 `AfterDeathMirrors` 分派未登记覆写 → `MethodNotMirrored` 未补偿风险 → `CombatBeamSolver.StateEvaluation` 的 `uncertainVictory`（`HasUncompensatedDeathGap`）把胜利终局降级为 `UnsupportedEffect` 边界 → `BuildRouteAnnotations` 显式排除该边界节点写入 `combatEndedTurn` → 结果 `CombatEndedTurn` 为空 → UI 显示「预计战损 未知」。未补偿死亡缺口的降级本身是既有安全语义（未镜像死亡钩子可能藏复活效果），修的是兼容层漏登记：`AfterDeathMirrors.RegisterIgnoredMirror(Type)` 新增按 Type 的纯表现登记入口，审计通过后为 CorruptHeart 登记。同时补齐 `IntentForecaster.IsKnownStableAttack` 的终幕常量攻击行动（原版 `AttackIntent(int)` 的 `DamageCalc` lambda 捕获构造参数使 `Target` 非空，未放行会被误标「动态伤害」并压低置信度；终幕三怪攻击意图全部为审计 IL 常量）。

测试盲区与补齐：终幕 mock 类型位于 CombatSolver 程序集，而 CombatSolver 清单声明 `affects_gameplay=false`，`MethodMirrorRegistry.ResolveLookupUnderLock` 会把未映射的测试类型覆写自动当非 gameplay 忽略——掩盖「真实 gameplay Mod 覆写未登记镜像」路径。终幕场景现在把 mock 怪物/Power 映射到 id=Act4Heart、affectsGameplay=true 的假 Mod（runner 构造时，与登记同时），未登记的 mock 覆写与真实 Mod 一致走 Unsupported。场景体新增「击杀腐化之心→正式搜索→战斗终局回合非空且边界非 UnsupportedEffect」阶段；反向对照（临时禁用登记）复现玩家症状后修复通过。

验证：`ACT4HEART-FINALE` A10（含新 `VictoryTerminal` 阶段，反向对照先失败）、A0、`ACT4HEART-COMPAT` 均 Passed。真机安装真实 Mod 的腐化之心战斗未复测。

验证（初版）：新场景 `ACT4HEART-FINALE`（同构 mock 怪物/Power 走同一审计与登记路径；登记与行为开关覆盖在 runner 构造时完成——回合阶段镜像有密封门，场景断言体内的迟到登记会被回合准备期间的自动根捕获拒绝）：场景体覆盖不可战胜钳制/累计/持有者回合开始重置与末日转伤害（实机侧以同构 Harmony Prefix 复刻原补丁后差分，测试进程内安装卸载）；行动差分由 `coverage/fixtures/third-party/act4heart-finale-monster-moves.json`（A10，17 项检查：含每怪物 Rng 消费对齐与续用 MS 段对账、状态机条件推进、死亡之搏、BURN 进抽牌堆、重击 99 格挡、注入小额度不可战胜的钳制/累计）与 `act4heart-finale-monster-moves-a0.json`（非 A9 分支：灼烧进弃牌堆、重击按总伤害获得格挡、额度 300 的重置与末日口径）驱动，两者本机均 Passed。伤害口径注意：矛与盾的出生 `BackAttack*Power`+`SurroundedPower` 使盾侧攻击 ×1.5（猛击 21/重击 57）、矛侧正面无倍率；贯枪给全体敌人 +2 力量会滚入后续检查的心脏伤害与增益数值。`ACT4HEART-COMPAT` 重构后回归通过（结构拒绝改走 `VerifyAuditedTypesForTesting` 纯校验缝，订阅者门禁拒绝改用「未确认的额外 hook 订阅者」——CombatSolver 清单声明 `affects_gameplay=false`，未映射到假 Mod 的测试类型会被门禁直接信任放行）；`MONSTER-MOVES-B004` 原版怪物行动差分回归通过（覆盖 `MonsterMoveSemantics` 攻击总伤害参数与 `ApplyBeforeAttack` 前置状态块的原版路径）。`verify-refactor-boundaries` 因本机缺少 ripgrep 未运行；CoverageCatalog `--verify` 在未改动基线同样失败（InfusedCore 运行证据等 3 项预存缺口，与本批无关），生成目录保持历史结果。测试宿主不安装真实 Mod；真实程序集行为未在真机验证（MVID/结构由审计常量约束）。

### 回合准备路径的内容性 Mod 停止（修复战斗卡死）

玩家报告：装 Act 4 Heart 进入第四层时，`IncompatibleGameplayModException`（`ValidateMonsterModels` 正确拒绝 `Act4Heart.SpireShield`）从回合准备协调器逃逸，杀死游戏回合循环，战斗卡死直到重开房间。根因：`PlayerTurnSetupPatch` 用求解器的回合准备任务**替换**了游戏 `SetupPlayerTurn`；初始上下文构造（含 `CombatRootSnapshot.Capture`）里的通用 catch 只通知 Failed 后原样重抛，而怪物门禁、订阅者门禁与 OnPlay 补丁门禁都在捕获内抛 `IncompatibleGameplayModException`——自动计算或全自动开启时该路径必然触发，任何未适配内容性 Mod 都会卡死战斗，不只 Act 4 Heart。

修复（`RunSetupAfterRootCaptureBarrierAsync` 新增专用 catch）：内容性 Mod 未适配是已定义的搜索边界而非错误——调用 `SolverController.StopTurnSetupForIncompatibleMod` 走既有 `RecordTurnSetupFailure` 呈现（专用「暂未适配」提示、账本记 `IncompatibleGameplayMod`、不引导上传）并在 `SolverCombatSession` 上标记本战斗停止接管，然后 `InvokeOriginalSetupAsync` 回落原版回合准备，任务正常完成不重抛；后续回合 `TryInterceptSetup` 按停止标记直接放行原生路径。停止范围是单场战斗（会话随战斗生命周期重建）；手动搜索仍走搜索路径的既有优雅失败。未知异常保留原通知+重抛语义。

验证：新场景 `INCOMPATIBLE-MOD-TURN-SETUP-STOP`（把敌方怪物映射到假 Mod、满足接管先决后穿过 Harmony 补丁驱动真实 `SetupPlayerTurn`）断言替换任务无异常、原版回合准备完成抽牌、停止标记设置、稳定失败通知恰好一次、同战斗不重试接管，随后完整搜索部署流程照常跑完；`TURN-SETUP-DELAYED-ENERGY`（原生回合准备全状态对账）与 `SMOKE-001`＋`-VerifyPredictionFailureBoundaries` 回归通过。未在真机安装 Act 4 Heart 复测第四层（场景以同构 mock 走同一协调器路径）；用户侧需重启游戏加载新 DLL 后生效。另观察到游戏根目录 `.combatsolver-precombat` 积累了 90 个历史 `process-*` 目录（含卡死会话残留），未清理，不属本修复范围。
