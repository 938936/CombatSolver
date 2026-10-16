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
