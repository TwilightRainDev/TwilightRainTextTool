# SDD ledger — plan: doc/plans/2026-09-26-texttool-lint.md

Repo: E:\work_zone\Code\TextTool ｜ 分支: main（用户明确同意直接在 main 上实施）
规格: doc/specs/2026-09-26-texttool-lint-design.md

预检发现（已修）: 任务 2 的测试依赖任务 3 的产物，与 pre-commit 全量测试冲突 → 已把该测试移入任务 3（commit 见下）。

BASE(task 1) = 4fd4a8c
Task 1: implementer DONE_WITH_CONCERNS — commit adf9270（2 行资源名修正 + 2 个测试；179 测试全绿、门禁全过）
Task 1: brief 步骤 2 的 RED 预期被实施者实测证伪 —— 指定测试断言的是「程序集里存在该资源名」，由 csproj 的 EmbeddedResource 决定，与 store 里的查询串解耦，改前改后均通过；实施者另用行为探针（临时标记方案 + 断言 store 读到）取得真 RED/GREEN 证据，探针已清理未入库。
Task 1: fix round 1/5 (pre-review) — 人类裁定：采用实施者建议（资源名提为 store 上的 internal const + InternalsVisibleTo，测试改为引用该常量，使其在查询串写错时真正失败）。已派回原实施者 impl-task1，要求不做 amend、追加新 commit。
   待办（controller）: 修正 doc/specs 与 doc/plans 中「该测试今天会失败」的错误断言。→ 已修，commit 34bc00e
Task 1: 评审者裁决 —— Spec ✅（与修订后 plan Task 1 逐项对齐）、Critical 0、Important 0、Minor 6、Task quality Approved。
Task 1: warnings resolved —— (a) 门禁由 controller 独立复验：`dotnet format --verify-no-changes` 退出码 0、`dotnet test` 179 通过 0 失败；(b) `refactor:` 前缀经查仓史（docs 10 / fix 5 / feat 3 / chore 3 / build 3 / refactor 1）非独有类型，按 Minor 延后。
Task 1: minor (deferred): 测试钉的是常量而非调用点 —— store 里若把 GetManifestResourceStream 改回字面串，测试不会红。
Task 1: minor (deferred): d3f0352 用了计划清单外的 `refactor:` 前缀（仓史仅此一例）。
Task 1: minor (deferred): adf9270 提交信息「不再掩盖 JSON 读取失败」言过其实（该提交时查询串仍是错的）。
Task 1: minor (deferred): 两处 store 的常量 + 注释文字重复。
Task 1: minor (deferred): 新增的两个测试未 dispose 取到的 stream。
Task 1: minor (deferred): JSON 现已成为活数据源，GetDefaultSchemes() 每次调用都重新读资源，无缓存。
Task 1: complete (commits 4fd4a8c..d3f0352, review clean — 0 Critical/Important, 6 minor deferred, 2 warnings resolved)

BASE(task 2) = d511eec
Task 2: implementer 交付 d66fc1f（LintRule.cs + LintRuleStoreTests.cs，5 测试；184 全绿、门禁全过），报告提 4 条顾虑
Task 2: 顾虑 1 裁定为真缺陷 —— System.Text.Json 会把显式 null 写回 Patterns，Validate 抛 NRE 而非设计承诺的 ArgumentException → 修复轮 1/5（pre-review，已派回 impl-task2）
Task 2: 顾虑 2（Merge 对外部规则按引用插入、与入参共享引用）观察性，当前无调用方，无动作
Task 2: 顾虑 3（Load/GetDefaultRules 在任务 3 前必抛 InvalidOperationException）属预期状态，无动作
Task 2: 顾虑 4（EmbeddedResourceName 为 public）并入修复轮收为 internal，对齐 ReplaceSchemeStore.ResourceName
Task 2: plan 已同步订正（commit b87393a、53034f0）
Task 2: 修复轮 1/5 完成 —— commit 9fb9314（Validate 拦 null/空串 Patterns、内置侧先校验、常量收为 internal、补 2 测试）；真 RED 已取证（Expected ArgumentException / Actual NullReferenceException），GREEN 7/7、全量 186/186、格式化退出码 0
Task 2: 评审者裁决 —— Spec ✅、Critical 0、Important 0、Minor 5、Task quality Approved
Task 2: warnings resolved —— (a) 署名经查为 TwilightRainDev <122437146+TwilightRainDev@users.noreply.github.com>；(b) 门禁由 controller 独立复跑：构建 0 错误、186/186、格式化退出码 0；(c) Minor⑤（计划代码块仍写 public const）已由 53034f0 解决
Task 2: minor (deferred): Merge 就地替换后不更新 index —— external 同 Id 出现两次会抛 ArgumentOutOfRangeException（生产路径已被 Validate(external) 挡住）
Task 2: minor (deferred): Validate 仍信任列表元素非 null —— 外部 JSON 形如 [null] 时仍走 NRE，是修复轮的同类残余
Task 2: minor (deferred): Merge 拷贝不对称（内置克隆、外部按引用插入）
Task 2: minor (deferred): 新增的 Validate(builtIn) 无测试执行到（任务 3 前 Load() 必抛）
Task 2: minor (deferred): 计划文档 internal/public 不一致（已随 53034f0 消解，仅存记录）
Task 2: complete (commits d511eec..9fb9314, review clean — 0 Critical/Important, 5 minor deferred, 2 warnings resolved)

BASE(task 3) = 53034f0
Task 3: implementer 交付 98f8ac2（default_lint_rules.json 19 条 + csproj EmbeddedResource + 2 测试）
Task 3: controller 独立复核 JSON —— 19 条、Id 顺序正确、PascalCase、无空模式、Kind 16 regex/3 literal、Scope/TailChars/MinCount 符合设计、L7 Hints 6/6 对齐、P1/P3 转义可编译且能命中、P8 表情范围实测命中 😀🚀✨✅ 与变体选择符、普通中文不命中
Task 3: 实施者发现真缺陷（数据错，非执行偏差）—— P7 隐形字符在 literal + JSON 转义组合下静默失效：JSON 的 \\u200B 解码为六个普通字符，再经 Regex.Escape 转义，只匹配字面该序列，真实 U+200B 永不命中；实测三组合 literal(Regex.Escape)=False / regex(原文)=True / literal(真字符)=True
Task 3: 修复轮 1/5（pre-review）—— 裁定 P7 的 Kind 改 regex（保留可见转义，优于嵌入真实不可见字符）；并立不变量「literal 规则的 Patterns 不得含反斜杠」+ 配套测试；plan 与 spec 已订正（commit f3b5eec）
Task 3: 实施者的第二点澄清（派单说明里 CJK 字符类的措辞过宽，brief 原文只约束汉字范围、标点不在其列）成立，无动作
Task 3: 修复轮 1/5 完成 —— commit f58deac（P7 的 Kind literal→regex、补不变量测试「literal 规则的 Patterns 不得含反斜杠」）；RED 已取证（聚焦跑 1 失败/0 通过，offenders 含 P7 八次），GREEN 9/9、全量 188/188、门禁全过
Task 3: 评审者裁决 —— Spec PASS、Critical 0、Important 0、Minor 4、Task quality Approved
Task 3: warnings resolved —— (a) 三个变更文件 LF/无 BOM/末行完整（controller 字节级复验）；(b) 署名正确、构建 0 错误、189/189、格式化退出码 0（controller 独立复跑）；(c) P7 的 regex 语义将由图 Task 5 的真实规则集端到端冒烟测试覆盖（已补入计划）
Task 3: minor (deferred): 新测试未断言四个非默认字段真的绑定（L2.MinCount=3 / S2.Scope+MinCount / S3.Scope+TailChars=30 / L7.Hints）——键名拼错会静默回落默认值，Validate 不拦
Task 3: minor (deferred): P3 的 Detail 措辞比其字符类窄（实际也会命中 ",。"、",Ａ"）
Task 3: minor (deferred): 跨规则重叠 —— L5 完全包含 L6「您说得完全正确」、P1 包含 P2 的「。{3,}」，同一跨度报两次；建议 Task 8/9 报告层去重，交最终评审分诊
Task 3: minor (deferred): 不变量测试在 literal 规则归零时空过；Load 的 19 条计数依赖程序目录无 lint_rules.json（今日实测 0 命中）
Task 3: complete (commits 53034f0..f58deac, review clean — 0 Critical/Important, 4 minor deferred, 3 warnings resolved)

BASE(task 4) = f58032d
Task 4: implementer 交付 b0bb2a2（LintReport.cs 四个类型 + Filter + ToJson，4 测试），报告提 4 条自审
Task 4: controller 复核发现门禁真缺陷 —— LintReport.cs:43 局部变量 WantWarnOnly 违反 .editorconfig 的 locals_should_be_camelcase；实测 dotnet format --verify-no-changes 真实退出码 = 2，而 CI 的 code-quality job 正是跑这条命令（build-test.yml:115）→ 会弄红 CI。dotnet build -warnaserror 与 pre-commit 钩子均不命中（本仓未开 EnforceCodeStyleInBuild）。
Task 4: 方法学订正 —— 此前 Task 1-3 记录的「格式化退出码=0」是管道假象（cmd | tail 后 $? 取的是 tail 的状态）。已改用 cmd > log 2>&1; echo $?。以此法复验：全仓唯一 format 违规就是上述 Task 4 那一处，Task 1-3 的代码干净。
Task 4: 计划补两条全局约束（局部变量/参数一律 camel_case；量门禁退出码不接管道）并修正命名 —— commit a9a1618
Task 4: 修复轮 1/5（pre-review）已派回 impl-task4，要求以正确读法实测格式化真实退出码为 0
Task 4: 其余自审（Notes 与接收者共享引用、Hint/SuggestScheme 输出显式 null、测试源隐形字符用可见转义）无动作
Task 4: 修复轮 1/5 完成 —— commit 33f52e9（WantWarnOnly → wantWarnOnly）；门禁以正确读法实测：构建退出码 0、193/193 通过、格式化退出码 0 且输出 0 字节（修复前为格式化退出码 2、输出一条 IDE1006）
Task 4: 评审者裁决 —— Spec [OK] 全项通过、Critical 0、Important 0、Minor 6、Task quality Approved
Task 4: 两条 Minor 已在后续任务开工前折入计划（commit ae6e371）—— (a) Snippet 的 ±8 字符窗口可能劈开 emoji 代理对产出落单代理项 → 边界外扩一格 + Task 5 加测试；(b) 算法命中不走 Validate、Filter 的 warn 比较大小写敏感，写成 "Warn" 会被 --min-severity 静默丢弃致退出码 0 → Task 7 加测试钉住只用小写
Task 4: minor (deferred): 「隐形字符不抛异常」测试是空转的（U+200B 是合法标量，任何编码器不会为它抛错），其保护由 Snippet 代理对测试取代
Task 4: minor (deferred): 未钉住「Filter 不改接收者」（用例把接收者丢弃了），也未断言 File/Chars 传递到过滤结果
Task 4: minor (deferred): Filter 结果的 Notes 与命中元素跟原报告共享引用（别名），下游若就地修改会穿透
Task 4: minor (deferred): UnsafeRelaxedJsonEscaping 连带不转义 < > & ' +；Task 11 若把该 JSON 嵌进 HTML/Markdown 是注入面（更窄的等价选择 JavaScriptEncoder.Create(UnicodeRanges.All)）
Task 4: minor (deferred): LintReport 类型唯一缺 XML 文档注释（其余三个类型都有）
Task 4: complete (commits f58032d..33f52e9, review clean — 0 Critical/Important, 6 minor deferred, 门禁以正确读法复验全绿)

BASE(task 5) = ae6e371
Task 5: implementer 报 NEEDS_CONTEXT（未提交）—— 计划缺陷：Task 5 的冒烟测试 `LintRuleStore.Load()` 全量 19 条规则，其中 S2/S3 是段落作用域，而 ScanParagraphScope 在本任务还是抛异常的桩，Scan 必然先撞上；pre-commit 跑全量测试 → 提交必被挡住。实施者取证：两个新文件与 brief 逐字一致（脚本比对）；临时把桩清空做对照实验得 7/7 通过，再还原并复验一致——证明数据与引擎语义本身没问题，唯一障碍是桩。
Task 5: 裁定 B —— 冒烟测试整体挪到 Task 6（不在 Task 5 里过滤成 document 再指望 Task 6 取消过滤：那样一旦忘记，护栏会静默降级）。plan 已改：Task 5 预期 7→6，Task 6/7 的 10/15 不变（commit 待实施者提交后落）。
Task 5: controller 侧事故与处置 —— 我为上述计划修正发起的提交被 pre-commit 挡下（实施者的 7 个测试在当时的工作区里，全量 199/200、1 失败）；且该计划改动一度处于**暂存态**，实施者若此刻提交会把我的改动卷进它的提交。已 `git restore --staged` 拆开，我的计划改动留作未提交，待其提交后再提。
Task 5: 实施者交付 94d36cc（AiToneLintService.cs + AiToneLintServiceTests.cs 共 195 行，6 测试）；聚焦 6/6、全量 199/199、构建 0 错误 0 警告、`dotnet format` 退出码 0（未用 --no-verify，提交只含那两个文件）
Task 5: 交付形态 —— 按裁定 B 移除冒烟测试；生产代码与规则数据未改；`AllRuleIds()` 未实现（YAGNI：brief 代码块与 6 条测试都不用，计划里它属 Task 7）
Task 5: controller 订正三处计划缺陷（commit dcc2e01）—— 冒烟测试归属 Task 6、Task 7 预期数 15→16（漏计了 Severity 契约测试）、Task 5 接口里越界的 `AllRuleIds()`
Task 5: 计数分歧 —— 实施者两次主张 Task 6 应为 11（其推导基于"Task 5 仍是 7 条"）；经实测区间 [Fact] 计数（Task 5 区间 6、Task 6 区间 4）确认 Task 6 = 10 ✓，偏差在 Task 7，已回复澄清
Task 5: task-5-brief.md 已按修订后计划重生成（原 brief 含已移走的冒烟测试与 PASS 7，会让评审者误判缺项）
Task 5: 评审者裁决 —— Spec [OK] 全项通过、Critical 0、Important 0、Minor 6、Task quality Approved
Task 5: warnings resolved —— 署名正确；门禁由 controller 独立复跑（正确读法）：构建 0、199/199、格式化退出码 0 且输出 0 字节
Task 5: 两条 Minor 折入后续任务计划（commit 68b7d9b）—— Snippet 右边界覆盖 + MinCount 按 Pattern 口径各补一条测试（Task 6，预期 10→12）；组内按 (行,列) 升序渲染 + 倒序输入断言（Task 8）
Task 5: minor (deferred): Position 每次命中从头重扫，O(长度×命中数)，大文件会显形（计划逐字指定）
Task 5: minor (deferred): 跨行命中时 Snippet 不含 Match 原文（brief 要求去换行）
Task 5: complete (commits ae6e371..94d36cc, review clean — 0 Critical/Important, 4 minor deferred, 2 warnings resolved)

环境事故（阻塞约 1 小时）:
- 症状：pre-commit 的全量测试出现 7 条 EncodingDetectorTests 失败，报 System.IO.IOException 磁盘空间不足（写 Temp 失败）
- 根因：E 盘 20G 撑满（剩 6.6M）；元凶是单个 13.6G 文件 —— 包内重定向 TEMP 下另一个会话的后台任务输出：…\Temp\claude\E--work-zone\367bafef-d302-44f4-8228-114b35ff9869\tasks\b7b1p14qw.output（最后写入 14:42，实测 5 秒内未增长）
- 处置：由用户清理；清理后 E 盘 13G 可用（36%），此前被挡下的计划提交随即通过
- 教训：包内重定向 TEMP 与工作区共用 E 盘，后台任务输出可无上限增长；E 盘满的显性症状是"测试报磁盘空间不足 + pre-commit 挡提交"

BASE(task 6) = 68b7d9b
Task 6: implementer 交付 31e8d5c（AiToneLintService.cs +38/-1、AiToneLintServiceTests.cs +75；新增 6 条测试，含端到端冒烟与两条补强）
Task 6: 实施者订正计划的代码缺陷并上报 —— brief 里的 ScanParagraphScope 把各段命中**累加**后与 MinCount 比较，与同一份 brief 的用例（`Scan_段内序数词需同段出现两次才报`）互相矛盾；按用例实现为**逐段判定**（符合规则名「段内」口径）
Task 6: controller 订正（commit 57f1eb1）—— 计划与规格的 MinCount 口径写明"Scope=paragraph 时按段各自判定"；全局约束补上提交信息末尾的 Co-Authored-By trailer（此前计划十条提交命令全漏，Task 6 的提交因此未带）
Task 6: 实施者另一处自纠 —— 写冒烟测试时零宽空格落成真字符，经字节级探针改为可见转义（与 f58032d 的同类订正一致）
Task 6: 评审者裁决 —— Spec [OK]、Critical 0、Important 1、Minor 4、Task quality Approved（评审者对位置换算、CRLF 偏移、TailChars 语义、两条测试的可失败性做了独立复算）
Task 6: Important 裁决（park）—— 31e8d5c 缺 Co-Authored-By trailer。本工作流 10 条提交中 d66fc1f / b0bb2a2 / 31e8d5c 三条缺，成因是计划里十条提交命令全漏（trailer 是我事后才补进全局约束的），实施者是忠实执行 brief。裁定**不回改历史**（遵从本机既有「历史提交不改」；为署名元数据 rebase 7 个已提交、每个重跑全量测试，代价与收益不成比例）。向前收口：commit 40b19ac 已给剩余四任务的提交命令显式补上 trailer。
Task 6: minor (deferred): 实现注释措辞与 brief 漂移（「任务名」vs「规则名」，纯文字）
Task 6: minor (deferred): Paragraphs 的行首空白与 CRLF 分支零测试覆盖（评审者手推算术正确）
Task 6: minor (deferred): 「非空行即一段」会把 Markdown 软换行拆开，S2/S3 因此少报（计划已自陈的简化，非代码缺陷）
Task 6: minor (deferred): windowStart 可提到 Pattern 循环外（可忽略；brief 同形）
Task 6: complete (commits 68b7d9b..31e8d5c, review clean — 0 Critical, 1 Important parked, 4 minor deferred)

BASE(task 7) = 40b19ac
Task 7: implementer 交付 7b2df6b（AiToneLintService.cs +123 至 260 行、AiToneLintServiceTests.cs +59 至 18 条测试；带 trailer ✓）；全量 211/211、构建 0 警告、格式化退出码 0
Task 7: 实施者自审第 1 条（实质，已交评审者定级）—— 6 条新测试只有 2 条真正驱动实现（P4/C1 各一条正向），P5/C2/C3/C4/C5 五条规则零直接覆盖；实施者明确这是 brief 测试集自身的强度问题、未擅自扩测
Task 7: 实施者另报的观察（无动作）—— (a) `--min-severity warn` 会静默吞掉全部算法命中（P 组无 warn 级，与"info 不进退出码"的设计一致，Task 9 接线时须知）；(b) 文件 260 行高于计划"接近 200"的估算，但新增行全是 brief 强制代码；(c) P5/C2 每次调用构造正则（单文件扫描可忽略）
Task 7: 评审者已派发（BASE 40b19ac, HEAD 7b2df6b）
Task 7: 评审者裁决 —— Spec ✅、Critical 0、Important 0、Minor 5、Task quality Approved（评审独立复算越界与除零路径、查证 RegexGuard 是回溯引擎而非 NonBacktracking、核对 SuggestScheme 指向的内置方案真实存在、由 hunk 头反推的 TDD 行号与报告吻合）
Task 7: warnings resolved —— (a) CA1861 归属实测：`--severity info --diagnostics CA1861` 全仓 28 处，含 LineMergerTests(9)/VNReformatterServiceTests(8)/SelfUpdater 等既有文件；新测试行确在其列，但属既有模式且不进默认门禁（默认门禁退出码 0、输出 0 字节）；(b) 两个变更文件 BOM=false、CRLF=0；(c) P4/P5 为 info 级与退出码的关系由规格定义（`--min-severity warn` 就是要吞掉 info 级命中；不加该开关则计入），非缺陷
Task 7: Minor 1（P5/C2/C3/C4/C5 零正向覆盖）折入 Task 8（commit 2056900）；Minor 5（P4/P5 启发式误报面）写进 Task 10 的 README 段落
Task 7: minor (deferred): 新增 public 成员 AlgorithmRuleIds/AllRuleIds 无中文 XML 注释
Task 7: minor (deferred): AlgorithmRuleIds 把底层数组当只读表暴露（Array.AsReadOnly 一行可修）
Task 7: minor (deferred): 正则构造位置不一致（QuoteStyles/OrdinalSkeleton 静态，P5 每次 3 个、C2 每次 1 个）——Task 9 按文件循环会放大
Task 7: minor (deferred): `Scan_统计项不进Hits` 近似不可失败（Notes 与 Hits 是两个集合）
Task 7: complete (commits 40b19ac..7b2df6b, review clean — 0 Critical/Important, 5 minor deferred, 3 warnings resolved)

BASE(task 8) = 2056900
Task 8: implementer 交付 5ef14b3（3 文件：LintTextFormatter.cs +59、LintTextFormatterTests.cs +53、AiToneLintServiceTests.cs +50 补 5 条）；带 trailer ✓；全量 220/220
Task 8: 5 条补覆盖用例首跑即 23/23 全绿 —— 独立印证 Task 7 的实现与其门槛一致（若有一条红则说明 Task 7 实现有误）
Task 8: 实施者报的两处有意偏差 —— (a) `git add` 补上第三个文件（brief 步骤 5 漏列；不收进来会形成「钩子测过却没提交」的空档）→ 计划已修（commit b5ccfc5）；(b) 测试源码里不可见字符写成 `\uXXXX` 转义而非裸字符（与本工作流既有约定一致）
Task 8: 评审者裁决 —— Spec ✅、Critical 0、Important 0、Minor 4、Task quality Approved；自报的两处偏差被判为恰当处置
Task 8: warnings resolved —— 署名 TwilightRainDev + trailer 齐备；构建退出码 0、格式化退出码 0（零输出）、全量 220/220
Task 8: minor (deferred): Format 的测试未断言 Detail 的渲染分支
Task 8: minor (deferred): 5 条补覆盖用例只钉文案不钉数值（如 C4 的密度）
Task 8: minor (deferred): Format 输出末尾无换行 —— Task 9 的 CLI 用 `+ Environment.NewLine` 补（计划代码已含）
Task 8: minor (deferred): Visible 的转义集不含 emoji 等"GBK 编不出的可见符号"——Task 9 已设 `Console.OutputEncoding = UTF8`（计划代码已含）
Task 8: complete (commits 2056900..5ef14b3, review clean — 0 Critical/Important, 4 minor deferred, 2 warnings resolved)

BASE(task 9) = b5ccfc5
Task 9: implementer 交付 72cccc4（TextTool.Cli/Program.cs +101 行，单文件）；brief 规定的 6 条路径 + 6 条额外边界全部实测，退出码一律以不接管道方式取值；全量测试保持 220/220
Task 9: controller 独立冒烟（四条亲跑）—— 有命中退 1（人读输出分组/行列/片段/说明均正常）、干净文本 `[OK]` 退 0、`--json` 为 PascalCase 且中文未转义、`--min-severity warn` 只留 warn 级命中且退 1（info 级 L2 被正确滤掉）
Task 9: 实施者自审两条（无动作）—— (a) `--only ""` 显式空串按空过滤集处理（命中 0、退 0），计划未定义该用法；(b) 重复读 stdin 第二次为空（无用例，无害）
Task 9: 评审者裁决 —— Spec ✅、Critical 0、Important 1（plan-mandated）、Minor 6、Task quality Approved；评审以机器比对确认 计划代码块 vs Program.cs:301-398 = VERBATIM MATCH
Task 9: warnings resolved —— (a) task-9-brief.md 确实不存在：controller 漏跑 task-brief 抽取脚本；实施者与评审者各自发现并以计划章节替代、均如实上报，现已补生成；(b) 署名 TwilightRainDev + trailer ✓；(c) 门禁复跑（正确读法）：构建退出码 0、220/220、格式化退出码 0（零输出）
Task 9: Important 裁定 = 修 —— `--only ""` 经 RemoveEmptyEntries 得空数组 → 过滤集空 → `[OK]`/退 0；CI 里 `--only "$RULES"` 变量为空即假阴性，与计划自身「宁可响亮失败」立场冲突。计划与规格已补显式报错（commit 683b517）；修复轮 1/5 已派回 impl-task9
Task 9: minor (deferred): `--min-severity` 大小写敏感（与 --only 的不敏感不对称）；类头 XML 注释缺 lint；`--only` 路径重复 Load；单文件输出尾部多空行；OutputEncoding 的 guard 只捕 IOException；报告精度小疵
Task 9: 修复轮 1/5 完成 —— commit c445ca3（TextTool.Cli/Program.cs +4：onlyIds 为空数组即抛 ArgumentException）；controller 亲测四条：`--only ""` → 退 2 且 stderr 给出正确原因、`--only S1` → 1、`--only NOPE` → 2、`--only p7`（小写）→ 0（0 属正常：规则被接受但该文本无命中）
Task 9: scoped re-review 已派发（FIX_BASE 72cccc4, HEAD c445ca3）
Task 9: scoped re-review 裁决 —— Finding ADDRESSED（Program.cs:336-339 守卫 + RunLint 自身 catch 退 2；评审独立实测 `--only ""` 与 `--only ","` 均退 2，默认路径仍退 1、4 命中）；New Breakage 无；Verdict = All findings addressed, no new Critical/Important breakage
Task 9: complete (commits b5ccfc5..c445ca3, review clean — 0 Critical, 1 Important fixed, 6 minor deferred, 3 warnings resolved)

BASE(task 10) = c445ca3
Task 10: implementer 交付 2c4db85（README.md / doc/TECH-DEBT.md / Directory.Build.props；+70/-13）；带 trailer ✓；全量保持 220/220；版本 2.5.0 已落地
Task 10: 实施者关切 (c) 最有价值 —— **brief 的「info 级默认只报不卡」是错的**：实测 info 命中默认（不加 --min-severity）仍让退出码变 1，只有 --min-severity warn 才排除。README 按实测书写，计划已同步订正（commit fbf4d6a）
Task 10: 实施者关切 (b) —— brief 未列但已做的四处一致性修正：README 结构树版本行、README 测试数 139→182（口径为 [Fact]/[Theory] 属性计数，非执行用例数）、TECH-DEBT 版本锚点、props 注释里的派生 AssemblyVersion
Task 10: 实施者关切 (a) —— README 功能表首列是「页签/Tab」而 lint 是纯 CLI，实施者写成 `**CLI · lint**` 且未动表头；处置待评审判断
Task 10: TECH-DEBT 新增 D-5 —— R0 遗留的两份内联兜底待删（含方法行号与实测条目数），是工作流里第一条被正式登记的技术债
Task 10: 评审者裁决 —— Spec ✅、Critical 0、Important 0、Minor 4、Task quality Approved；评审把 README 的每条断言逐一对回源码行（退出码、info 级语义、C 组不进退出码、D-5 的 6 处行号、测试锚点实测 182）
Task 10: warnings resolved —— 署名 TwilightRainDev + trailer ✓；格式化退出码 0（零输出）；全量 220/220；`git tag` 实为 10 个且无 v2.5.0，故 README 保留「最新 v2.4.4」正确
Task 10: minor (deferred, 交最终评审分诊):
  - M-1 README 功能表首列是「Tab / 页签」而 lint 是纯 CLI：实施者写 `**CLI · lint**`（诚实、未伪造页签），但表头与「5 个页签」的表述未随之调整
  - M-2 README「These rules are all info level」把没有 severity 概念的 C 组也扫了进去；中文「默认只报」有歧义
  - M-3 **规格与实现的契约不一致**：规格 :233 写「`--only` 同时作用于 hits 与 notes」，而实现（`LintReport.Filter`）只过滤 Hits、Notes 原样透传；README 与 CLI help 的「只跑指定规则」因此对 notes 不成立。两种修法（改码让 notes 也按 id 过滤 / 改文档说明 notes 不受影响）交最终评审裁决
  - M-4 `doc/TECH-DEBT.md:118` 的版本锚点命令会匹配到文件自身注释里的 `<Version>`
Task 10: complete (commits c445ca3..2c4db85, review clean — 0 Critical/Important, 4 minor deferred, 4 warnings resolved)

Task 11（仓外，无 git 提交）: implementer 交付 —— SKILL.md 改写为 lint 三步流程 + 规则码映射表 + `--only`/退出码的实际行为段；删除纪律到位（先列 3 条清单 → 逐项核对 EXISTS/KEEP-OK → recycle-remove.ps1 干跑再 -Apply）
Task 11: 目录末态 —— SKILL.md(12182 B) + scripts/install_scheme.py + references/humanize-scheme.json；tone_lint.py 与 __pycache__ 已回收；SKILL.md 无 BOM/无 CRLF/无 emoji；frontmatter name 与目录名一致、description 仅触发条件
Task 11: 真实文本验证暴露三条发现（交最终评审分诊）:
  - C2（中）机械层方案 `引号括号统一` 把全角括号转半角，反而制造新的 P3 命中（实测 P3 x3 → x7）；而 P4/P5 的 SuggestScheme 正指向该方案，两个 P 组规则的建议互相打架。已在技能「已知边界」与「红旗信号」写明"每跑一条方案就重跑一次 lint"
  - C3（低）旧脚本的弱序号统计项（`第[一二三四五]` 等）未迁移，映射表如实标注「未迁移」，未编造对应；若需要可另开 C6
  - C4（低）P3 的两条正则命中按「先全部 NEXT、再全部 PREV」分组，同一行内 Col 非单调（与旧脚本一致、非本次回归）；人读报告已由渲染层按行列排序，raw JSON 消费方需自行排序
Task 11: 评审者裁决 —— Spec ❌（Critical 0、Important 1、Minor 4、Task quality Needs fixes）；评审独立实跑核实了删除项/保留项/体例/frontmatter/映射表/`--only` 契约，并确认仓库 `replace_schemes.json` 未被验证过程污染
Task 11: Important 裁定 = 修（一行）—— `SKILL.md:35` 断言「`...`、`......`、`。。。` 一律转 `……`」被实测证伪：`。。。` → `。。` → `。`，永远变不成 `……`，9 个内置方案无该映射；而 `P2` 的 SuggestScheme 正指向该方案，下游会误以为已修好。该行是改前版本逐字沿用（非本轮引入），但本任务职责即"按真实 CLI 复核描述"。修复轮 1/5 已派回 impl-task11
Task 11: 连带发现（交最终评审）—— 技能自带方案 `去AI味标点归一` 的 Description 自称「省略号统一」，实测却是重复标点折叠先行、单次跑不出 `……`：方案自述行为与实际不符（`references/humanize-scheme.json` 的 Description 与规则顺序问题）
Task 11: minor (deferred): `SKILL.md:97` info 示例漏 `L1`；`:34` 未说明 U+200C/U+200D 方案不修但 `P7` 会报；`:142`/`:180` 缺交叉引用；`:47` 前向引用
Task 11: 修复轮 1/5 完成 —— SKILL.md 省略号那节按实测重写（`...`/`....`/`......`/单个 `…` → `……`；`..` 无规则；`。。。` 归重复标点折叠、一遍收一格、永不 `……`），并给出实测 输入→输出 表
Task 11: controller 独立取证与本轮一致 —— `甲……乙。丙。。。丁。戊......己。庚...辛。` → `甲……乙。丙。。丁。戊……己。庚……辛。`
Task 11: scoped re-review 裁决 —— Finding ADDRESSED（评审独立实测：`...`/`....`/`......`/单个 `…` 一律转 `……`、`..` 与 `……` 不变、`。。。` 一遍 `。。` 两遍 `。` 永不 `……`，两遍 EXIT 均 0；10 个已装方案无任何 `。`串→`……` 规则）；New Breakage 无
Task 11: minor (deferred): `SKILL.md:181` 的「只」过宽；末句复核粒度不足
Task 11: complete (仓外，无 git 提交 — review clean 经一轮修复：0 Critical, 1 Important fixed, 6 minor deferred)

=== 全部 11 个任务完成，进入最终整分支评审 ===

最终整分支评审（最强模型，range abc56fb..fbf4d6a = 34 提交 / 193 KB）:
- **Verdict: FAIL —— 3 组阻塞**（2 组代码契约缺口 + 1 组文档一致性），判「都是小改动：代码约 15 行 + 测试 2 条 + 文档约 12 行，改完即可视为 done，无需重做或回改历史」
- **I-1** `--only` 不过滤 `Notes`（规格 :233 为权威；核心证据是 `--only C1` 打出的是 C5 的统计项）→ 改码对齐规格
- **I-2**（本次新发现）规格 :192 要求「JSON 输出同理」转义隐形字符，实现在 `Match`/`Snippet` 里给的是原始 3 字节 → 改码（序列化后整体转义；JSON 解析方语义不变，只是在"看 JSON 文本"时可见）
- 文档收口：README 功能表表头（双语）、README info 级主语收窄到 P4/P5、TECH-DEBT 版本锚点正则、`ArchitectureGuide.md` §8 结构树陈旧（与 README 打架）
- 技能侧同步：`SKILL.md:95`/`:172`（随 I-1）、`:183`（随 I-2）
- **分诊**：约 50 条 deferred minor 逐条判「留/修」并写明理由；两处 parked 项复核通过（trailer 不回改历史、`--only ""` 守卫已验）
- 评审自陈这两类问题**只有在这一层才能发现**：逐任务评审都拿 plan 当基准，看不到"plan 与 spec 的分叉"（M-3 的分叉正是写 plan 时引入的），也看不到跨仓的 SKILL.md 与规格互相矛盾
- Open Questions 均由评审给出裁决，我按其执行：Q1 转义 vs 原字符 → 按规格转义；Q2 是否删 `P2` 的 `。{3,}` → 判「留双报」（报告层去重会丢掉一条建议、且破坏 `--only` 按规则选择的契约）；Q3 README 补副作用说明 → 采用；Q4 技能侧同步 → 由本轮 fixer 一并做

修复波已派发（单 fixer，带完整 findings 清单 + 可留项聚合登记进 TECH-DEBT）
修复波完成（2 提交）—— `b10d67a` fix: `--only` 同时过滤统计项 + `--json` 转义隐形字符；`b4a8e46` docs: 文档一致性收口（README 表头/info 主语、TECH-DEBT 锚点、ArchitectureGuide §8 结构树、计划 Task 4 代码块同步）+ 遗留项登记进 TECH-DEBT
controller 独立验证（两条契约亲自复跑）:
  - I-1：不加 `--only` 时 C1+C5 都出 → `--only C1` **只剩 C1**（C5 被滤掉，修复前会打出 C5）→ `--only C5` 只剩 C5 ✓
  - I-2：JSON 文本里是可见的 `"Match": "​"`，且 `JSON.parse` 解析回来仍是真 U+200B ✓ 语义未变
controller 门禁复跑（正确读法）：构建退出码 0、**221/221**（+1 新测试）、格式化退出码 0（零输出）；两提交署名 TwilightRainDev、trailer 齐备
最后一次 scoped re-review 已派发（FIX_BASE fbf4d6a, HEAD b4a8e46）

