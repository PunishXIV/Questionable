# Delegation baseline and handoff audit

This document preserves dated decisions, implementation checkpoints and verification evidence. [OVERHAUL-PLAN.md](OVERHAUL-PLAN.md) is the current scope and remaining-work index; start with its [outstanding issues](OVERHAUL-PLAN.md#outstanding-issues-and-parked-work) and [live checklist](OVERHAUL-PLAN.md#pending-live-validation).

Read each result against its recorded snapshot. Earlier passing tests, source hashes and capability-harness results do not validate later edits. Original finding tables are static baseline evidence, with historical line references; later fixes may supersede them. They are not a fresh defect list or authorization to change working behavior.

## Recovery-status display cleanup (2026-11-01)

Crafting, purchasing and gathering now share one private rendering helper in `ActiveQuestComponent` for the recovery button, its existing confirmation callback and the two wrapped instruction lines. Each provider retains its original operation/status checks and reconciliation guard. Localized strings, button eligibility and callback-before-instructions ordering are preserved. Fishing, duties, combat and working dialogs are untouched; Feesh remains parked.

Validation: the isolated Debug build passed with 0 errors and the existing SixLabors license warning. Snapshot comparison confirmed only this production file changed before this audit entry; localized literals and their order are unchanged, the built source matches, and whitespace checks passed. No new tests were added for this rendering-only extraction, and no live UI validation, deployment, reload, commit or push was performed.

## Crafting test setup cleanup (2026-11-01)

Three crafting fixtures now use `QuestTestData.LoadQuest` for their repeated embedded-resource deserialization and quest construction. Resource selection, the shipped-path scan, sequence/step references, scenarios and assertions are preserved. Only four test files changed; no production code, dialog handlers or Feesh work changed.

Validation: the isolated Debug build succeeded with the two existing SixLabors license warnings; **146 crafting tests passed**, 0 failed and 0 skipped. The sandboxed runner initially timed out connecting to testhost without executing tests; the same built binaries passed outside the sandbox. The four source files match the tested mirror, and targeted whitespace checks passed. No live deployment, reload, commit or push.

## Solo-duty reload diagnosis (2026-11-01)

The reported In from the Cold Proceed prompt coincided with BossMod's failed dev reload at 19:07:46: a module pack referenced the removed `BossMod.SoloDutyAutomationAttribute`. QST subsequently queued `Interact(1039932), EndAutomation` at 19:19:56, with no solo-duty tasks. The existing unavailable-provider gate explains this behavior; solo duties remained enabled and duty 793 was whitelisted. The supper confirmation was accepted successfully.

Read-only inspection subsequently found all three deployed dev packs matching their Debug outputs and no old attribute reference. The next live check is a full BossMod unload/reload after all packs finish building, followed by restarting QST's step. Successful initialization and solo-duty entry remain unconfirmed. No dialog or runtime code was changed for this diagnosis.

## Documentation consolidation (2026-11-01)

The plan now separates current implementation status, unresolved reports, parked scope and completion checks. Repeated implementation summaries were replaced by links to this audit. Unique crafting ownership/grouping details and the original rollout decisions were retained below. This documentation-only pass leaves working dialogs untouched and Feesh parked; it adds no runtime changes or new gameplay-validation claim.

Validation: all 15 relative document links/anchors resolve, historical evidence links are retained, and the unique details moved from the plan were checked against the saved before versions. Only the two documentation files changed during this pass; whitespace checks passed. No build or runtime tests were run for this documentation-only change.

Feesh clarification from the supplied conversation: Wah reported IPC ready for testing and planned default exclusion of fish required by unfinished quests, plus an explicit exclusion parameter for other needs such as crafting. Protections must account for interrupted work and later logins; an IPC lease alone does not establish the needed lifetime. Endpoint names/signatures, supported build and actual exclusion semantics have not been supplied/verified. The earlier candidate fish-ID/additional-quantity request remains a proposal. This clarification does not resume fishing work.

## Direct preset simplification (2026-11-01)

This update supersedes the capability-gate design and its historical test results below. The seven BossMod duty modules remain, while both support files and the registry/IPC additions are removed. QST uses direct preset activation, retaining its ownership and cleanup logic; Pâtisserie (CFC 1045) selects NormalMovement. The activation tests now cover all 14 migrated duties and an ordinary duty without the capability endpoint.

BossMod.Modules Release passed with 0 errors and 12 existing package warnings. QST Release and the focused BossModIpcTests/SoloDutyTests run were blocked before compilation/test execution by ImageSharp 4.1.2 requiring an unconfigured Six Labors license. Earlier passing suite results are historical, not validation of this update. No commits or pushes.


## Shared test clock cleanup (2026-11-01)

Authorized test-only cleanup: extracted six identical nested clocks into `TestClock`. AutoDutyFixture, CraftingOperationStateTests, FishOperationTests, GatherChillIpcTests, PurchaseTests and PurchaseOperationStateTests each retain their own instance. Comparison against the preceding files confirms only the field type and duplicate nested class changed; every scenario, time advance, poll and assertion is preserved. The helper's clock implementation is unchanged and no dependency was added.

Validation: **9,470 tests passed**, 0 failed and 0 skipped, in the isolated mirror, with only the two existing SixLabors license warnings. All seven test files hash-match the tested copies and targeted whitespace checks passed. Incremental diff: `Questionable.Tests/bin/test-clock-cleanup.diff`. Concurrent edits to `BossModIpc.cs` and `SinglePlayerDuty.cs` appeared during this task and were left untouched; those edits are not included in this isolated test result.

This task made no production or dialog edits. Feesh remains parked. No live build, deployment, reload, commit or push.

## Navigation test setup cleanup (2026-11-01)

Authorized test-only cleanup: four navigation test fixtures now share their common substitutes, cancelable-pathfinding IPC registration, controller construction and disposal through `NavigationTestFixture`. Individual fixtures retain their own IPC responses, status availability, pending-result behavior, clocks, destinations, player/mounted state and movement override. Test methods and assertions are unchanged; the extraction removes 62 lines overall without adding dependencies.

Validation: the full isolated suite passed **9,470 tests**, 0 failed and 0 skipped, with only the existing two SixLabors license warnings. All four test bodies match the preceding versions, the five test files hash-match the tested mirror, production source hashes are unchanged, and targeted whitespace checks passed. Incremental diff: `Questionable.Tests/bin/navigation-setup-cleanup.diff`.

Working dialogs are untouched. Feesh remains parked until the actual IPC details are available. No live build, deployment, reload, commit or push.

## Reject stale sequence completions (2026-11-01)

Authorized narrow fix: added the missing return after the sequence-mismatch warning in `QuestController.IncreaseStepCount`. An old-sequence completion can no longer advance the current sequence's step or reset its progress. This is the only production change; working dialog handlers are unchanged.

Six regression cases exercise the real transition method with managed quest data: stale, matching and unspecified sequences, each against one-step and multi-step sequences. Against the preceding source, the two stale cases failed by advancing to step 1 or marking the sequence complete (255); the four controls passed. After the fix, the full isolated suite passed **9,470 tests**, 0 failed and 0 skipped. Stale completions preserve the original step-progress object and point-menu counter; valid transitions retain their existing behavior. Only the two existing SixLabors license warnings remain. Source/test hashes match the tested mirror, all three dialog handlers match the preceding mirror, and targeted whitespace checks passed.

No live build, deployment, reload, commit or push. This corrects a pre-existing guard bug with a known simulation path; it is not confirmation that the user's live premature acceptance/turn-in symptom is resolved.

## Quest acceptance and turn-in damage interruption (2026-11-01)

Authorized targeted fix: `WaitQuestAcceptedExecutor` and `WaitQuestCompletedExecutor` now allow damage interruption. These are the only production changes. Existing interruption handling queues combat when the combat condition is active, replays the preceding interaction, and retains the original quest-state wait. Quest completion checks, dialog handlers and provider implementations are unchanged.

Regression coverage exercises the real shared damage handler, both wait executors and the replay queue. Both grounded cases failed against the preceding source because no combat was queued. With the fix, combat precedes the replayed interaction, the original wait appears once, pending task order is preserved, and both flight cases remain uninterrupted. The full isolated suite passed **9,464 tests**, 0 failed and 0 skipped, with only the two existing SixLabors license warnings. Both edited source/test files hash-match the tested mirror; targeted whitespace checks passed.

No live build, deployment, reload, commit or push. Native quest reads and live combat-provider activation are not exercised by these regression tests. The reported gameplay incident still needs live confirmation; the separate early combat-flag timing gap and premature quest-completion report remain unresolved.

## Title-bar status rendering cleanup (2026-11-01)

Authorized scope: consolidate repeated title-bar pill rendering without changing status selection. `ActiveQuestComponent.DrawTitleBarPill` selects text and color using the same eleven branches, then renders once. Branch priority, translated labels, colors and alignment are unchanged. Working game dialogs and automation behavior were not changed.

Validation: **9,460 tests passed**, 0 failed and 0 skipped, in the isolated mirror. Only the two existing SixLabors license warnings remain. The source file hash matches the tested copy, and the targeted whitespace check passed. No live visual verification, live build, deployment, reload, commit or push. Incremental diff: `Questionable.Tests/bin/title-status-cleanup.diff`.

## Crafting startup cleanup (2026-11-01)

Authorized scope: simplify crafting startup bookkeeping while preserving current behavior and leaving working dialogs alone. Only `Craft.cs` changed in production code.

- `hasItemTarget` is local to startup instead of stored on the executor.
- List inventory counts and objective requirements are prepared locally before submission. A successful list submission installs them before TrackSubmission; missing or unsuitable lists retain the original recipe targets and counts without temporary field swaps and restoration.
- Inventory read order, early objective checks, list eligibility, fallback decisions, quantity/quality rules, provider ownership and progress timing are unchanged. Submission cleanup uses operation ownership rather than the staged objective fields. No new helper framework, dependency or test seam was introduced.

Validation: the full isolated suite passed **9,460 tests**, 0 failed and 0 skipped. Only the two existing SixLabors license warnings remain. Existing crafting tests cover list selection, batching, inventory requirements, progress and provider ownership; they do not directly execute native DoCraft.Start. Static review found no supported submission callback that reads the staged objective fields. Craft.cs matches the tested mirror; both dialog handlers match the preceding baseline. No live build, deployment, reload, commit or push. Incremental diff: `Questionable.Tests/bin/crafting-startup-cleanup.diff`.

## Task scheduling and cleanup simplification (2026-11-01)

Authorized scope: simplify QuestController, MiniTaskController and TaskQueue while preserving current behavior and leaving working dialogs alone.

- QuestController uses early returns in Stop and CheckNextTasks. Stop still captures running state before cleanup and always attempts cleanup, including when already stopped. Provider cleanup, manual-mode notification, stop commands and refresh timing retain their order.
- MiniTaskController flattens task startup and shares the interruption queue submission and cancellation-failure handling across combat and noncombat branches. Optional unmount, combat and wait tasks retain their order. Starting or skipping a task still returns until the next update.
- TaskQueue reuses TryPeek, removes an unnecessary intermediate replay list and uses OfType for null filtering. Replay is still captured before reset; failed cancellation still prevents replacement tasks from being queued.

Retained checks before and after external callbacks, cleanup retry calls, ownership barriers, notification boundaries and all existing delays. No new abstractions or dependencies. The two dialog handlers hash-match the preceding tested baseline.

Validation: **9,460 tests passed**, 0 failed and 0 skipped, in the isolated mirror. Strengthened replan tests assert cleanup order and executor detachment; three added replay cases cover completed redoable history, a current task with either redo policy, and no current executor. Only the two existing SixLabors license warnings remain. All five changed source/test files hash-match the tested copy, and git diff whitespace checks passed. No live build, deployment, reload, commit or push. Incremental diff: `Questionable.Tests/bin/task-scheduling-cleanup.diff`. Automated checks do not replace live gameplay verification.

## IPC wrapper and lifecycle cleanup (2026-11-01)

Authorized scope: simplify existing wrappers and lifecycle handling while preserving working behavior. The user confirmed that quest startup works after the preceding dialog fix; this cleanup leaves the dialog handlers unchanged. Later duty handoff and completion still need live verification.

- RSR now uses a direct framework-thread guard instead of a generic delegate wrapper that never marshalled calls. Guard placement, exception boundaries, provider identity, mode restoration and verification intervals are preserved. Removed its unused targeting enum and uncalled disposal helper; the combat module still owns disposal.
- Removed unused navmesh SimpleMove mutation wrappers and subscribers. Cancelable pathfinding, route submission and status queries remain. Path-running reads reuse the existing nullable read helper with the same unknown-on-error behavior.
- Gather n Chill child-status reads use one exception boundary instead of nested catches and nullable checks. All required child statuses are checked before deciding idle; an unavailable status still prevents confirmation.
- Knightshopper has one shared uncertain-submission exit. Rejected starts, accepted operation IDs, failure reporting and cancellation remain unchanged; its internal status helper is private.
- Removed unused BossMod cleanup/deletion aliases and inlined one single-use deletion wrapper without changing IPC order or arguments.
- QST disposal now unregisters `Questionable.IsQuestLockedReason` and `Questionable.RedoLookupIndex`, which were previously left registered. A registration/unregistration pairing scan found no other unmatched direct callbacks in `Questionable/External`.

Retained the shared ownership state, Artisan's separate state, AutoDuty's distinct cancellation/entry timers, and RSR's mode reader. They handle different provider contracts and demonstrated failure cases; consolidating them further would require behavioral changes or more exceptions in a new abstraction. Fishing remains parked. No new dependencies or architecture were introduced.

Validation: the full isolated suite passed **9,457 tests**, with 0 failures and 0 skipped. Added coverage protects missing Gather child statuses and unknown/negative purchase results. Only the two existing SixLabors license warnings remain. No live build, deployment, reload, commit or push was performed. The incremental cleanup diff is saved at `Questionable.Tests/bin/ipc-lifecycle-cleanup.diff`.

## Simplification and regression review (2026-11-01)

The review covered all 109 pending Questionable files and 17 coordinated BossMod files, excluding the user's unrelated Zelenia changes. The correction batch keeps cancelable navmesh pathfinding and provider ownership tracking. Wrath uses its existing API wrapper with exception suppression disabled; a second handwritten set of IPC subscribers is unnecessary. Recovery confirmation buttons share a small UI helper, and repeated combat cleanup failures warn once per cleanup episode while retries continue.

Corrections address abandoned navmesh routes when movement is disabled, unreadable busy status from an available provider, obsolete quest tasks after a blocked replan, multiple BossMod presets during cleanup, normal BossMod combat-end clearing, and slow AutoDuty exits. Crafting's unreachable provider-loss grace period is removed: ownership loss already requires reconciliation before that helper runs.

The local log confirms that the In from the Cold prompt was detected and difficulty 2 was requested. Its one-shot fallback occurred while the cutscene condition remained set, so it could return without confirming or retrying. The repair retains quest and step identity and the exact prompt across bounded retries; a ready matching button can be confirmed while the cutscene flag remains set. Loading still postpones confirmation, and changed context cancels it.

The later supper log shows startup before quest discovery, followed by the explicit supper prompt while QST still tracks sequence 1, step 0. Startup now defers the existing full dialogue sweep until quest/tasks are available, then briefly retries explicit matching sequence choices until execution starts. It does not infer future solo-duty entry from an arbitrary prompt. Both the old one-shot fallback and immediate startup sweep already exist in the repository HEAD; these logs identify failure paths, but do not isolate which overhaul change first exposed them. Gameplay validation remains required.

The isolated full Questionable suite passed 9,452 tests with 0 failures and 0 skipped. These tests verify managed state, provider adapters, and regression cases; they do not execute the native game dialogs. This batch was built only in the isolated mirror, with no agent deployment or reload. The user's live log records a separate reload at 14:21:08.

Fishing remains parked pending Feesh IPCs. The review recorded an obsolete `WaitAtEnd` stop when AutoHook is absent despite an already-satisfied fishing objective, plus unused legacy AutoHook mutation wrappers; neither is changed in this batch. Existing helper tests and provider-registration slices do not establish full plugin startup without companions or end-to-end encounter behavior.

## Original scope and baseline (2026-10-31; updated 2026-11-01)

The following scope notes and baseline are historical. The direct preset update above supersedes the former Boss core/module-pack capability requirement and its passing harness results. They do not establish current-tree or gameplay validation.

Status update (2026-11-01): the user confirmed Wah will add Feesh IPCs. Fishing provider selection and integration are PARKED pending those APIs; no further fishing implementation until the Feesh API lands. Previously implemented/tested Gather and AutoHook source work remains the current historical baseline; do not roll back or delete it.

The candidate Feesh request is fish ID plus additional quantity, not an assumed available API. Before migration, verify the real published contract and quest coverage (21 of 33 catalogued, 12 missing), including data for all eight collectible targets. The complete solo-duty source handoff is applied with zero Questionable encounter overrides; the full Questionable suite, native metadata harness and final Boss Release build passed. Historical capability-harness results (38/38) apply to the now-removed implementation. The whole overhaul is not complete.

Scope update (2026-10-31): implement improvements within Questionable using existing plugin APIs. Changes to companion plugins are exceptions only when a concrete required behavior cannot be achieved safely within Questionable. The complete solo-duty handoff is now an explicitly authorized companion exception (2026-11-01), requiring the new Boss core and module pack. Other companion-API proposals below remain findings and possible future options. For normal crafting completion, verify every required item, quantity and quality and wait for Artisan to become idle.

Original audit date: 2026-10-31; Stage 3 update: 2026-11-01. Repository baseline: `f1f2d6185`, plus the pending aetheryte and configuration UI refactors.

This is an implementation backlog based on static inspection of adapters and their callers. The original finding tables are historical baseline evidence from 2026-10-31, with later progress and corrections recorded separately. The failure scenarios below have not been reproduced in game. SmartNav is being developed separately; no particular SmartNav IPC contract or map-link coverage is assumed here.

The migration order is defined by [OVERHAUL-PLAN.md](<D:/Dev Drive/Questionable/OVERHAUL-PLAN.md>). The completed crafting, purchasing and gathering/fishing implementations and their automated checks remain historical evidence. The Stage 3 isolated full suite passed 9,113 tests with 0 failures, up 52 from Stage 2's 9,061; the crafting result was 9,018. These are not Stage 4 results. Gather and AutoHook companion changes remain applied to their actual sources while fishing provider selection/integration is parked. Companion binary updates/reloads and live validation remain deferred to the maintainer's level 1 playthrough after the complete rework; the full overhaul is not yet complete.

## Stage 3 gathering and fishing (2026-11-01)

Historical baseline: implemented and verified by automated checks. At that checkpoint, all 66 changed/new Questionable C# and csproj files hash-matched the tested mirror; the live DLL baseline was unchanged. The source work and verification record below remain in place while further fishing implementation is parked pending Feesh IPCs.

- Gather uses the existing API 2 `GatherItem(uint, int additionalQuantity)`, which already supports collectibles. Questionable requests the missing qualifying quantity, verifies final quality/quantity, and waits for provider, navmesh, Lifestream and native idle. The earlier assertion that a collectability API change was confirmed necessary is withdrawn: no new collectability field, API, version or solver change is justified.
- Legacy Gather global stop is issued at most once, only for a known accepted request. Ambiguous submission or provider loss requires manual confirmation after stable idle. The local guard does not provide a remote ownership guarantee.
- The narrow Gather internal fix is applied to `D:/Dev Drive/Gather-Chill`: five source files use a transient request list/routes, preserving saved Keep entries, larger saved lists, routes and subsequent user edits. The API is unchanged. The isolated build passed with 0 errors and 13 preexisting warnings; the harness passed 14/14. Applied files hash-match the tested mirror and `git diff --check` passed; no binaries were built in the actual repository.
- AutoHook's additive owned fishing API v1 is applied to actual source across 12 files (11 existing and one new). It has five endpoints covering caller ID, start, status, stop and idle, retaining legacy APIs. It removes only its own preset, restores settings/selection only if unchanged, cancels its own queued work and acknowledges actual stop. Natural provider end settles as `Stopped`, allowing Questionable to verify the objective; internal owned disable settles as `Failed`, and manual disable as `OwnershipLost`. Older AutoHook cannot execute an unmet fishing objective; an already-completed objective can skip.
- Fishing supports ordinary single-preset rod fishing, not ocean fishing, independent reduction or preset swapping. All 13 live quest exports are structurally compatible. Manual takeover retains the barrier; acknowledgement requires valid native idle. Questionable verifies final inventory/quest flags before progression. Structural compatibility and automated tests do not replace the deferred live playthrough.
- AutoHook's final full isolated build passed with 0 warnings and 0 errors; its harness passed 23/23. The minimal existing compile fix adds `unsafe` to the private `EffectResultDetourDelegate` declaration in `HooksUpdateModule`. All 12 files hash-matched the reviewed/tested mirror before being copied into the actual repository; `git diff --check` passed. No `QuestPaths`, live DLLs or dependencies changed; no commits, pushes or live builds were made. Companion binary updates/reloads remain pending for the deferred in-game validation.

## Stage 4 duties and combat verification (2026-11-01)

Historical lifecycle checkpoint, before the complete solo-duty handoff: 9,231 tests passed with 0 failures and 0 skipped; all 83 changed/new Questionable C# and csproj files matched that mirror. No `QuestPaths` changes had been made at that checkpoint. Historical handoff evidence follows; the direct preset update at the top records the later change and its validation blocker.

- AutoDuty ownership persists across the entire duty sequence. Completion combines the duty-completion event, exit and stable idle, with temporary settings tracked and conditionally restored.
- Movement cleanup and quest preflight preserve the movement barrier for manual busy AutoDuty runs and for an available provider whose stopped status is unknown.
- Rejected rotation startup, lease loss and provider loss stop automatic continuation while retaining unresolved cleanup for the original provider, including after provider preference changes.
- Solo-duty snapshots survive task transitions and repeated activation. Conditional restoration preserves subsequent manual setting changes.

Legacy AutoDuty and BossMod APIs provide no remote session identity, so an unload/reload between observations cannot be detected. RSR mode reflection supports both assembly shapes and requires a readable mode. These constraints remain limitations of the verified Questionable lifecycle.

Final native/UI/in-game validation remains deferred to @Kagekazu's level 1 playthrough after the entire rework. The complete solo-duty source handoff below replaces Questionable's encounter overrides. The whole overhaul is not complete.

## User priority and solo-duty boundary (2026-11-01)

The user authorized the complete solo-duty handoff with zero Questionable encounter overrides or encounter fallback. All old `SpecialTerritories` branches are removed, including the commented Viper branch. Duty entry, difficulty, unlocks, quest outcome verification and retry remain in Questionable. The outcome grace timer resets during occupied or cutscene states without native access. Generic seasonal metadata discovery remains; QST selects its existing NormalMovement preset directly for Pâtisserie (CFC 1045). Native FFXIV documentation confirms `ContentsType.Regular` queue `Id` is the CFC; final entry confirmation verifies the requested duty so a different duty cannot satisfy it.

Pâtisserie is historical Valentine seasonal-event coverage and is currently unavailable. Its metadata fixture proves historical mapping, not current playability. It is excluded from the current in-game checklist and is not a final overhaul gate; no return date is assumed.

At the user's request, the capability gate and both SoloDutyAutomation support files were removed. QST activates QuestBattle directly, with NormalMovement for Pâtisserie (CFC 1045). All seven duty modules remain; encounter routing stays in BossMod. Existing preset ownership, provider checks and conditional settings restoration remain. No new Boss core IPC or module-pack revision is required.

Source migration and reported historical automated checks:

| Encounter | Territory | CFC | Finding and historical check |
| --- | --- | --- | --- |
| AnEgi-stential Crisis | 701 | 454 | [AnEgistentialCrisis.cs](<D:/Dev Drive/ffxiv_bossmod/BossMod.Modules/QuestBattle/Stormblood/ClassJobQuests/SMN/AnEgistentialCrisis.cs>) implemented; 7/7 historical behavioral groups passed. |
| Nightkin | 676 | 461 | New native quest module; 16 checks passed. |
| Bar the Passage | 1246 | 1016 | New native quest module; 11/11 groups passed. |
| Lahabrea | 1052 | 867 | Scripted-death protection added to existing `TheUltimateWeapon` coverage; 32 checks passed. |

Historical Egi evidence: [BossMod framework harness results](<D:/Dev Drive/Questionable/Questionable.Tests/bin/stage4-bossmod/harness-results.log>) and [isolated Release build](<D:/Dev Drive/Questionable/Questionable.Tests/bin/stage4-bossmod/build-final.log>). These logs predate the complete handoff.

Reused and adjusted BossMod coverage:

- [Naadam](<D:/Dev Drive/ffxiv_bossmod/BossMod.Modules/QuestBattle/Stormblood/MSQ/Naadam.cs>): existing routes and Ovoo position retained; dismount handling now covers all mounts.
- [ItsProbablyATrap](<D:/Dev Drive/ffxiv_bossmod/BossMod.Modules/QuestBattle/Stormblood/MSQ/ItsProbablyATrap.cs>): phase detection via duty action `7816`, smoke handling and forbidden guard targets.
- [TheWarmthOfFamily](<D:/Dev Drive/ffxiv_bossmod/BossMod.Modules/QuestBattle/Dawntrail/MSQ/TheWarmthOfFamily.cs>): existing interactions and Buffalo 3 retained; the encounter's leg target gets priority 1.
- [ReturnoftheHolyfist](<D:/Dev Drive/ffxiv_bossmod/BossMod.Modules/QuestBattle/ARealmReborn/ClassJobQuests/PGL/ReturnoftheHolyfist.cs>): standard rotation handling for the level 20 target with OID `0x36F`.
- Icing: only a `MovementOnly` marker was added at this checkpoint; the later direct preset simplification removed the capability support design.

The four data-driven target overrides moved to native quest modules: Matsuba Mayhem (CFC 250, territory 710, OID 7154), Heroes and Pretenders (1001, 1218, 17034), An Antidote for Anarchy (994, 1215, 16983) and A Rush of Cold Wind (1101, 1371, 19818). They prioritize ordinary targets and boost the initial target only until actual acquisition, then latch off for that module instance. QST's four shipped `Target` fields, model property and schema property are removed.

The following evidence predates the direct preset simplification. In particular, the 38/38 capability checks describe removed code, and the hashes match those older tested mirrors rather than the current working tree.

| Validation item | Historical handoff result / deferred check |
| --- | --- |
| BossMod isolated Release | Final full build with cache and `ResolveDuty` filter passed: 0 errors, 12 existing package warnings. |
| Four target modules | [28/28 compiled checks passed](<D:/Dev Drive/Questionable/Questionable.Tests/bin/stage4-targets/harness-results.log>). |
| Boss capability harness | [38/38 offline checks passed](<D:/Dev Drive/Questionable/Questionable.Tests/bin/stage4-coverage/run-optimized.log>): native raw Quest mappings with the zone module removed, nonseasonal exclusion, metadata filtering and cache configuration/reload behavior. Warm queries allocated 0 B/call. |
| Full Questionable suite after handoff | 9,275 passed, 0 failed, 0 skipped with the outcome guard in place (+44 from the previous Stage 4 checkpoint; +162 from Stage 3). Two existing SixLabors license warnings; no new compiler warnings. |
| Questionable full native metadata harness | 24/24 passed: 5,533 quests scanned, 281 unique mappings, all 14 former-override CFCs covered, historical Pâtisserie `5325:0` maps to CFC `1045`, ordinary dungeons excluded. [Results](<D:/Dev Drive/Questionable/Questionable.Tests/bin/stage4-territory-review/results.log>), [validation data](<D:/Dev Drive/Questionable/Questionable.Tests/bin/stage4-territory-review/validation.json>). |
| Final source verification | All 92 changed/new QST source and `QuestPaths` files and all 2,708 BossMod source/project files match their tested mirrors. [QST hashes](<D:/Dev Drive/Questionable/Questionable.Tests/bin/solo-handoff-qst-hashes.json>), [BossMod hashes](<D:/Dev Drive/Questionable/Questionable.Tests/bin/stage4-bossmod/handoff-source-hashes.json>). |
| Live DLLs and preexisting work | QST live DLL baseline, all three installed VBM DLLs and preexisting T04Zelenia unchanged. [Live handoff check](<D:/Dev Drive/Questionable/Questionable.Tests/bin/stage4-bossmod/live-handoff-check.json>). |
| Native navmesh, UI and gameplay | Deferred to the final level 1 playthrough after the whole overhaul; currently unavailable Pâtisserie is excluded. |

No new dependencies, commits, pushes or live DLL reloads. Existing Zelenia work is untouched. The whole overhaul remains incomplete.

## Crafting verification update

### Ownership slice (before the Stage 3 checkpoint)

Stage 1, change 1 adds a local crafting submission record independent of the task queue. The adapter creates it immediately before the Artisan start action; catalogue queries, missing lists and preflight rejection do not acquire ownership. Exceptions after submission retain uncertain work. Normal completion releases the record only through the owning craft executor after its existing objective checks and stable idle.

Stop, skip and other abandonment retain the record. Observed provider loss requires reconciliation even if Artisan later returns idle. Questionable blocks replacement execution and continues checking status while stopped. The UI allows the user to confirm finished/stopped Artisan work only after fresh, stable idle; confirmation does not restart questing.

This is a guard within the current Questionable instance. It does not cancel Artisan, provide a remote operation/session ID, guarantee detection of an unload/reload between observations, or recover ownership after Questionable itself unloads. These remain documented limitations, not a commitment to expand Artisan's API.

Ownership-slice verification: the isolated `Questionable.Tests` run passed 8,920 tests (14 added for submission ownership and recovery). No dependency, quest-path or companion-plugin changes were made in that slice; nothing was committed or pushed.

### Output verification and grouping (before the Stage 3 checkpoint)

Historical Stage 1 verification, preceding the Stage 3 update on 2026-11-01.

- All ten shipped list-only steps now have local, sequence-scoped inventory targets. Every required item, quantity and quality must be present; submitted work also requires stable Artisan idle. This covers CRP 139, 140, 141, 142, 168, 205 and CUL 2044–2047 without changing their paths or Artisan.
- Requirements were cross-checked against the paths and `LuminaSupplemental.Excel.Generated.QuestRequiredItem.csv`, embedded in the locally cached `LuminaSupplemental.Excel` 4.3.3 assembly (SHA-256 `86F7068F46F9A89C1427A0DCFB0072A8F3CCED0915FA0365323BF8730EB358D2`). This was an inspection source only, not an added dependency or runtime dependency on Artisan's files. CRP 139 uses Artisan's explicit level-5 correction from one shield to three. CUL 2044's tart permits either quality; its tea requires HQ.
- Native quest `RITEMn` IDs now exclude known unsuitable lists before submission. CUL 268's explicit Natron step uses a recipe: its full quest list produces a tart that consumes Natron, so running that list cannot reliably satisfy the intermediate-item check.
- The remaining audit covered all 404 explicit Craft steps across 347 quests. Supplemental data covers 326; the other 78 consist of 25 intermediate CUL crafts, 24 always-collectable deliveries, 20 products incapable of HQ, seven Namazu products and two WVR 590 outputs. Native item-sheet checks confirmed the item categories. Installed Lua (`2026.10.16.0000.0000`) confirms Namazu quests 3100, 3101, 3112, 3120, 3123, 3126 and 3129 require one HQ product; WVR 590 accepts one each of either quality. Namazu checks use `QST_PRODUCT_ITEM` and `NUM_OF_ITEMS_FILTER_HQ`, not the misleading `RITEM` values. WVR uses `NUM_OF_ITEMS_FILTER_NQ_OR_HQ`.
- The local catalogue now records 353 outputs across 313 quests: 344 supplemental entries plus the nine Lua-verified items. Both Craft and its preliminary inventory skip check apply corrected minimum quantity/HQ. This fixes 209 permissive quality checks and two undersized counts (CUL 257: two Grilled Dodo; ALC 648: three HQ Hi-Potions of Strength). Larger explicit quantities and stricter valid requested quality are preserved. Recipe calls use the corrected deficit divided by yield.
- Whole lists now require one contiguous, unambiguous crafting block, complete verified outputs matching native quest metadata, and a final-output trigger. CRP 539 and other multistage/unproven lists use individual recipes. Eligible lists verify every final output, not just the current item. A fully satisfied group also skips later intermediate crafts, avoiding CUL 268/270 recrafting ingredients consumed by the list. Missing-list fallback restores the individual target; uncertain submissions still cannot fall back.
- Additional grouping details: disabled or ambiguous conditional crafts, missing metadata and multiple stages select recipes; ordinary inventory-satisfied skips remain supported. CRP 538 verifies all three final outputs, while CRP 539 has two separate deliveries. Duplicate inventory thresholds merge by maximum, not sum; incompatible strict quality requests reject grouping.
- Unknown custom list-only paths retain the prior compatibility warning. Artisan's recipe API accepts recipe/count, not guaranteed output quality; Questionable checks the resulting quality and stops if unmet. Existing stop/recovery limitations remain documented in the plan.
- Isolated Debug compilation and 9,018 tests passed (63 additional regressions in the final crafting batch); newly introduced compiler/analyser diagnostics were fixed. Coverage includes partial/wrong-quality output, corrected skip conditions, deficits, multiple stages, group completeness and duplicate thresholds. No companion-plugin, dependency or quest-path changes were made. Runtime validation remains pending.

## Purchasing implementation update

Historical Stage 2 record, preceding the Stage 3 update on 2026-11-01: purchasing addresses the original Knightshopper ownership and unbounded-status finding below using its existing API. The original finding tables remain baseline evidence.

- Persistent `PurchaseOperationState` records a local submission token before the provider call and retains the accepted Knightshopper operation ID across task resets. `CancelOwned` targets only the retained ID. Start or cancellation acceptance is not terminal; an abandoned operation's late terminal status releases ownership without continuing the abandoned task.
- Active success also verifies the requested inventory count and quality, allowing a two-second propagation grace. Requests preserve target-inventory-total semantics, and `EmptyList` must verify inventory before completing. A busy manual provider job is refused before movement cleanup; saved lists and settings are untouched.
- Unknown status after ten seconds or cancellation still pending after fifteen seconds stops automatic scheduling but retains the ownership barrier. Unknown or ambiguous submissions require explicit acknowledgement after fresh, continuous `IsBusy=false` for two seconds. Idle cannot override `Running` or cancellation status.
- Start, reset, skip, reload, death and stop gates extend the crafting barrier to purchasing. The controller polls every 250 ms even while stopped. Movement and shop/travel dialogue handlers stand down while purchase ownership remains unresolved. The movement-stop boundary also reads Knightshopper's current busy flag when Stop is pressed to protect manual shopping routes.
- Normal plugin unload attempts owned cancellation, but terminal confirmation cannot continue after unload and ownership is not persisted across Questionable reload. The framework-unload branch still cannot safely invoke IPC.
- The final isolated Debug build and full `Questionable.Tests` suite passed 9,061 tests, including 43 new purchasing cases over the 9,018-test crafting result. There were no new compiler/analyser warnings; only existing SixLabors license warnings remain. The live DLL was unchanged at SHA-256 `91FAA9E4636D0FBBB56E1DD0A797530BF4DB819FBCD865711B4286DBAE487F55`. No companion-plugin, dependency, `QuestPaths` or live-DLL changes were made.
- Purchasing live checks remain deferred to the final playthrough: missing-item purchases to the requested total, preservation of a manual job, stop during travel and at the shop, cancellation refusal and provider loss, late completion after abandonment, insufficient gil and full inventory. At that checkpoint, Stage 3, gathering and fishing, was next.

## Baseline verification

Historical baseline recorded 2026-10-31; the counts and hashes below describe that baseline.

- Corrected a regression in the pending aetheryte refactor: the original no-position branch could explicitly return `false` from the entire skip decision. The extracted helper's `false` instead continued into the busy-character and Rising Stones checks. The helper now distinguishes an explicit decision from continuing evaluation.
- Restored a TODO on the territory-specific route factory. Extracting the executor's skip decisions did not finish the factory's nesting cleanup.
- Reviewed the pending checkbox helper changes; bindings, same-frame dependent controls, save-on-change behavior, and help markers are preserved in the inspected changes.
- Built and tested from `Questionable.Tests/bin/baseline-check`, a separate source copy whose three changed C# files were hash-matched against the working tree.
- `Questionable.Tests`: 8,843 passed, 0 failed. `QuestPathGenerator.Tests`: 1 passed, 0 failed.
- Build diagnostics included a missing SixLabors license warning and generator-test assembly version conflict suggestions. These did not fail the build/tests.
- The loaded development DLL was unchanged: SHA-256 `AA086D605F83DAD5F693CDB7F52472A33ECF0862C3F824DF07430D66E776ECBE` before and after verification.
- These tests do not establish live native-game or cross-plugin behavior. The corrected teleport decision still needs an in-game check, especially for a same-zone step with no position.

## Handoff requirements

Each adapter needs explicit answers to these questions before more work moves out of Questionable:

1. Is the provider loaded, compatible, and able to accept this specific request?
2. Was the start accepted? An attempted IPC call is not acceptance.
3. Which operation did Questionable start, and how long does it own it? Ownership may span multiple internal tasks.
4. Did it succeed, fail, stop, or disappear? Idle alone does not establish success.
5. Does game state confirm the requested result before the quest advances?
6. Can cancellation stop owned work without stopping unrelated user work?
7. Will local cleanup complete even if provider cancellation throws?
8. Which settings were changed, and are they restored on every exit path?

Prefer explicit state in the existing adapters first. A shared helper should emerge from repeated, verified behavior rather than impose an assumed common API on every plugin.

## Travel, duties and combat findings

Priority A means address before extending delegation. Priority B means fix during the relevant integration pass. These are source-level findings, not claims of live reproduction.

| Priority | Finding and evidence | Required behavior / acceptance check |
| --- | --- | --- |
| A | `QuestController.ClearTasksInternal` calls the current executor's `StopNow` before resetting the queue and stopping other controllers. `AutoDutyIpc.Stop` throws on unavailable IPC (`QuestController.cs:830`, `External/AutoDutyIpc.cs:88`). | Unload AutoDuty during an owned run, then stop Questionable. Local queue, automation state, combat and gathering cleanup must complete even when external cancellation fails. |
| A | Questionable's controller disposal unsubscribes events but does not cancel the active AutoDuty executor (`QuestController.cs:1447`, `MiniTaskController.cs:41`). | Cancel owned work before services are disposed; unloading Questionable during a duty run must issue an owned stop without cancelling unrelated work. |
| A | Solo-duty BossMod activation can precede several movement/wait tasks; cleanup is attached to a later waiter (`Steps/Interactions/SinglePlayerDuty.cs:132,410`). | Preserve ownership across the entire sequence. Stopping during an intermediate task must release Questionable's solo-duty preset. |
| A | Wrath setup can call `Stop()` after rejected configuration and still return `true`; exception exits can retain acquired leases (`CombatModules/WrathComboModule.cs:151`). | A rejected start must return failure and release partial acquisitions. |
| A | Wrath lease loss throws, but the combat controller catches it and clears only the target (`WrathComboModule.cs:212`, `CombatController.cs:187`). | Lease revocation must cause explicit recovery or failure, not repeated target acquisition without a working combat provider. |
| A | RSR stop is gated on RSR still being the selected provider (`External/RotationSolverRebornIpc.cs:36`). | Changing preferences during combat must not prevent stopping the provider that Questionable previously started. |
| B | Lifestream's teleport result is ignored and `_teleported` is set anyway; the adapter exposes no cancellation (`Steps/Shared/AethernetShortcut.cs:222`, `External/LifestreamIpc.cs`). | A rejected start must remain distinguishable from accepted travel. Check rejection, timeout, unload and cancellation; confirm destination arrival independently. Verify provider ownership guarantees before using a global abort API. |
| B | Navmesh start errors are swallowed and path-query errors become `false`; movement can complete after its retry without final arrival validation (`External/NavmeshIpc.cs:65`, `Steps/Movement/MoveExecutor.cs:47`). | Reject both movement attempts while far from the destination. Report failure rather than advance. Carry this requirement into SmartNav. |
| B | AutoDuty's configured-content rejection is only applied in Support mode (`Steps/Interactions/Duty.cs:48`). | Blacklist a duty with an available path and an eligible Regular/unsynced mode. It must not be started. |
| B | AutoDuty's entry wait checks territory arrival without confirming that its operation is still alive (`Duty.cs:84`). | A returned `Run` call followed by no entry, provider unload, or provider stop must reach a useful failure rather than wait indefinitely. Allow for legitimate queueing/loading delays. |
| B | BossMod activation ignores `SetActive` rejection; RSR reports start success after swallowing an IPC failure (`External/BossModIpc.cs:84`, `RotationSolverRebornIpc.cs:28`). | Rejected activation must prevent the quest from treating combat as successfully delegated. |
| B | BossMod changes `ClearPresetOnCombatEnd` without restoring it and can overwrite its restoration flag on repeated activation; AutoDuty changes mode/unsynced settings without recording previous values (`BossModIpc.cs:136`, `AutoDutyIpc.cs:75`). | Snapshot once per owned operation; verify settings after normal completion, failure, cancellation, repeated activation and unload. Confirm each provider's own restore behavior before implementing overlapping restoration. |

## Navigation verification (2026-11-01)

Vnav IPC review and implementation (2026-11-01): added optional movement-allowed, SimpleMove-pending, pathfinding-in-progress and queued-pathfinding reads, plus nearest-reachable mesh queries. Updates cache optional status for 250 ms; dispatch checks are fresh. A disabled follower is diagnosed without enabling it; pending SimpleMove prevents competing QST dispatch and global stop. Ground queries run only when capturing a failure and do not rewrite quest coordinates. Removed the duplicate preflight takeoff jump and its floor scan. Retained local stuck recovery (vnav StopOnStuck defaults off), mounting, landing/dismount, quest jumps and route overrides pending equivalent coverage. SimpleMove retains typed traversal waypoints, but its pending result can dispatch after Path.Stop; this prevents wholesale migration until cancellation is resolved. Evidence: installed vnavmesh 1.2.3.14 and upstream commit `6fc80725eb8290472eee433fc4be7ee06ec79357` (`IPCProvider`, `AsyncMoveRequest`, `FollowPath`, `NavmeshQuery`). Isolated validation: 9,407 passed (34 new), 0 failed, 0 skipped; two existing SixLabors warnings, no new warnings. In-game landing/takeoff parity remains to be verified.

Navigation follow-up (2026-11-01): batches 1 and 2 address the navmesh start/status/false-completion findings above. Original pathfinding faults are retained, the 30-second deadline is enforced independently of provider cancellation, and late results cannot dispatch movement. Unknown movement status fails an active request without per-frame warning spam. Short routes are validated, the existing distance/territory rules are preserved, and manual purchasing/gathering/duty activity blocks QST submission and delayed dispatch. Isolated validation: 9,358 passed (49 added in batch 2), 0 failed, 0 skipped. This does not establish in-game route correctness or complete the SmartNav migration; SmartNav has no available source or IPC contract yet. Batch 3 completes last-failure troubleshooting capture: the existing Stuck? clipboard report includes a frozen LastNavigationFailure record with failure-time quest/task and movement context. Stops, retries and later player movement do not rewrite it; ordinary cancellation does not create a failure. The isolated suite passed 9,373 tests, including 15 new capture/report cases. Reproduced arrival problems remain outstanding; no live plugin deployment or reload was performed.

## Dialogue, tweaks and diagnostics findings

Original static findings from 2026-10-31; recheck against current source before treating any as unresolved. Working dialog handlers remain outside the current cleanup scope.

| Priority | Finding and evidence | Required behavior / acceptance check |
| --- | --- | --- |
| B | YesAlready initializes `_wasEnabled` to the startup state, then requires it to be false before disabling YesAlready (`External/YesAlreadyIpc.cs:38,68`). | Separate initial state from “disabled by Questionable.” Start with YesAlready enabled, run overworld automation, stop; suppress the conflict during the run and restore only Questionable's own changes. |
| B | TextAdvance calls IPC from the framework update without unload handling; its external configuration is sent only on acquisition (`External/TextAdvanceIpc.cs:46`). | Handle provider disappearance and update owned settings when dialogue/cutscene preservation changes during automation. |
| B | Pandora marks acquisition attempted before successful feature queries and discards failed restoration obligations (`External/PandorasBoxIpc.cs:130,172`). | Retry acquisition after provider return; retain failed restores until resolved, with explicit handling when the provider never returns. |
| C | NotificationMaster identifies itself as vnavmesh; AutoDuty readiness requires it to be running; installed-plugin lookup compares display names with supplied internal names (`NotificationMasterIpc.cs:9`, `AutoDutyIpc.cs:17`, `IPCUtils.cs:11`). | Separate identity, availability, busy status and acceptance. Verify installed/enabled/idle/running/missing cases. The NotificationMaster identity issue does not by itself prove notification delivery is broken. |

YesAlready is used to suppress conflicting automation and restore it afterward. It is not Questionable's dialogue execution provider. TextAdvance advances dialogue/cutscenes; Questionable retains quest-specific choice handling. Automaton's inspected role is a capability check for sniping and an explicit user configuration action; no temporary owned-setting restoration was established for that path.

## Migration boundaries

- Questionable owns quest intent, prerequisites, task sequence and progress checks.
- SmartNav is the intended owner of map topology and map-link traversal. Agree on route selection and execution ownership with Lifestream before replacing the existing routes. Story-dependent entrances remain until equivalent coverage is demonstrated.
- Lifestream already handles aethernet execution. Fix acceptance and cancellation handling before extending its role to normal aetheryte teleports; compare Return, unlock, cooldown and interrupted-cast behavior first.
- Keep provider-specific quest data translation where necessary. Replacing every game API call with an IPC is not a useful migration criterion on its own.
- Keep inventory/quest outcome checks in Questionable even when a provider reports success.

## Next implementation batches

Historical implementation order from the 2026-10-31 audit; the migration stages in the plan supersede this sequence.

1. Make cancellation and disposal reliable across owned operations; add focused failure-path tests for local cleanup when an external stop throws. Preserve ownership across multi-task duties.
2. Correct start acceptance and provider-loss handling, including Wrath, RSR, Lifestream and movement. Test rejection, unload, cancellation and an unchanged destination/objective.
3. Correct settings ownership/restoration and readiness diagnostics. Test initial enabled/disabled state and provider changes during a run.
4. Apply the Artisan-specific findings below before expanding crafting delegation. Acceptance and outcome validation belong in batches 1–2; a new quest-specific resolver is a separate companion-API change.
5. Migrate normal teleport execution and map-link routing only after their contracts are established; then remove superseded workarounds and resume the nesting cleanup on retained code.

The initial audit pass changed only the pending aetheryte refactor. The first implementation batch below follows that baseline; no companion plugin has been modified.

## First implementation batch

Historical first-batch record, preceding the Stage 3 update on 2026-11-01. Later stage updates above supersede the limitations recorded here.

- Task abandonment now cancels the current stoppable executor on stop/reset, skip, retry and interruption. Failed startup rolls back partial acquisition, while a skipped start does not cancel unacquired work. The executor is detached before external cleanup, and a failed cancellation stops the local queue instead of starting replacement work.
- Controller cleanup attempts movement, combat, gathering, AutoDuty and solo-duty cleanup independently. Plugin unload runs cleanup on the framework thread before container disposal. Successful task completion remains separate from cancellation.
- AutoDuty ownership spans its start and wait tasks, so stopping between them still reaches the provider. Unowned runs are not stopped; confirmed provider disappearance invalidates stale ownership. AutoDuty start/wait tasks ignore unrelated non-combat interruption toasts, which otherwise would stop a run and replay only its waiter.
- Solo-duty cleanup is guarded by acquisition state, preserves the original quest-battle setting across repeated activation, and attempts configuration restoration even when preset cleanup throws.
- Fishing startup now records its original enabled setting once, rolls back before the first cast if preset generation fails, and avoids deleting presets when creation was never attempted. Scoped preset deletion, prior-selection restoration and acknowledged fishing cancellation remain unresolved provider-contract work.
- Artisan fallback now requires a successful catalogue query confirming the list is missing. Busy, paused, unavailable and failed submissions stop the task instead. Only the verified class-quest ID mapping remains; delivery groups are not guessed.
- Craft objectives and quality counts are initialized before either submission path. For steps with an item target or completion flags, completion requires the requested inventory or explicit quest progress plus stable provider idle. Missing startup and provider loss have bounded waits; stopped work with insufficient output fails. Recipe fallback accounts for yield and includes CUL.
- Compatibility inspection found 414 craft steps: 404 have item targets; ten legacy steps have neither targets nor completion flags (CRP quests 139, 140, 141, 142, 168, 205; CUL quests 2044–2047). These retain list-only support, requiring observed active crafting followed by stable idle instead of treating initial idle as success. Their outputs still cannot be independently verified. Artisan uses supplemental quest-item data for its lists and exposes only names through the inspected catalogue API; no guessed targets, new dependency or quest-data edits were introduced.
- Artisan still has no scoped terminal cancellation. Stop/skip/unload can leave submitted Artisan work running; `SetStopRequest` was deliberately not used as a substitute because it pauses lists. Inspect/cancel that work in Artisan before restarting. Companion API work is required to close this gap.

Regression coverage includes task cancellation/rollback, Artisan rejection and delayed startup, provider loss, quality/count validation, recipe yield, and fishing startup restoration. Tests run only in the isolated source mirror; in-game integration checks remain necessary. The original finding tables below are the baseline evidence, with the bullets above recording what this batch addresses.

Final verification: `Questionable.Tests` passed 8,906 tests with no failures (63 more than the baseline). All 24 changed/new C# files, including the earlier refactors, hash-match the tested mirror. The development DLL retains the baseline SHA-256 above. The existing SixLabors license warning remains; the newly introduced file-name analyzer warnings were resolved. No quest data or companion plugin files changed, and no commits or pushes were made.

## Artisan: built-in quest lists and correct usage

Original contract inspection and findings from 2026-10-31. The crafting updates above supersede addressed defects; the proposed richer API below is not required for the current crafting scope.

Questionable already attempts a built-in class-quest list before falling back to a recipe (`Steps/Shared/Craft.cs:52`, `External/ArtisanIpc.cs:56`). The normal class-quest mapping, short quest ID plus 65536, matches Artisan's full quest row IDs.

Evidence was checked against local Artisan branch `IPC`, commit `8cb22b2fea53a69cbadebf48e08eec017e7d0eec`. The installed `Artisan/4.0.5.21/Artisan.dll` identifies commit `e4380555cac967ec47f976f1b23595a1efbbde73`; the relevant list mapping and IPC contracts match that commit. This verifies the installed file's contract, not which binary is currently loaded in Dalamud.

`Artisan.GetLists` returns built-in lists as well as user lists, and `Artisan.StartListById` can start them (`D:/Dev Drive/Artisan/Artisan/IPC/IPC.cs:551,567`). Artisan's separate Quest Helper is a UI recipe-selection facility, without a matching quest-helper IPC in the inspected registration. Both list crafting and single-recipe endurance can switch jobs. Lists include subcrafts but can skip recipes whose materials are missing, so a finished list is not proof that a quest's outputs were produced.

| Priority | Finding and evidence | Required behavior / acceptance check |
| --- | --- | --- |
| A | `ArtisanIpc.CraftList` maps any failure to `false`, and `Craft.Start` treats it as grounds for recipe fallback. The provider rejects lists while another run is active, but its `CraftItem` path can modify global endurance recipe/count state (`ArtisanIpc.cs:36`, `Craft.cs:52`; provider `IPC.cs:193,567`). | Distinguish missing list, busy, unavailable and accepted. Only a confirmed missing list may trigger fallback. Starting a quest craft during manual Artisan activity must preserve that manual run. |
| A | A successful list start returns before `_itemQuality` and initial inventory counts are set. No-item tasks treat inactive Artisan as completion; item-target tasks can wait indefinitely when Artisan stops with insufficient output (`Craft.cs:46,124`). | Initialize expectations before either start path. Verify count and quality, or explicit quest progress. Test HQ requested/NQ produced, insufficient materials, queued startup, and provider unload. |
| A | `IsCrafting` checks endurance/list flags and maps IPC loss to false, excluding queued startup/cleanup covered by the provider's broader `IsBusy` (`ArtisanIpc.cs:58`; provider `IPC.cs:215`). | Distinguish unavailable, starting, running and finished. Preserve a bounded startup period and report an unmet objective when owned execution ends. |
| B | The alternate ID concatenates a full quest ID with a job slot. Artisan delivery lists use `800000 + RecipeNotebookList.RowId` and can contain multiple quest recipes (`Craft.cs:53`; provider `CraftingList/PremadeLists.cs:105`). | Remove the guessed mapping. Prefer a quest/job-specific resolver or start API in Artisan. Do not blindly start a whole delivery group for one quest. Check class quests and Crystarium, Studium and Wachu deliveries. |
| B | Recipe fallback passes missing item count as synthesis count; one fallback array duplicates WVR and omits CUL (`Craft.cs:97,114`; provider `Autocraft/EnduranceCraftWatcher.cs:93`). | Ceiling-divide missing quantity by recipe yield; include CUL. Five items from a yield-three recipe should request two crafts. |
| A | Craft has no owned cancellation path. Artisan's `SetStopRequest(true)` pauses lists, and clearing it can resume the paused list (`Craft.cs:39`; provider `Artisan.cs:443`). | Do not model pause as terminal cancellation. Define scoped cancellation and acknowledgment before allowing a new operation to take over. Test stop, skip, interruption, plugin unload and restart. |

A useful future Artisan contract would resolve a quest/job request to its exact required outputs and subcrafts, return accepted/busy/unsupported/error explicitly, provide an operation identifier/status, and cancel only that operation. Required materials and output-quality information should be exposed if Questionable is to coordinate gathering or shopping. The current inspected IPC does not expose that complete contract. This is a proposal, not an API that already exists.

## Gathering, purchasing, fishing, gear and mail

Original static findings from 2026-10-31, with the dated Gather correction retained. Later implementation records supersede addressed defects; fishing remains parked and Mogmail remains disabled.

| Priority | Finding and evidence | Required behavior / acceptance check |
| --- | --- | --- |
| A | Skip/interruption can discard executors without stopping them; failed startup can mutate a provider before the executor becomes current (`QuestController.cs:1232`, `Steps/TaskQueue.cs:43`, `MiniTaskController.cs:54`). | Route task abandonment through idempotent owned cleanup; roll back partial startup even when `Start()` throws. Exercise stop, skip, interruption and unload, not just normal task completion. |
| A | Knightshopper status errors become `Unknown` and are polled forever. Cancel's acceptance is discarded and the executor is cleared before cancellation acknowledgment (`External/KnightshopperIpc.cs:80`, `Steps/Shared/Purchase.cs:63`; installed `Knightshopper/1.0.1.9/docs/IPC.md:188`). | Bound unknown/unavailable handling and retain the operation ID until cancellation completes. Test unload during travel and cancellation immediately followed by movement. |
| A | AutoHook preset/start results are ignored. Failed preset generation after enabling the plugin misses cleanup; provider disappearance can leave endless retries (`Steps/Shared/Fish.cs:75,110`; provider `D:/Dev Drive/AutoHook/AutoHook/Services/AutoHookIPC.cs:60`). | Roll back each partial startup mutation; establish preset/start acceptance and explicit unavailable handling. Test invalid preset and throwing preset generation. |
| A | AutoHook cleanup deletes all anonymous presets, does not restore the prior selection, and treats a single Quit attempt as completed cleanup (`Fish.cs:166`; provider `AutoHookIPC.cs:160`). | Track and remove only Questionable's preset, restore prior selection, confirm fishing has stopped. Preserve another caller's anonymous preset; test cancellation during a hook animation. This needs provider support for scoped ownership. |
| A | The original GatherChill provider modified/persisted the user's saved gathering entry and retained its mode/larger amount, allowing Keep or large Gather entries to conflict with quest requests (provider `D:/Dev Drive/Gather-Chill/GatherChill/IPC/GatherChillProvider.cs:59`, `Utilities/GatheringHelpers/GatherList_Util.cs:50`, `Scheduler/Tasks/Task_Start.cs:102`). Correction (2026-11-01): existing API 2 supports collectibles; the earlier claim that a collectability API change was confirmed necessary was overstated. | The applied five-file internal fix uses a transient request list/routes and preserves saved entries, routes and user edits without changing the API or solver. Questionable requests missing qualifying quantity and verifies final quality/quantity and stable idle. No new collectability field or API version is justified; no remote ownership guarantee is claimed. |
| B | Stylist update errors are swallowed while the executor records a triggered request; unload maps to permanently busy. Missing Stylist permanently saves the user's preference as Vanilla (`External/StylistIpc.cs:18`, `Steps/Interactions/EquipRecommended.cs:76,118`). | Propagate rejected starts, distinguish unavailable from busy, verify completion, and use a temporary fallback that preserves preference. Verify delayed-start timing against the provider. |
| B before enablement | Mogmail's task registration is commented out. If enabled, the executor ignores claim acceptance, immediately closes mail, repeats claims while busy and never completes. It retains a game object across movement before accessing its address (`ServiceCollectionExtensions.cs:62`, `Steps/Common/Mail.cs:39`, `External/MogmailIpc.cs:18`). | Keep it disabled until open/ready, accepted claim, completion and close have distinct states; re-resolve the moogle at interaction time. Test empty/full mail, full inventory, rejection, unload and despawn. No crash was reproduced. |

Two existing foundations should be preserved: GatherChill returns start acceptance and Questionable verifies inventory afterward; Knightshopper provides an operation ID and terminal states. The gathering remaining-quantity argument and Knightshopper target-inventory-total argument match their respective inspected contracts.

The earlier high-level overview overstated mail support: Mogmail scaffolding exists, but its task is not currently registered. It also described YesAlready too broadly; its actual role is conflict suppression, as corrected above.

## Original rollout decisions (2026-10-31; scope updated 2026-11-01)

Retained from the former plan for history, not a new work queue. The original stage order was: establish the baseline; prove crafting; reuse the lifecycle for purchasing; gathering/fishing; duties/combat; travel; simplify retained code. Crafting supplied the first concrete operation state; purchasing supplied the second use case before extracting shared lifecycle behavior. The existing task pipeline was to remain, with no generic provider framework or new dependency.

The first reviewable crafting slice was split into three parts: retain submission ownership independently of the queue and keep recovery polling alive while stopped; improve list selection and verified outputs using current IPC and recipe fallback; validate normal/interrupted execution in isolation, then exercise native behavior during the final playthrough. The first ownership slice intentionally left quest paths, travel, profiles and unrelated integrations intact. Later crafting verification completed the ten shipped list-only requirements; they are no longer an outstanding implementation task.

Normal crafting completion was defined as correct list or recipe selection, all required outputs verified and stable Artisan idle. Interrupted/uncertain work retained the local guard and recovery guidance. Remote IDs, acknowledged cancellation and caller-loss cleanup were explicitly not prerequisites; companion changes required a concrete behavior gap. Material procurement remained outside this pilot.

Later decisions supersede the original stage gates: Feesh and SmartNav remain parked pending real contracts; SimpleMove migration awaits safe pending-request cancellation; BossMod's capability gate and support files were removed in favor of existing preset IPC. The former stage-4 gate cited the 9,275-test suite, native metadata/encounter checks, 38/38 cached capability checks, Boss Release and matching hashes. Those are historical checkpoint evidence, not validation of the later direct preset change. The [current completion gates](OVERHAUL-PLAN.md#completion-gates) and [pending live validation](OVERHAUL-PLAN.md#pending-live-validation) replace the old future-tense implementation instructions.
