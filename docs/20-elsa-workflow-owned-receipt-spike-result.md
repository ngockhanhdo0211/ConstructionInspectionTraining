# Elsa Workflow-Owned Receipt Atomicity Spike Result

## 1. Executive verdict

**ADR-006 candidate: Rejected/NO-GO for Elsa 3.9.0.** Elsa 3.9.0 can persist workflow properties and bookmarks, and its distributed workflow-instance lock serialized the tested concurrent stimuli. However, the public Runtime path did not provide the required failure semantics for a workflow-owned receipt:

- a fault after assigning receipt/state properties but before activity completion left the new receipt and shadow state durable;
- faults before the state assignment left the previous receipt and bookmark visible, but the incidented activity did not safely process a resend;
- therefore receipt, shadow transition, bookmark consumption/recreation, and activity success were not proven to be one rollback-safe atomic unit.

The candidate does demonstrate exactly-once logical effect for the tested happy-path duplicate, restart, and concurrency cases. That is not exactly-once delivery and does not overcome the fault-window evidence above. E1 production source remains **NO-GO**. E2 controlled orchestration remains **NO-GO**. This is a project-specific conclusion about the tested public API and behavior of Elsa 3.9.0, not a claim that Elsa can never be used in another architecture or version.

## 2. Scope and safety

The checkpoint changed only:

- `backend/spikes/ConstructionInspection.ElsaPersistenceSpike/`;
- this result document.

The spike stayed outside the production solution. It did not change production source, packages, lockfiles, migrations, frontend, or database schema. The only writable database was `ConstructionInspectionElsaSpikeDb`, protected by the existing exact-name guard. No automatic migration or database reset was used.

The event envelope contains only:

- `PayloadVersion = 1`;
- `DomainEventId`;
- `InspectionId` as the aggregate ID;
- monotonic `AggregateSequence`;
- one of the nine allowlisted event types;
- server-generated canonical SHA-256 `PayloadHash`;
- `OccurredAtUtc`.

It contains no JWT, secret, credential, `DataJson`, Form Schema, file content, or business comment.

## 3. Terms and boundaries

| Term | Meaning in this spike |
|---|---|
| At-least-once delivery | A transport may send the same domain event more than once. |
| Exactly-once logical effect | An exact duplicate does not advance aggregate sequence or shadow state twice. |
| Durable receipt | `LastAppliedSequence`, `LastDomainEventId`, and `LastPayloadHash` stored in workflow state. |
| Runtime transaction | Elsa's supported workflow commit transaction configured by `UseWorkflowCommitTransaction = true`. |
| External marker | The previous spike's custom inbox/processed row; it is not authoritative in this candidate. |
| Crash window | A point where process/fault/cancellation can occur between validation, in-memory mutation, bookmark handling, and durable completion. |

No distributed transaction between ConstructionInspection and Elsa databases is claimed. No custom inbox `DbContext`, SQL application lock, reflection, internal Elsa API, package patch, or manual write to Elsa tables participates in the candidate decision.

## 4. Candidate architecture

The workflow owns the authoritative receipt and shadow state:

```text
at-least-once domain event
        |
        v
Elsa generic intake bookmark
        |
        v
workflow callback
  - validate envelope/hash/allowlist
  - compare aggregate sequence and durable receipt
  - validate transition
  - update receipt and shadow state
  - recreate intake bookmark
        |
        v
Elsa Runtime persistence commit
```

The workflow persists:

- `LastAppliedSequence`;
- `LastDomainEventId`;
- `LastPayloadHash`;
- `CurrentShadowState`.

A single generic, auto-burn bookmark remains available in terminal states so an exact terminal duplicate can still be classified as a no-op. This is a spike choice, not an approved production design.

All sequence and transition checks execute inside the workflow callback. The host reads before/after snapshots only to report the observed disposition; it does not authorize a transition through an external precheck.

## 5. Authoritative rules tested

| Input | Workflow behavior |
|---|---|
| First event is sequence 1 `Inspection.Created` | Creates Draft receipt/state and intake bookmark. |
| Sequence equals last + 1 and transition is valid | Applies receipt and next shadow state. |
| Same last sequence, same event ID and hash | Duplicate no-op. |
| Same last sequence, different event ID or hash | Conflict; no mutation. |
| Sequence below last | Obsolete; no mutation. |
| Sequence above last + 1 | Gap; no mutation. |
| Invalid transition | Reject; no mutation. |
| Exact duplicate after terminal state | Duplicate no-op. |
| Different event after terminal state | Terminal conflict; no mutation. |

The event allowlist remains exactly:

1. `Inspection.Created`
2. `Inspection.Submitted`
3. `Inspection.StartReview`
4. `Inspection.Approved`
5. `Inspection.Rejected`
6. `Inspection.RecheckRequested`
7. `Inspection.RevisionCreated`
8. `Inspection.Resubmitted`
9. `Inspection.ReviewerReassigned`

## 6. Public API and Runtime evidence

The implementation uses public Elsa 3.9.0 surfaces only:

- `ActivityExecutionContext.CreateBookmark(CreateBookmarkArgs)`;
- `WorkflowExecutionContext.GetProperty<T>` and `SetProperty<T>`;
- `IStimulusSender.SendAsync`;
- `IWorkflowRuntime` client create/run/export state;
- EF Core Management and Runtime persistence;
- `EFCoreWorkflowRuntimePersistenceFeature.UseWorkflowCommitTransaction`.

Elsa's official documentation describes a bookmark as a persistence point and states that the workflow resumer uses a distributed workflow-instance lock. It also advises that resume handlers be idempotent. Those statements support the happy-path persistence and serialization model, but they do not promise that arbitrary activity property mutations are rolled back when the activity becomes incidented.

Primary references reviewed:

- <https://docs.elsaworkflows.io/extensibility/custom-activities>
- <https://docs.elsaworkflows.io/guides/architecture/workflow-dispatcher>
- <https://docs.elsaworkflows.io/guides/api-client>

`IStimulusSender.SendAsync` returned only after the tested workflow execution path had persisted observable state. A simulated lost acknowledgement was represented by discarding that successful return and resending after host restart; the resend was an exact duplicate no-op.

## 7. Workflow and restart matrix

| Case | Result after restart |
|---|---|
| Created -> Submitted -> StartReview -> Approved | Receipt sequence 4 and Approved remained durable. |
| Created -> Submitted -> StartReview -> Rejected | Rejected is supported by the transition table and concurrency terminal test. |
| Recheck -> RevisionCreated -> Resubmitted -> StartReview -> Approved | Every receipt/state survived a host restart between events. |
| Reviewer reassignment in Submitted/UnderReview | Accepted only at the next sequence and serialized with competing stimuli. |
| Duplicate after restart | Exact duplicate no-op. |
| Duplicate at terminal state | Exact duplicate no-op; terminal receipt/state unchanged. |
| Different terminal event | Terminal conflict; no second terminal outcome. |

The full restart flow reached sequence 9 Approved with one intake bookmark and the matching receipt ID/hash after each process restart.

## 8. Concurrency evidence

Two independent hosts connected to the same spike database and sent stimuli against the same workflow instance.

| Race | Observed result |
|---|---|
| Same event ID and sequence | One durable receipt/effect; both callers can observe the same committed event as applied because `IStimulusSender` has no per-event commit token. |
| Different event IDs at same sequence | One receipt won; the other event did not overwrite it and was classified from durable state as conflict. |
| N and N+1 concurrently | Runtime serialization prevented corruption; if N+1 arrived first it was a gap/no-op and could be resent after N. |
| Duplicate after restart | No sequence or state advance. |
| Duplicate terminal event | No sequence or state advance. |
| Reassign vs start review | At most one receipt occupied the sequence; the other could not overwrite it. |
| Approve vs reject | Exactly one terminal receipt/state became durable; no dual terminal state occurred. |

These observations support serialized logical effects in the tested matrix. They do not prove exactly-once message delivery or provide a durable per-delivery acknowledgement identity.

## 9. Fault and cancellation evidence

The decisive results are:

| Injection point | Receipt/state after restart | Bookmark/incident behavior | Verdict |
|---|---|---|---|
| Before receipt | Previous receipt/state remained | Intake bookmark remained, activity incident persisted, resend did not safely apply | FAIL |
| After validation, before state | Previous receipt/state remained | Intake bookmark remained, activity incident persisted, resend did not safely apply | FAIL |
| After receipt variables, before activity completion | New receipt sequence and new shadow state were durable | Incident persisted | **FAIL: non-atomic receipt** |
| Cancellation before Runtime work | No workflow instance or receipt created | No listener leaked | PASS for this narrow point |
| Lost acknowledgement after commit, then resend | Committed receipt survived restart | Resend was duplicate no-op | PASS |

No SQL service kill, manual Elsa-table update, retry loop, timeout increase, or exception suppression was used.

The requested rollback guarantee is disproved: activity failure did not restore one coherent pre-event state in every injection window. In particular, the after-receipt fault made the event appear durably applied even though activity completion failed.

## 10. ADR-006 decision

ADR-006 is **Rejected/NO-GO for Elsa 3.9.0**. It must not be changed to Accepted from this spike.

The redesign would have been eligible for acceptance only if all of these were demonstrated with supported APIs:

1. receipt, state, and bookmark commit or roll back together;
2. every precommit fault leaves a resumable original bookmark;
3. resend after any precommit failure can safely apply once;
4. concurrent hosts cannot produce conflicting receipts or terminal states;
5. restart preserves the same guarantees.

Items 1-3 failed. Consequently:

- do not implement E1 production dispatcher;
- do not treat an Elsa workflow variable as a transactionally safe inbox receipt;
- do not claim exactly-once delivery;
- do not start E2 authoritative workflow migration.

## 11. Reconsideration boundary

The decision may be reconsidered only when at least one of these conditions is met:

1. a newer Elsa version or public API has a documented atomic contract for receipt, state, bookmark, and failure rollback, then passes the same repeatable fault/concurrency tests; or
2. the project architecture requirement changes and management explicitly approves the weaker semantics and resulting operational risk.

Until then there is no further Elsa production implementation checkpoint.

## 12. Verification summary

Spike verification:

- build: passed, 0 warnings and 0 errors;
- tests, run 1: 20/20 passed, 0 failed, 0 skipped;
- tests, run 2: 20/20 passed, 0 failed, 0 skipped;
- runtime evidence includes happy path, restart, duplicate, sequence conflict, two-host concurrency, fault diagnostics, cancellation, and lost acknowledgement;
- diagnostic tests intentionally assert the unsafe fault behavior so future package changes reveal any change in semantics.

Repository and production regression results are recorded in the checkpoint report. No test result in this document converts the NO-GO finding into production approval.

Production regression:

- solution build: passed, 0 warnings and 0 errors;
- Domain unit tests: 82/82 passed;
- Application unit tests: 124/124 passed;
- Integration tests: 36/36 passed against `ConstructionInspectionIntegrationTestDb`;
- production formatter: passed;
- NuGet audit: no vulnerable packages;
- EF pending-model check: no changes since the last migration;
- migration history ends at applied `20261020133408_AddChecklists`;
- no production migration was created or applied.

## 13. Final status

| Scope | Status |
|---|---|
| Elsa 3.9.0 SQL persistence compatibility | GO, inherited from E0/E1 persistence spike |
| Happy-path workflow-owned receipt | GO for limited evidence |
| Duplicate/restart logical idempotency | GO for tested cases |
| Two-host serialization | GO for tested cases |
| Fault rollback atomicity | NO-GO |
| ADR-006 acceptance | NO-GO |
| E1 production source | NO-GO |
| E2 authoritative workflow | NO-GO |

Production remains on the existing `BusinessProcess` and append-only `ProcessHistory` workflow. The project will not add Elsa packages to the production solution, deploy an Elsa production database, add a transactional outbox solely for Elsa, or expose Elsa Studio, Identity, or generic Elsa APIs. E0/E1 spike projects and documents remain only as technical evidence.

The checkpoint is ready to be closed by a local documentation/evidence commit; it does not authorize E1 or E2 implementation.
