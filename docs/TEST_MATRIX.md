# CombatSolver 测试入口

## 0.113 实例状态与 RNG 问题包

0.51.1 的最小差分、失败记录与验证范围见[历史卷 24](archive/testing/volume-24.md#0113-实例状态与-rng-问题包)。

## 0.113 Part 2：恐惧与极速

0.51.1 的领域、怪物、随机生成、六角色集成、续用与恢复证据见[历史卷 24](archive/testing/volume-24.md#0113-part-2恐惧与极速)。

## 请求预算显示

0.51.0 定稿的验证结果、复跑入口、失败与未验证项见[历史卷 23](archive/testing/volume-23.md#请求预算显示)。

## v0.113.0 标准模式适配

0.51.0 定稿的验证结果、复跑入口、失败与未验证项见[历史卷 23](archive/testing/volume-23.md#v01130-标准模式适配)。
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

## 首领卖血预算搜索

0.51.0 定稿的验证结果、复跑入口、失败与未验证项见[历史卷 23](archive/testing/volume-23.md#首领卖血预算搜索)。

## 社区批次 Q010 与组合补搜

现行组合入口按共享节点与时间准入，执行期间在每批提交边界处理内存预约、回收和停止。原包 SearchOnly、Beam 组合检查及真实 CLR 合同的结果、失败与未验证项见 [历史卷 20](archive/testing/volume-20.md#社区批次-q010-与组合补搜)。

## 0.50.1 智能药水机会成本

终局单药／双药低收益拒绝与 Force、奖励抵扣、原价高收益哨兵由 `SMART-OPENING-POTION-ADMISSION` 覆盖；跨成员比较由 `python -B tools/testing/checks/BeamWidthPortfolioChecks/run.py` 覆盖（118项通过）。原生 runId `d9629992ac31423586c7465510c18f6d` Passed（23.63秒，含建局），实例已清理。失败基线、同根材料与未验证范围见[问题记录](issues/potion-opportunity-20261006.md)。

## 社区批次 Q015：路线保留

0.51.0 定稿的验证结果、复跑入口、失败与未验证项见[历史卷 23](archive/testing/volume-23.md#社区批次-q015路线保留)。

## 社区批次 Q013 同根哨兵与部署夹具

0.51.0 定稿的验证结果、复跑入口、失败与未验证项见[历史卷 23](archive/testing/volume-23.md#社区批次-q013-同根哨兵与部署夹具)。

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

历史结果和复跑命令见[历史卷 22](archive/testing/volume-22.md)。

## 社区批次 B013 夹具与 PR #203、#204 合并验收（2026-10-04）

贡献者五主题夹具的失败基线与复跑、13 个无头机制场景及 18 项真实 CLR 检查的 runId、失败记录与未验证范围见[历史卷22](archive/testing/volume-22.md)。

## 性能研究分支

既有组件、药水、魂枢及未达标原型的合同与历史结果见[历史卷18](archive/testing/volume-18.md)。PR #207最新上游整合、逐次ABBA、23根品质回归及NoGC波动/超时限制见[当前验收](performance/pr207-upstream-0494-integration-20261005.md)。

## 手牌上限状态一致性（PR #224）

原版根／Fork 合同与贡献者的动态上限及耗时证据见[历史卷21](archive/testing/volume-21.md#手牌上限状态一致性pr-224)。
