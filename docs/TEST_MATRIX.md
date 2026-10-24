# CombatSolver 测试入口

2026-10-07 PR #227–#230 的本机 `SMART-POTION-AUDIT-BUDGET`、旧报告、GC 合同，以及 O068 和固定哨兵整场部署通过。O056 最终与当前主线同为 62 战损，历史界 42 未达成；组件证明合同被 native-version 门禁拒绝。最小复跑入口、同根对照、失败及未验证范围见[整合验证](archive/testing/pr-integration-20261007.md)。

贡献者 2026-10-06 的 Linux [计划续搜完整胜利证明](performance/plan-witness-propagation-20261006.md)：原生计划循环与最终 GC 合同 Passed；政策门禁、16 Fork、DOP1/16 实际成员派发与一次压缩完整回收通过，实例清理。11 根 44 次纯交错质量及峰值通过；2 次完整诊断、9 次根审计单列。未做全29根、上传包或可见性能，历史战损差异保留。

0.50.0 定稿沿用 PR #207、PR #211 与开局药水准入的既有行为证据，来源和范围见 [历史卷 20](archive/testing/volume-20.md)。

按改动选择最小验证层，方法见 [无人测试](HEADLESS_TESTING.md) 与 [社区验收](community/testing-guide.md)。以下命令提供当前复跑入口，不表示本轮已执行。单人共享损血剪枝的新基线验证见[策略证据](strategy/hp-loss-pruning/README.md#单人共享损血剪枝2026-10-05)。

历史记录见 [归档索引](archive/testing/README.md)，0.48.1 的验证、失败与未验证项见 [历史卷 12](archive/testing/volume-12.md)。

PR #213 的贡献者分阶段验证见 [Q002 历史入口](issues/q002-route-quality.md#0494-重新验证2026-10-05)。当前 0.50.0 主线整合使用 O003、O004、O005 同根、同政策对照，结果与复跑预算见 [当前验收](issues/q002-route-quality.md#0500-主线整合2026-10-05)。

## 回合准备路径的内容性 Mod 停止

`INCOMPATIBLE-MOD-TURN-SETUP-STOP` 把敌方怪物映射到假 gameplay Mod，满足接管先决（第 2 回合 + 消耗堆 Imbued 附魔牌）后穿过 Harmony 补丁驱动真实 `SetupPlayerTurn`：替换任务不得把 `IncompatibleGameplayModException` 带回游戏回合循环，原版回合准备完成抽牌，停止标记设置且稳定失败通知恰好一次，同战斗后续回合不重试接管；随后完整搜索部署流程照常跑完。修复前的失败基线即玩家报告的第四层战斗卡死。

```text
pwsh -NoProfile -File tools/testing/run-unattended-test.ps1 -ScenarioId INCOMPATIBLE-MOD-TURN-SETUP-STOP -EnemyCurrentHp 30 -Sts2GameRoot <游戏根目录> -RitsuWorkshopRoot <RitsuLib 工坊目录> -HeadlessInstance incompat-ts -CleanupInstanceOnExit
./tools/testing/run-unattended-test.sh --scenario-id INCOMPATIBLE-MOD-TURN-SETUP-STOP --enemy-current-hp 30 --sts2-game-root <游戏根目录> --ritsu-workshop-root <RitsuLib 工坊目录> --headless-instance incompat-ts --cleanup-instance-on-exit
```

## Act 4 Heart 前三层兼容层

`ACT4HEART-COMPAT` 用同构 mock 类型（映射到 id="Act4Heart" 的 MockTypes Mod 与测试缝登记）走完整审计与适配路径：结构自检经纯校验缝拒绝带额外战斗 hook 的订阅器、MVID 不匹配拒绝且不影响已登记状态、未确认的额外 hook 订阅者被订阅者门禁拒绝（`IncompatibleGameplayModException` 带 Act4Heart 来源）、已确认订阅者放行并完成完整根捕获；金属化（敌方回合末格挡）与再生（AfterSideTurnEnd 治疗不递减）做模拟 vs 实机严格差分，玩家侧 Early 分发不记录未镜像风险，随后当回合搜索与部署完整跑通。登记在 runner 构造时完成（早于回合准备期间的自动根捕获）。测试宿主不安装真实 Act4Heart。

```text
pwsh -NoProfile -File tools/testing/run-unattended-test.ps1 -ScenarioId ACT4HEART-COMPAT -EnemyCurrentHp 30 -Sts2GameRoot <游戏根目录> -RitsuWorkshopRoot <RitsuLib 工坊目录> -HeadlessInstance act4heart -CleanupInstanceOnExit
./tools/testing/run-unattended-test.sh --scenario-id ACT4HEART-COMPAT --enemy-current-hp 30 --sts2-game-root <游戏根目录> --ritsu-workshop-root <RitsuLib 工坊目录> --headless-instance act4heart --cleanup-instance-on-exit
```

## Act 4 Heart 第四层「终幕」

`ACT4HEART-FINALE` 用同构 mock 怪物（SpireShield/SpireSpear/CorruptHeart，照抄 1.1.7 审计 IL）与 mock Power（BeatOfDeath/Invincible）走同一审计登记路径（runner 构造时完成，早于首个根捕获；行为开关由测试缝覆盖提供：降专注概率 0.5、末日转伤害开启；mock 类型映射到 id=Act4Heart、affectsGameplay=true 的假 Mod——CombatSolver 清单声明 affects_gameplay=false，未映射的测试类型覆写会被镜像注册表自动忽略，掩盖真实 gameplay Mod 的未登记镜像路径）。场景体断言不可战胜的额度钳制、跨次累计与持有者一侧回合开始重置，末日改为被钳制的伤害而非击杀（实机侧以同构 Harmony Prefix 复刻原补丁后差分，测试进程内安装卸载），以及击杀腐化之心的完整胜利必须带战斗终局回合且边界非 UnsupportedEffect（AfterDeath 未登记镜像的未补偿死亡缺口会把胜利终局降级、预计战损显示「未知」；已做反向对照）。行动差分由 `-MonsterMoveChecksPath` 驱动的检查数组执行（模拟侧 `ApplyForecastMove` vs 实机侧 `PerformMove` 全量快照对账）：覆盖三怪全部 10 个行动、状态机条件推进（`expectedNextMoveId`）、猛击的每怪物 Rng 消费对齐（有球/无球两态，含续用 MS 段对账）、死亡之搏（出牌反噬）、灼烧目标牌堆与重击格挡的进阶分支。A10（含背击 ×1.5、贯枪力量叠加与心脏增益滚雪球的伤害口径）用 `act4heart-finale-monster-moves.json`，非 A9 分支用 `act4heart-finale-monster-moves-a0.json`；两者本机均 Passed。怪物门禁的未适配拒绝路径由 `INCOMPATIBLE-MOD-TURN-SETUP-STOP` 与 ContentModFailures 场景维护。测试宿主不安装真实 Act4Heart；真实程序集行为未在真机验证。

```text
pwsh -NoProfile -File tools/testing/run-unattended-test.ps1 -ScenarioId ACT4HEART-FINALE -Ascension 10 -MonsterMoveChecksPath coverage/fixtures/third-party/act4heart-finale-monster-moves.json -Sts2GameRoot <游戏根目录> -RitsuWorkshopRoot <RitsuLib 工坊目录> -HeadlessInstance act4fin -CleanupInstanceOnExit
pwsh -NoProfile -File tools/testing/run-unattended-test.ps1 -ScenarioId ACT4HEART-FINALE -MonsterMoveChecksPath coverage/fixtures/third-party/act4heart-finale-monster-moves-a0.json -Sts2GameRoot <游戏根目录> -RitsuWorkshopRoot <RitsuLib 工坊目录> -HeadlessInstance act4fin0 -CleanupInstanceOnExit
```

## Ctrl+F9 面板可见性（PR #226）

快捷键、新图层、重置和监控合同的通过、失败与可见验证范围见[历史卷21](archive/testing/volume-21.md#ctrlf9-面板可见性pr-226)。

## 社区批次 Q010 与组合补搜

现行组合入口按共享节点与时间准入，执行期间在每批提交边界处理内存预约、回收和停止。原包 SearchOnly、Beam 组合检查及真实 CLR 合同的结果、失败与未验证项见 [历史卷 20](archive/testing/volume-20.md#社区批次-q010-与组合补搜)。

## 0.50.1 智能药水机会成本

终局单药／双药低收益拒绝与 Force、奖励抵扣、原价高收益哨兵由 `SMART-OPENING-POTION-ADMISSION` 覆盖；跨成员比较由 `python -B tools/testing/checks/BeamWidthPortfolioChecks/run.py` 覆盖（118项通过）。原生 runId `d9629992ac31423586c7465510c18f6d` Passed（23.63秒，含建局），实例已清理。失败基线、同根材料与未验证范围见[问题记录](issues/potion-opportunity-20261006.md)。

## 社区批次 Q015：路线保留

贡献者五主题的同根短搜、原生部署、固定哨兵及失败记录见[Q015 记录](issues/q015-route-quality.md)。维护的开战政策输入为 [High](../coverage/fixtures/search/damaging-continuation-replay-policy.json)、[Medium](../coverage/fixtures/search/damaging-continuation-medium-replay-policy.json) 与 [Force](../coverage/fixtures/search/damaging-continuation-forced-dexterity-policy.json)，原生部署使用 Instant／0 秒，断言计划外重算。本机最终整合收窄破盾收尾资格，O068 保持 3 战损、0 药、T4，独立哨兵保持 5 战损、1 药、T4，均完整部署、零重算；其余贡献者旧结果保留原验证范围。旧报告默认手牌上限兼容合同通过，详细同根对照见[本机验证](archive/testing/pr-integration-20261007.md)。

## 社区批次 Q013 同根哨兵与部署夹具

Q013（O056–O060，#220 / PR #228）的五条同根 `SearchOnly` 质量界夹具与两条 `DeploySolver` 夹具位于 `coverage/fixtures/regressions/community/q013-*.json`；逐条结果、失败基线与未验证项见 `coverage/evidence/test-evidence.json` 的 `Q013-*` 条目与[认领者记录](community/drafts/2026-10-06/Q013-claim-reproduction.md)。夹具依赖不入库的玩家原包 ZIP，故本页不提供行首可执行命令（与 Q010 同口径，矩阵可复跑清单条数不变）：复跑时把夹具 JSON 字段逐个传给 `tools/testing/run-unattended-test.ps1`，显式带 `-Sts2GameRoot` / `-RitsuWorkshopRoot` / `-CleanupInstanceOnExit`，判定以 `result.json.status` 为准（带实例清理时 launcher 退出码 1 不代表失败）。

认领提交未改 `src`（`git diff --name-only 0d290fbe 0143d82f -- src` 为空），七条夹具在基点 `0d290fbe` 上按夹具本体全部 Passed；界锁各主题当时的实际产出，不构成改良值达标声明（O056/O057/O059 未达包内改良值，O060 默认预算下终值 35～43 摆动，O058 缺口在同一次请求内后续搜索的运行态）。合并 `6031debd` 后（`89798a78` 把 `max_hand_size` 写进续用指纹）七条全部在 `native_replay_events` 报 `restore_mismatch` 而未进入搜索，属旧包容兼容性边界，逐条 runId 与包内取证见[调查流水](archive/community/q013-claim-reproduction-20261006.md)；PR #227 的默认手牌上限兼容已进入整合，本机 O056 原生恢复通过，但历史质量界 42 未达成；其他六条夹具在最终整合上未复跑，见本页整合验证。

## 开局药水补搜准入

固定两槽满栏，使用原版奖励 RNG 捕获确定掉药／不掉药的搜索根。`SMART-OPENING-POTION-ADMISSION` 覆盖低损共同准入与换药抵扣，`SMART-OPENING-POTION-VALUE` 覆盖原价省血边界。失败基线、最终结果与验证范围见 [历史卷 20](archive/testing/volume-20.md#开局药水补搜准入)。

```text
pwsh -NoProfile -File tools/testing/run-unattended-test.ps1 -ScenarioId SMART-OPENING-POTION-ADMISSION -CharacterId SILENT -EncounterId FUZZY_WURM_CRAWLER_WEAK -EnemyCurrentHp 1000 -EnableNoGcRegionForTest 0 -TimeoutSeconds 120 -CleanupInstanceOnExit
./tools/testing/run-unattended-test.sh --scenario-id SMART-OPENING-POTION-ADMISSION --character-id SILENT --encounter-id FUZZY_WURM_CRAWLER_WEAK --enemy-current-hp 1000 --enable-no-gc-region-for-test 0 --timeout-seconds 120 --cleanup-instance-on-exit
```

将 ScenarioId 改为 `SMART-OPENING-POTION-VALUE` 可单独执行原价省血边界。

## 0.49.1 日志站硬逻辑（2026-10-04）

强制结束回合选牌、群体 Power、死亡金币回调、延迟能量／等离子球、开局小刀、回合末自动出牌、行动意图、资源隔离、反应格挡和界面归属的原生差分见 [逐类验收](issues/0.49.1-hardbugs-20261004.md#原生验收证据)。该记录保留失败基线、runId、复跑入口、第三方边界及未验证项；生产部署合同包含增量验证和计划外重算断言。

0.49.2 内容性 Mod 失败分类的原生合同、完整命令及未验证范围见[历史卷19](archive/testing/volume-19.md)。

## 0.49.1 内存提交回归（2026-10-04）

`CombatSolver.GcPolicyChecks` 直接编译生产 Runtime 内存代码。`default-commit` 在未改生产代码时 Failed：显式回退后分配上限仍有效；修复后 2 项 Passed，覆盖普通检查点保留限额、不可分割提交完成回收后续行、后续请求重新建立限额与取消保持原准入。

```bash
dotnet run --project tools/testing/checks/CombatSolver.GcPolicyChecks/CombatSolver.GcPolicyChecks.csproj -c Release -- default-commit
```

相邻真实 CLR 回归：`default-entry` 2 项、`recovery-lifecycle` 3 项 Passed，包含实际 NoGC 退出与恢复、显式退出持续生效及诊断失败清理。每项设有 15 或 20 秒截止时间。未重放 18 份原包整场，也没有可见 Steam 或低内存宿主性能结论；[排查记录](issues/0.49.0-memory-commit-regression-20261004.md)保留固定报告身份与触发窗口。

## PR #203 的机制合同

金币、最大生命回复与遗物间接回复的贡献者证据见 [专题记录](performance/gold-max-hp-healing-20261003.md)。独立场景为 `GOLD-HEALING-MECHANISMS`、`MAX-HP-HEALING-CALLBACKS`、`FEED-MAX-HP-CAP`、`RELIC-MAX-HP-HEALING-BOUNDS` 和 `AXEBOT-JOSS-DEFERRED-FORK`，均从平台原生无人入口运行，使用 IRONCLAD、FUZZY_WURM_CRAWLER_WEAK、120 秒上限及实例清理。原作者结果与本轮合并验证分别记账。

金币和回复合同同时传 `-EvidenceDirectory .local/validation/pr203/<场景>`（Bash：`--evidence-directory`），初始生命为 50/80。金纸合同还传 `-RelicsPath coverage/fixtures/regressions/axebot/axebot-joss-deferred-relics.json`（Bash：`--relics-path`）。搜索阶段顺序合同为 `KNOWN-HEALING-MEMBERS` 和 `KNOWN-HEALING-OPENING`，使用 SILENT；受影响的固定前缀合同使用 IRONCLAD，强制结束回合牌合同使用 REGENT。

## 当前矩阵命令

矩阵启动器只读取本节的平台原生命令；历史记录不作为自动执行清单。以下两项分别检查倾泻手空边界与相邻效果作用域，不运行整场搜索。需要当前游戏及 RitsuLib 路径，矩阵启动器统一管理实例并在结束时清理。

```powershell
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId CASCADE-EMPTY-HAND-NATIVE -EnemyCurrentHp 1000 -HeadlessFastModeForTest Instant -DeploymentFastModeForTest Instant -DeploymentInterActionDelaySecondsForTest 0 -TimeoutSeconds 120
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId EFFECT-SCOPE-ADJACENT-CONTRACT -EnemyCurrentHp 1000 -HeadlessFastModeForTest Instant -DeploymentFastModeForTest Instant -DeploymentInterActionDelaySecondsForTest 0 -TimeoutSeconds 120
```

```bash
./tools/testing/run-unattended-test.sh --scenario-id CASCADE-EMPTY-HAND-NATIVE --enemy-current-hp 1000 --headless-fast-mode-for-test Instant --deployment-fast-mode-for-test Instant --deployment-inter-action-delay-seconds-for-test 0 --timeout-seconds 120
./tools/testing/run-unattended-test.sh --scenario-id EFFECT-SCOPE-ADJACENT-CONTRACT --enemy-current-hp 1000 --headless-fast-mode-for-test Instant --deployment-fast-mode-for-test Instant --deployment-inter-action-delay-seconds-for-test 0 --timeout-seconds 120
```


## 0.48.0 硬错误机制合同

这些场景各自停止在共享首因或必要跨回合边界。完整runId、失败基线和未验证项见[历史卷13](archive/testing/volume-13.md)，逐包分类见[问题记录](issues/0.48.0-hardbugs-20261003.md)。

```powershell
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId NATIVE-CHOOSE-OPEN-GATE -CharacterId SILENT -EncounterId CHOMPERS_NORMAL -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId CALCULATED-GAMBLE-ORDERED-DISCARD -CharacterId SILENT -EncounterId CHOMPERS_NORMAL -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId FROZEN-LIGHTNING-CHANNELS -CharacterId DEFECT -EncounterId MECHA_KNIGHT_ELITE -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId REPORT-ROUND-IMBALANCED -CharacterId NECROBINDER -EncounterId BOWLBUGS_NORMAL -InitialPlayerHp 200 -InitialPlayerMaxHp 200 -ClearPlayerHand -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId SECOND-WIND-REPORT-ROOT -CharacterId IRONCLAD -EncounterId DECIMILLIPEDE_ELITE -InitialRoundNumber 3 -InitialPlayerTurnNumber 3 -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId GROUP-DEBUFF-REACTIVE-DRAW -CharacterId NECROBINDER -EncounterId CHOMPERS_NORMAL -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId CONSTRUCT-REAPER-ARTIFACT -CharacterId NECROBINDER -EncounterId CONSTRUCT_MENAGERIE_NORMAL -InitialRoundNumber 3 -InitialPlayerTurnNumber 3 -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId FIXED-PREFIX-POTION-POLICY -CharacterId IRONCLAD -EncounterId FUZZY_WURM_CRAWLER_WEAK -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId FIXED-PREFIX-TURN-OUTCOMES -CharacterId IRONCLAD -EncounterId FUZZY_WURM_CRAWLER_WEAK -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId MIRRORED-HOOK-FILTER -CharacterId IRONCLAD -EncounterId FUZZY_WURM_CRAWLER_WEAK -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId FROZEN-ROOT-LISTENERS -CharacterId IRONCLAD -EncounterId FUZZY_WURM_CRAWLER_WEAK -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId CALCULATED-HISTORY-FREEZE -CharacterId IRONCLAD -EncounterId FUZZY_WURM_CRAWLER_WEAK -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId VOID-FORM-TURN-CHOICES -CharacterId REGENT -EncounterId FUZZY_WURM_CRAWLER_WEAK -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId FLATTEN-MUSIC-BOX-ENTRY -CharacterId NECROBINDER -EncounterId FUZZY_WURM_CRAWLER_WEAK -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId SEEKER-ORDERED-OPTIONS -CharacterId IRONCLAD -EncounterId FUZZY_WURM_CRAWLER_WEAK -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId RITUAL-TEMPORARY-STRENGTH -CharacterId IRONCLAD -EncounterId FUZZY_WURM_CRAWLER_WEAK -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId NOXIOUS-RAMPART-ORDER -CharacterId IRONCLAD -EncounterId TURRET_OPERATOR_WEAK -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId EVIL-EYE-EXHAUST-HISTORY -CharacterId IRONCLAD -EncounterId FUZZY_WURM_CRAWLER_WEAK -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId BLOCK-SPEC-CARD-PLAY-IDENTITY -CharacterId IRONCLAD -EncounterId FUZZY_WURM_CRAWLER_WEAK -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId ROUTE-ADOPTION-LIFETIME -CharacterId IRONCLAD -EncounterId FUZZY_WURM_CRAWLER_WEAK -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId PAELS-LEGION-FINISHED-REFERENCE -CharacterId SILENT -EncounterId FUZZY_WURM_CRAWLER_WEAK -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId END-TURN-RISK-LOSS-ACCOUNTING -CharacterId DEFECT -EncounterId FUZZY_WURM_CRAWLER_WEAK -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId MAKE-IT-SO-FULL-HAND -CharacterId REGENT -EncounterId FUZZY_WURM_CRAWLER_WEAK -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId CARD-COST-IDENTITY-CONTRACT -CharacterId IRONCLAD -EncounterId FUZZY_WURM_CRAWLER_WEAK -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId DAMPEN-REACTIVE-ROCKET-PUNCH -CharacterId DEFECT -EncounterId KNIGHTS_ELITE -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId DAMPEN-REACTIVE-MELANCHOLY -CharacterId NECROBINDER -EncounterId KNIGHTS_ELITE -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
pwsh -NoProfile -File tools\testing\run-unattended-test.ps1 -ScenarioId RADIANT-PEARL-ENTRY -CharacterId IRONCLAD -EncounterId FUZZY_WURM_CRAWLER_WEAK -EnemyCurrentHp 1000 -TimeoutSeconds 120 -CleanupInstanceOnExit
```

```bash
./tools/testing/run-unattended-test.sh --scenario-id NATIVE-CHOOSE-OPEN-GATE --character-id SILENT --encounter-id CHOMPERS_NORMAL --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id CALCULATED-GAMBLE-ORDERED-DISCARD --character-id SILENT --encounter-id CHOMPERS_NORMAL --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id FROZEN-LIGHTNING-CHANNELS --character-id DEFECT --encounter-id MECHA_KNIGHT_ELITE --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id REPORT-ROUND-IMBALANCED --character-id NECROBINDER --encounter-id BOWLBUGS_NORMAL --initial-player-hp 200 --initial-player-max-hp 200 --clear-player-hand --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id SECOND-WIND-REPORT-ROOT --character-id IRONCLAD --encounter-id DECIMILLIPEDE_ELITE --initial-round-number 3 --initial-player-turn-number 3 --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id GROUP-DEBUFF-REACTIVE-DRAW --character-id NECROBINDER --encounter-id CHOMPERS_NORMAL --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id CONSTRUCT-REAPER-ARTIFACT --character-id NECROBINDER --encounter-id CONSTRUCT_MENAGERIE_NORMAL --initial-round-number 3 --initial-player-turn-number 3 --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id FIXED-PREFIX-POTION-POLICY --character-id IRONCLAD --encounter-id FUZZY_WURM_CRAWLER_WEAK --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id FIXED-PREFIX-TURN-OUTCOMES --character-id IRONCLAD --encounter-id FUZZY_WURM_CRAWLER_WEAK --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id MIRRORED-HOOK-FILTER --character-id IRONCLAD --encounter-id FUZZY_WURM_CRAWLER_WEAK --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id FROZEN-ROOT-LISTENERS --character-id IRONCLAD --encounter-id FUZZY_WURM_CRAWLER_WEAK --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id CALCULATED-HISTORY-FREEZE --character-id IRONCLAD --encounter-id FUZZY_WURM_CRAWLER_WEAK --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id VOID-FORM-TURN-CHOICES --character-id REGENT --encounter-id FUZZY_WURM_CRAWLER_WEAK --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id FLATTEN-MUSIC-BOX-ENTRY --character-id NECROBINDER --encounter-id FUZZY_WURM_CRAWLER_WEAK --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id SEEKER-ORDERED-OPTIONS --character-id IRONCLAD --encounter-id FUZZY_WURM_CRAWLER_WEAK --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id RITUAL-TEMPORARY-STRENGTH --character-id IRONCLAD --encounter-id FUZZY_WURM_CRAWLER_WEAK --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id NOXIOUS-RAMPART-ORDER --character-id IRONCLAD --encounter-id TURRET_OPERATOR_WEAK --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id EVIL-EYE-EXHAUST-HISTORY --character-id IRONCLAD --encounter-id FUZZY_WURM_CRAWLER_WEAK --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id BLOCK-SPEC-CARD-PLAY-IDENTITY --character-id IRONCLAD --encounter-id FUZZY_WURM_CRAWLER_WEAK --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id ROUTE-ADOPTION-LIFETIME --character-id IRONCLAD --encounter-id FUZZY_WURM_CRAWLER_WEAK --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id PAELS-LEGION-FINISHED-REFERENCE --character-id SILENT --encounter-id FUZZY_WURM_CRAWLER_WEAK --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id END-TURN-RISK-LOSS-ACCOUNTING --character-id DEFECT --encounter-id FUZZY_WURM_CRAWLER_WEAK --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id MAKE-IT-SO-FULL-HAND --character-id REGENT --encounter-id FUZZY_WURM_CRAWLER_WEAK --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id CARD-COST-IDENTITY-CONTRACT --character-id IRONCLAD --encounter-id FUZZY_WURM_CRAWLER_WEAK --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id DAMPEN-REACTIVE-ROCKET-PUNCH --character-id DEFECT --encounter-id KNIGHTS_ELITE --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id DAMPEN-REACTIVE-MELANCHOLY --character-id NECROBINDER --encounter-id KNIGHTS_ELITE --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
./tools/testing/run-unattended-test.sh --scenario-id RADIANT-PEARL-ENTRY --character-id IRONCLAD --encounter-id FUZZY_WURM_CRAWLER_WEAK --enemy-current-hp 1000 --timeout-seconds 120 --cleanup-instance-on-exit
```

## 社区批次 B013 夹具与 PR #203、#204 合并验收（2026-10-04）

贡献者五主题夹具的失败基线与复跑、13 个无头机制场景及 18 项真实 CLR 检查的 runId、失败记录与未验证范围见[历史卷22](archive/testing/volume-22.md)。

## 性能研究分支

既有组件、药水、魂枢及未达标原型的合同与历史结果见[历史卷18](archive/testing/volume-18.md)。PR #207最新上游整合、逐次ABBA、23根品质回归及NoGC波动/超时限制见[当前验收](performance/pr207-upstream-0494-integration-20261005.md)。

## 手牌上限状态一致性（PR #224）

原版根／Fork 合同与贡献者的动态上限及耗时证据见[历史卷21](archive/testing/volume-21.md#手牌上限状态一致性pr-224)。
