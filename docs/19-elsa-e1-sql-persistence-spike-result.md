# Elsa E1 SQL Persistence Compatibility Spike Result

> **Final follow-up:** Candidate workflow-owned receipt ở mục 11 đã được triển khai và kiểm chứng tại `docs/20-elsa-workflow-owned-receipt-spike-result.md`. Candidate thất bại atomic rollback gate: fault sau property mutation vẫn để receipt/shadow state durable, còn fault sớm tạo incident khiến resend không áp dụng an toàn. Candidate này không còn được đề xuất tiếp tục. Docs/20 là quyết định cuối cho ADR-006 và rollout Elsa.

## 1. Kết luận

Checkpoint này đánh giá Elsa `3.9.0` và `Elsa.Persistence.EFCore.SqlServer` `3.9.0` trong project cô lập. Không có source production, solution production, migration nghiệp vụ hoặc database nghiệp vụ nào bị thay đổi.

Kết quả tổng hợp:

- Durable workflow instance, bookmark, correlation và terminal state tồn tại sau khi dừng host và khởi tạo host/process mới.
- Chín Inspection event trong contract E1 hoạt động với code-first workflow.
- Unique `DomainEventId`, SQL application lock, inbox state và reconciliation tạo một logical effect trong các trường hợp duplicate/concurrent/crash đã thử nghiệm.
- Elsa hỗ trợ transaction cho workflow commit của chính Runtime persistence qua `UseWorkflowCommitTransaction`.
- Không tìm thấy public API được hỗ trợ để enlist custom inbox `DbContext` và Elsa start/resume/state commit vào cùng một local SQL transaction.
- Cả Management persistence và Runtime persistence đều cần cho cách host code-first đã thử nghiệm.
- **E1 production source: NO-GO.** ADR-006 chưa đạt atomic single-effect theo contract docs/18. Kết quả logical reconciliation của spike không được dùng để tuyên bố exactly-once delivery.
- E2 tiếp tục NO-GO và không nằm trong checkpoint này.

## 2. Phạm vi và safety

Spike chỉ tạo:

- `backend/spikes/ConstructionInspection.ElsaPersistenceSpike/`
- `docs/19-elsa-e1-sql-persistence-spike-result.md`

Database duy nhất được ghi là `ConstructionInspectionElsaSpikeDb`. Mọi đường vào database đều parse connection string và fail-closed nếu `Initial Catalog` không khớp chính xác tên này hoặc còn placeholder chưa resolve. Connection string được dẫn xuất trong process từ User Secrets, không được in hoặc lưu vào file, và environment variable tạm được xóa trong `finally`.

Không sử dụng:

- `ConstructionInspectionDb` cho spike write;
- `ConstructionInspectionIntegrationTestDb` cho spike write;
- startup auto-migration;
- Studio, HTTP activities, JavaScript, Python, User Tasks hoặc generic Elsa API;
- JWT, `DataJson`, Form Schema, comment nghiệp vụ hoặc secret trong event payload/log.

Host là Generic Host, không cấu hình Kestrel hoặc TCP listener. Schema chỉ được apply khi chạy command `migrate` rõ ràng.

## 3. Dependency result

### 3.1 Direct packages

| Package | Requested | Resolved | Vai trò |
|---|---:|---:|---|
| `Elsa` | 3.9.0 | 3.9.0 | Code-first workflow/runtime registration |
| `Elsa.Persistence.EFCore.SqlServer` | 3.9.0 | 3.9.0 | SQL Server Management/Runtime persistence |
| `Microsoft.EntityFrameworkCore.SqlServer` | 10.0.12 | 10.0.12 | Patch override tương thích với production; resolve SQL Client 6.1.6 |
| `System.Security.Cryptography.Pkcs` | 10.0.13 | 10.0.13 | Security patch override; loại advisory từ transitive 9.0.4 |

Test project dùng cùng baseline test packages hiện hành: `Microsoft.NET.Test.Sdk` 17.14.1, xUnit 2.9.3, runner 3.1.4 và coverlet 6.0.4.

### 3.2 Elsa dependency tree

Các Elsa package sau đều resolve đúng `3.9.0`, không có mixed Elsa version:

- `Elsa.Alterations`, `Elsa.Alterations.Core`
- `Elsa.Api.Common`
- `Elsa.Caching`, `Elsa.Common`, `Elsa.Expressions`, `Elsa.Features`
- `Elsa.Identity`, `Elsa.KeyValues`, `Elsa.Labels`, `Elsa.Mediator`, `Elsa.Tenants`
- `Elsa.Persistence.EFCore`, `Elsa.Persistence.EFCore.Common`, `Elsa.Persistence.EFCore.SqlServer`
- `Elsa.Workflows.Core`, `Elsa.Workflows.Management`, `Elsa.Workflows.Runtime`

`Elsa.Identity` xuất hiện transitively từ meta-package `Elsa`; spike không đăng ký hay expose Identity endpoint. Đây là footprint cần tối giản lại nếu production E1 được phê duyệt.

Persistence path thực tế:

```text
Elsa 3.9.0
├── Elsa.Workflows.Management 3.9.0
├── Elsa.Workflows.Runtime 3.9.0
└── các Elsa feature packages 3.9.0

Elsa.Persistence.EFCore.SqlServer 3.9.0
├── Elsa.Persistence.EFCore 3.9.0
│   ├── Elsa.Persistence.EFCore.Common 3.9.0
│   ├── Elsa.Workflows.Management 3.9.0
│   └── Elsa.Workflows.Runtime 3.9.0
└── Microsoft.EntityFrameworkCore.SqlServer 10.0.12 (direct patch override)
    └── Microsoft.Data.SqlClient 6.1.6
```

Provider package có native `net10.0` asset. NuGet metadata khai báo license MIT và repository `elsa-workflows/elsa-core`. Restore/build không có downgrade hoặc version conflict. Audit cuối: không có vulnerable hoặc deprecated package.

Lần restore đầu với provider minimum EF Core `10.0.9` resolve `Microsoft.Data.SqlClient` 6.1.1 và transitive Pkcs 9.0.4 có High/Moderate advisory. Hai direct patch override nêu trên đồng bộ với production baseline và làm audit sạch; không hạ version hoặc dùng prerelease.

## 4. Public API và tooling evidence

Package public declarations xác nhận:

- `ManagementElsaDbContext.WorkflowDefinitions` và `.WorkflowInstances`.
- `RuntimeElsaDbContext.WorkflowInboxMessages`, `.Triggers`, `.WorkflowExecutionLogRecords`, `.ActivityExecutionRecords`, `.Bookmarks`, `.BookmarkQueueItems`, `.BookmarkQueueDeadLetterItems` và `.KeyValuePairs`.
- `EFCoreWorkflowRuntimePersistenceFeature.UseWorkflowCommitTransaction` là public configuration để bọc workflow commit persistence trong EF Core transaction.

Spike chỉ dùng public registrations/API:

```csharp
elsa.UseWorkflowManagement(management =>
    management.UseEntityFrameworkCore(ef => ef.UseSqlServer(connectionString)));

elsa.UseWorkflowRuntime(runtime =>
    runtime.UseEntityFrameworkCore(ef =>
    {
        ef.UseSqlServer(connectionString);
        ef.UseWorkflowCommitTransaction = true;
    }));
```

Không dùng reflection, internal API hoặc patch package.

Tài liệu Elsa chính thức chỉ ra migration theo từng context:

```text
dotnet ef database update --context ManagementElsaDbContext
dotnet ef database update --context RuntimeElsaDbContext
```

Spike dùng `RunMigrations=true` duy nhất trong command `migrate` để kiểm tra package migrations. Giá trị mặc định khi tạo host là `false`; production phải dùng deployment step có kiểm soát, không auto-migrate application startup.

## 5. Persistence footprint

Inventory read-only sau migration/runtime smoke:

| Ownership | Tables |
|---|---|
| EF | `__EFMigrationsHistory` |
| Elsa Management | `WorkflowDefinitions`, `WorkflowInstances` |
| Elsa Runtime | `ActivityExecutionRecords`, `BookmarkQueueDeadLetterItems`, `BookmarkQueueItems`, `Bookmarks`, `KeyValuePairs`, `Triggers`, `WorkflowExecutionLogRecords`, `WorkflowInboxMessages` |
| Spike evidence | `SpikeInboxMessages`, `SpikeWorkflowMappings` |

Custom inbox có unique index trên `DomainEventId`, JSON check constraint, processed/outcome/error fields và rowversion. Mapping có primary key `InspectionId`, unique `WorkflowInstanceId`, `LastDomainEventId`, shadow state và rowversion.

`__EFMigrationsHistory` được EF Core tạo khi command `migrate` áp dụng package migrations của `ManagementElsaDbContext` và `RuntimeElsaDbContext`. Đây là migration history của schema Elsa trong `ConstructionInspectionElsaSpikeDb`, không phải history migration của application trong `ConstructionInspectionDb`. Hai bảng `SpikeInboxMessages` và `SpikeWorkflowMappings` được command `migrate` tạo sau khi safety guard đã xác nhận database name; host chạy command khác không tạo hoặc apply schema.

## 6. ADR-011 verdict

**Evidence:** Runtime persistence lưu bookmark, queue, inbox và execution records nhưng workflow instance được lưu qua `ManagementElsaDbContext.WorkflowInstances`. Code-first definition được đăng ký lại khi host restart, tuy nhiên instance/correlation cần Management persistence để được tìm và resume theo public runtime path đã thử nghiệm.

**Verdict đề xuất:** dùng cả Management và Runtime persistence cho production E1 nếu tiếp tục kiến trúc này. Không thể kết luận Runtime-only là đủ. Migration ownership phải gồm cả hai Elsa contexts trong database Elsa riêng. ADR-011 có đủ evidence kỹ thuật để quản lý chốt theo hướng này, nhưng tài liệu này không tự thay đổi trạng thái ADR.

## 7. Workflow và restart results

### Approved flow

```text
Created -> Submitted -> StartReview -> Approved
```

Mỗi event được gửi từ một CLI process riêng. Sau process restart, state là `Approved`, runtime sub-status `Finished`, không còn bookmark và last event vẫn được persist.

### Suspended flow

Sau `Created -> Submitted`, process mới đọc được state `Submitted/Suspended` cùng hai bookmark `StartReview` và `ReviewerReassigned`. Process mới gửi `StartReview`, state chuyển thành `UnderReview/Suspended` cùng bốn action bookmark hợp lệ.

### Recheck flow

```text
Created
-> Submitted
-> StartReview
-> RecheckRequested
-> RevisionCreated
-> Resubmitted
-> process restart
-> StartReview
-> Approved
```

State cuối là `Approved/Finished`. Gửi một DomainEventId mới cho transition `Approved` sau terminal trả `OutOfOrder`; state không đổi.

### Reassign

`ReviewerReassigned` từ `UnderReview` đưa shadow về `Submitted` và tái tạo đúng bookmark `StartReview`/`ReviewerReassigned`.

## 8. Idempotency và concurrency

Đã chứng minh:

- cùng `DomainEventId` tuần tự: lần sau là duplicate no-op;
- cùng `DomainEventId` đồng thời từ hai host: một `Processed`, một `Duplicate`, chỉ một inbox row;
- duplicate sau restart: no-op;
- duplicate terminal event cùng `DomainEventId`: no-op;
- cùng `InspectionId` nhưng event ID khác: chỉ hợp lệ nếu transition hiện tại cho phép;
- cùng `DomainEventId` nhưng payload/hash khác: `PayloadConflict`, fail-closed;
- event type ngoài allowlist: exception fail-closed;
- event out-of-order: `OutOfOrder`, state/bookmark không bị mutate;
- cancellation được quan sát và host dispose không để listener.

SQL application lock dùng owner `Session` và được release tường minh trước khi connection trở về pool. Unique constraint vẫn là durable backstop.

Không tuyên bố exactly-once delivery. Delivery production dự kiến vẫn là at-least-once. Spike chỉ chứng minh một logical effect trong ma trận đã thử nghiệm.

## 9. Crash-window matrix

| Crash point | Durable state quan sát được | Reconcile | Logical result |
|---|---|---|---|
| Trước inbox commit | Không có inbox row, không có Elsa mutation | Gửi lại event | Một transition |
| Sau inbox commit, trước Elsa call | Inbox `Pending`, Elsa chưa đổi | Replay pending event | Một transition, inbox `Processed` |
| Sau Elsa persistence, trước processed marker | Elsa state và `LastDomainEventId` đã persist; inbox còn `Pending` | Nhận ra event đã reflected và chỉ mark processed | Không resume lần hai |

Không có event nào mất âm thầm trong test. Không có hai logical transitions cho cùng `DomainEventId` trong test tuần tự, đồng thời hoặc restart.

## 10. ADR-006 atomicity verdict

`UseWorkflowCommitTransaction=true` chỉ bảo đảm các write thuộc workflow commit của Elsa Runtime context. Custom `SpikeInboxMessages` nằm trong một `SpikeDbContext` khác. Public surface được kiểm tra không cung cấp transaction/connection handle để enlist custom inbox insert/processed marker cùng Elsa start/resume/state mutation vào một local transaction duy nhất.

Do đó spike thực hiện hai durable boundaries và dùng reconciliation để khép các crash window đã biết. Đây không phải atomic commit và không được mô tả như distributed transaction hay exactly-once delivery.

**ADR-006 verdict: không đạt điều kiện thiết kế hiện tại.** Production E1 chỉ có thể GO sau khi một thiết kế mới chứng minh bằng public API một trong hai hướng:

1. processed marker unique được ghi bên trong chính Elsa workflow commit transaction; hoặc
2. một Elsa-supported inbox/extension point cung cấp `DomainEventId` idempotency và cùng transaction với state mutation.

Cho tới lúc đó, E1 production source là **NO-GO**, dù reconciliation của spike đã qua các test crash hiện tại.

## 11. Candidate redesign after the spike — đã thử và bị loại

Đây là candidate nghiên cứu từng được đề xuất để sửa ADR-006. Candidate đã được thử trong checkpoint kế tiếp và bị loại theo bằng chứng tại docs/20; phần dưới được giữ làm lịch sử kỹ thuật, không phải đề xuất triển khai hiện hành.

Business transaction tiếp tục ghi transactional outbox. Mỗi event phải có tối thiểu:

- `DomainEventId` duy nhất;
- `AggregateId`;
- `AggregateSequence` tăng đơn điệu trên từng aggregate;
- `EventType` thuộc strict allowlist.

Elsa workflow state phải persist:

- `LastAppliedSequence`;
- `LastDomainEventId`;
- `CurrentShadowState`.

Sequence validation, durable event receipt và shadow-state mutation phải diễn ra trong cùng một workflow execution/commit do Elsa Runtime persistence sở hữu. Custom inbox bên ngoài workflow chỉ còn là transport/diagnostic record, không còn là authoritative idempotency boundary. Processed marker bên ngoài workflow có thể được cập nhật eventual; workflow state mới là bằng chứng authoritative rằng logical effect đã được áp dụng.

Candidate rules:

| Input | Candidate behavior |
|---|---|
| Sequence nhỏ hơn hoặc bằng `LastAppliedSequence`, receipt trùng khớp | Idempotent no-op |
| Sequence đúng bằng `LastAppliedSequence + 1` | Validate receipt rồi apply transition trong cùng workflow commit |
| Sequence lớn hơn `LastAppliedSequence + 1` | Fail-closed, giữ event để reconciliation xử lý gap |
| Cùng sequence nhưng `DomainEventId` hoặc payload hash khác | Conflict và dead-letter; không mutate shadow state |

Một spike riêng vẫn phải chứng minh bằng public Elsa API rằng:

1. concurrent stimuli trên cùng workflow instance được serialize;
2. workflow-owned receipt, `LastAppliedSequence`, `LastDomainEventId` và shadow state commit atomically;
3. crash/restart không tạo hai logical transitions hoặc bỏ qua sequence;
4. transport processed marker có thể phục hồi eventual mà không trở thành nguồn sự thật.

Spike đó đã hoàn tất và **không đạt**: workflow-owned receipt không rollback atomically ở mọi activity-fault window. Candidate này không được tiếp tục, không cấp phép E1 production source và không cấp phép E2. Xem docs/20 để có quyết định cuối.

## 12. Security result

- Event DTO chỉ có version, IDs, event/status, revision và reviewer IDs tối thiểu.
- Không có JWT, credential, `DataJson`, Form Schema, file content hoặc business comment.
- Unknown event và invalid envelope fail-closed.
- Không có raw payload/connection string trong console output.
- Không có HTTP/generic stimulus endpoint.
- User Secret chỉ tồn tại trong process; environment variable bị xóa sau lệnh.

## 13. Performance snapshot

Các số liệu là Development workstation, không phải production benchmark:

| Phép đo | Kết quả |
|---|---:|
| Explicit migration/startup đầu tiên | 6,137 ms |
| Process startup + state read | 1,782-2,237 ms |
| Process startup + resume/mutation | 2,538-3,121 ms |
| Working set read process | khoảng 108-112.5 MiB |
| Working set mutation process | khoảng 122.6-123.6 MiB |
| Working set migration process | 126.8 MiB |
| Database allocated size | 83,886,080 bytes (80 MiB) |

Trong một final test run, inbox tăng từ 68 lên 96 rows và mapping tăng từ 18 lên 25. Used table pages tăng từ 5,218,304 lên 5,677,056 bytes, tức 458,752 bytes cho 28 inbox events, 7 mappings và các Elsa execution/bookmark/log rows liên quan. Allocation file vẫn 80 MiB.

## 14. Test và regression

### Spike

- Restore/build: đạt, 0 warning/error.
- Tests final hai lượt liên tiếp: 11/11 và 11/11; 0 fail, 0 skip.
- Formatter: đạt.
- Vulnerability scan: 0 advisory.
- Deprecated package scan: 0.
- Runtime smoke: suspended bookmark/restart, recheck/resubmit, concurrent duplicate và crash sau Elsa commit trước processed marker đều đạt trong cả hai lượt test; smoke bằng process riêng cho Approved, suspended/restart và recheck cũng đạt.

### Production regression

- `dotnet tool restore`: đạt (`dotnet-ef` 10.0.12).
- Production restore/build: đạt, 0 warning/error.
- Domain: 82/82.
- Application: 124/124.
- Integration final hai lượt liên tiếp trên cùng binary: 36/36 và 36/36; không đổi timeout, retry, encryption hoặc connection configuration.
- Production formatter: đạt.
- Production NuGet audit: 0 vulnerability.
- EF pending model: không có thay đổi model.
- Development migrations: toàn bộ migration tới `20261020133408_AddChecklists` đã applied, không có migration mới.
- Frontend `npm ci`: đạt.
- Frontend: 134/134 tests.
- Frontend production build: đạt.
- `npm audit`: 0 vulnerability.

## 15. GO/NO-GO và bước kế tiếp

| Scope | Kết luận |
|---|---|
| Elsa 3.9 SQL durable persistence trên .NET 10 | GO về compatibility |
| Durable bookmark/restart/correlation | GO |
| Duplicate/concurrent/crash reconciliation trong spike | GO cho evidence |
| ADR-011 persistence footprint | Evidence đủ: Management + Runtime cần thiết |
| ADR-006 atomic single-effect | NO-GO |
| E1 production source | NO-GO |
| E2 authoritative workflow | NO-GO |

Blocker thực sự trước production E1 source là transaction boundary được Elsa hỗ trợ công khai cho durable idempotency marker và workflow mutation. Không triển khai dispatcher production, production migration hoặc E2 từ checkpoint này.
