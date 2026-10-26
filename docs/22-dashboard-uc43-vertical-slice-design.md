# Thiết kế Dashboard UC43 vertical slice

## 1. Trạng thái và phạm vi tài liệu

Tài liệu này thiết kế Dashboard cơ bản cho UC43 trên baseline commit
`0e8cf226b00269bc3d2d1def515513878f67c090`. Mười hai ADR tại mục 16 đã được
quản lý chấp nhận; việc triển khai source vẫn thuộc checkpoint sau.

Trong phạm vi:

- Tổng hợp metadata của Inspection, Checklist và RFI theo quyền đọc hiện hành.
- Thống kê trạng thái, công việc cần xử lý và RFI quá hạn.
- Một action queue nhỏ, có giới hạn và không chứa nội dung hồ sơ.
- Một API query-only và route frontend `/dashboard` đã được chốt.
- Đánh giá query, index, hiệu năng, bảo mật, accessibility và kiểm thử.

Ngoài phạm vi:

- UC40–UC42 Notification, scheduler, email, SignalR hoặc nhắc việc tự động.
- BI/reporting platform, export, biểu đồ tùy biến, saved filter hoặc date-range analytics.
- Mutation nghiệp vụ, bulk action, rowVersion hoặc generic workflow endpoint.
- Dashboard table/materialized view, background aggregation, Redis hoặc Elsa.
- DataJson, SchemaJson, Checklist item content, RFI question/response, PDF/MinIO.
- Thay đổi workflow, SLA hoặc object-assignment model hiện tại.

Không có source, package, migration hoặc database change trong checkpoint này.

## 2. Baseline audit

### 2.1 Repository và data baseline

| Nội dung | Kết quả xác minh |
|---|---|
| HEAD | `0e8cf226b00269bc3d2d1def515513878f67c090` |
| Branch | `upgrade/formio-elsa` |
| Worktree/staging trước thiết kế | Sạch |
| API endpoint hiện có | 66 endpoint; chưa có Dashboard |
| Migration mới nhất | `20261026053735_AddRfis` |
| EF model | Không có pending model changes |
| Workflow production | Domain + `BusinessProcess` + append-only `ProcessHistory` |
| Notification | UC40–UC42 tiếp tục Deferred |
| Frontend landing hiện tại | Inspector/Reviewer → `/inspections`; role khác → `/drawings` |

Endpoint inventory hiện hành:

| Controller/module | Số endpoint |
|---|---:|
| Authentication | 2 |
| Construction Object | 5 |
| Drawing | 4 |
| Drawing Object Mapping | 6 |
| Form Template | 8 |
| Inspection | 15 |
| Checklist | 14 |
| RFI | 12 |
| Tổng | 66 |

Dashboard sẽ là endpoint thứ 67 nếu contract một endpoint tại mục 8 được duyệt.

### 2.2 Current-state audit

- Trạng thái chuẩn của cả ba module nằm trong `BusinessProcesses`.
- Mỗi process có đúng một owner vật lý: `InspectionId`, `ChecklistId` hoặc `RfiId`.
- Inspection và Checklist lưu creator/reviewer trên aggregate; RFI lưu creator trên
  aggregate và current assignee trên process.
- Inspector query hiện tại chỉ đọc record do mình tạo.
- Reviewer query hiện tại chỉ đọc record đang được gán cho mình.
- Manager và Administrator có full operational read trong MVP vì chưa có bảng
  object/manager assignment.
- Reviewer cũ mất RFI read scope sau khi RFI được reassign.
- Inspection và Checklist hiện cố ý giữ `BusinessProcess.DueAt = null`.
- RFI cấp `DueAt` bằng 72 giờ lịch khi vào `Open`, reassign hoặc clarification;
  `Responded`, `Closed` và `Cancelled` xóa hạn hiện hành.
- Frontend đang dùng React Router, TanStack Query, session hiện hành và AppShell;
  không có chart package và không cần thêm package cho thiết kế này.

## 3. UC43 mapping chính xác

### 3.1 Contract có nguyên văn trong nguồn

| Thuộc tính | Nội dung từ `docs/01` |
|---|---|
| ID | UC43 |
| Tên | Xem dashboard cơ bản |
| Actor chính | Manager |
| Kết quả mong đợi | Thống kê theo trạng thái và trường hợp quá hạn trong phạm vi quản lý |
| Dữ liệu nguồn | `BusinessProcesses`, Inspection, Checklist, RFI và Construction Object |

Các phần sau không được tài liệu nguồn mô tả thành use-case flow chi tiết, nên chỉ
là đề xuất của tài liệu này chứ chưa phải contract đã Accepted:

- Preconditions: user đã xác thực, có một trong bốn role nghiệp vụ và scope hợp lệ.
- Main flow: backend áp scope, tính snapshot theo trạng thái, lấy action queue có
  giới hạn và trả thời điểm sinh snapshot.
- Alternative flow: dữ liệu rỗng trả các count bằng 0; anonymous trả 401; role
  ngoài allowlist trả 403; lỗi query trả ProblemDetails 500 chung.
- Postcondition: không thay đổi dữ liệu và không tạo history.
- Filter: UC43 nguồn không yêu cầu filter do client truyền; MVP đề xuất chỉ dùng
  implicit role scope, không có date/object/user filter trên endpoint Dashboard.

### 3.2 Requirement conflicts và nguồn sự thật đề xuất

| Chủ đề | Nguồn A | Nguồn B / implementation | Mâu thuẫn | Contract đề xuất | Trạng thái |
|---|---|---|---|---|---|
| Actor UC43 | `docs/01`: Manager | Yêu cầu checkpoint: bốn role | Mở rộng actor so với catalog | Cho cả bốn role xem snapshot đã scope; không tạo UC mới | Accepted — DASH-001/DASH-003 |
| Manager scope | “phạm vi quản lý” | Chưa có assignment/scope table | Không thể biểu diễn scope chi tiết | Manager full operational scope như read service hiện tại | Accepted — DASH-008 |
| Administrator scope | `docs/01` chưa chốt nội dung toàn cục | Contract module hiện tại cho full read | Tài liệu đầu kỳ mơ hồ | Full metadata/read nhưng không có Dashboard mutation | Accepted — DASH-007 |
| Inspection SLA | `docs/01–03`: SLA/quá hạn | `docs/10` và source: `DueAt = null` | Không có dữ liệu quá hạn thực | Không hiển thị metric quá hạn cho Inspection | Accepted — DASH-003 |
| Checklist SLA | `docs/01–02`: SLA/quá hạn | `docs/12` và source: `DueAt = null` | Không có dữ liệu quá hạn thực | Không hiển thị metric quá hạn cho Checklist | Accepted — DASH-003 |
| RFI quá hạn | `docs/01`: quá hạn chung | `docs/21` và source: Open + DueAt | RFI đã có semantics thật | Chỉ RFI có metric quá hạn | Accepted — DASH-009 |
| Readiness | `docs/13`: RFI chưa làm, AddChecklists | Baseline hiện tại: RFI complete, AddRfis | Báo cáo snapshot đã cũ | Baseline commit và source hiện tại thắng | Đã xác minh |
| Notification | `docs/01–03`: có notification | `docs/10`, `docs/12`, `docs/21`: Deferred | Không có notification store/service | Dashboard không phụ thuộc notification | Đã chốt Deferred |

Không gọi Inspection hoặc Checklist là “overdue” cho đến khi một checkpoint riêng
bổ sung và phê duyệt DueAt/SLA cho hai module đó.

## 4. Mục tiêu Dashboard theo role

| Role | Scope bắt buộc | Nội dung chính | Không được hiển thị |
|---|---|---|---|
| Inspector | Record do chính user tạo | Phân bố trạng thái own Inspection/Checklist/RFI; Draft/Recheck/InProgress cần làm; RFI Open đang chờ phản hồi; RFI Responded cần đóng/làm rõ | Record của Inspector khác; queue của Reviewer |
| Reviewer | Record currently assigned | Inspection/Checklist Submitted hoặc UnderReview; RFI Open; RFI Open quá hạn | Record đã reassign khỏi user; Draft chưa assign |
| Manager | Full operational scope hiện hành | Phân bố toàn hệ thống; open work; RFI quá hạn; queue theo dõi | Nội dung DataJson/question/response/item |
| Administrator | Full read hiện hành | Cùng operational metadata với Manager | Mutation shortcut; nội dung nhạy cảm không cần thiết |
| Anonymous | Không có | Không có | Toàn bộ payload; API trả 401 |

Scope phải được áp dụng trong SQL trước `GroupBy`, `Count`, ordering hoặc projection.
Frontend không được nhận global counts rồi tự lọc.

Với user có nhiều role, scope là union giống read service hiện tại: Manager hoặc
Administrator nâng thành full read; nếu không, Inspector-own hợp với
Reviewer-current-assigned. Response trả `roles[]` và `scopeMode`, không suy diễn
một role duy nhất rồi vô tình làm hẹp quyền.

## 5. Metric semantics chung

- `nowUtc` được chụp đúng một lần từ UTC clock abstraction phía server khi bắt đầu query.
- `generatedAtUtc` bằng `nowUtc` và là ISO 8601 UTC.
- `total` tính mọi trạng thái, gồm terminal, sau khi áp scope.
- `openWork` không có nghĩa overdue; đây là tổng trạng thái chưa terminal.
- Status count là mutually exclusive vì mỗi process có đúng một status hiện hành.
- Count bằng 0 vẫn có mặt trong response; frontend không phải suy diễn key thiếu.
- Không đọc ProcessHistory để tính current status hoặc assignment.
- Không deserialize `DataJson`/`SchemaJson` và không đọc RFI/Checklist content.
- Drill-down chỉ dùng route/filter frontend đã allowlist; count không mang URL tùy ý.

Terminal states:

- Inspection: `Approved`, `Rejected`.
- Checklist: `Approved`, `Rejected`.
- RFI: `Closed`, `Cancelled`.

Open-work states:

- Inspection: `Draft`, `Submitted`, `UnderReview`, `Recheck`.
- Checklist: `Draft`, `InProgress`, `Submitted`, `UnderReview`, `Recheck`.
- RFI: `Draft`, `Open`, `Responded`.

## 6. Metric data dictionary

Ký hiệu scope trong các bảng: `S` là scope đã áp theo mục 4. “Empty” luôn là số 0
và drill-down tương ứng hiển thị empty state.

### 6.1 Inspection metrics

| Metric key | Nhãn UI | Status/điều kiện | Role scope | UTC rule | Terminal | Drill-down | Empty |
|---|---|---|---|---|---:|---|---|
| `inspection.total` | Tổng Inspection | Mọi Inspection trong S | S | Không phụ thuộc thời gian | Có | `/inspections` | 0 |
| `inspection.draft` | Draft | `Draft` | S | Không | Không | `/inspections?status=Draft` | 0 |
| `inspection.submitted` | Submitted | `Submitted` | S | Không | Không | `/inspections?status=Submitted` | 0 |
| `inspection.underReview` | Under Review | `UnderReview` | S | Không | Không | `/inspections?status=UnderReview` | 0 |
| `inspection.recheck` | Recheck | `Recheck` | S | Không | Không | `/inspections?status=Recheck` | 0 |
| `inspection.approved` | Approved | `Approved` | S | Không | Có | `/inspections?status=Approved` | 0 |
| `inspection.rejected` | Rejected | `Rejected` | S | Không | Có | `/inspections?status=Rejected` | 0 |
| `inspection.openWork` | Inspection đang mở | Draft + Submitted + UnderReview + Recheck | S | Không | Không | `/inspections` + UI open-status filter | 0 |
| `inspection.awaitingMyAction` | Inspection cần tôi xử lý | Theo bảng 6.4 | S | Stable order dùng UpdatedAt/CreatedAt UTC | Không | `/inspections` với filter theo role | 0 |

Inspection không có `overdue` metric trong contract này.

### 6.2 Checklist metrics

| Metric key | Nhãn UI | Status/điều kiện | Role scope | UTC rule | Terminal | Drill-down | Empty |
|---|---|---|---|---|---:|---|---|
| `checklist.total` | Tổng Checklist | Mọi Checklist trong S | S | Không | Có | `/checklists` | 0 |
| `checklist.draft` | Draft | `Draft` | S | Không | Không | `/checklists?status=Draft` | 0 |
| `checklist.inProgress` | In Progress | `InProgress` | S | Không | Không | `/checklists?status=InProgress` | 0 |
| `checklist.submitted` | Submitted | `Submitted` | S | Không | Không | `/checklists?status=Submitted` | 0 |
| `checklist.underReview` | Under Review | `UnderReview` | S | Không | Không | `/checklists?status=UnderReview` | 0 |
| `checklist.recheck` | Recheck | `Recheck` | S | Không | Không | `/checklists?status=Recheck` | 0 |
| `checklist.approved` | Approved | `Approved` | S | Không | Có | `/checklists?status=Approved` | 0 |
| `checklist.rejected` | Rejected | `Rejected` | S | Không | Có | `/checklists?status=Rejected` | 0 |
| `checklist.openWork` | Checklist đang mở | Draft + InProgress + Submitted + UnderReview + Recheck | S | Không | Không | `/checklists` + UI open-status filter | 0 |
| `checklist.awaitingMyAction` | Checklist cần tôi xử lý | Theo bảng 6.4 | S | Stable order dùng UpdatedAt/CreatedAt UTC | Không | `/checklists` với filter theo role | 0 |

Checklist không có `overdue` metric trong contract này.

### 6.3 RFI metrics

| Metric key | Nhãn UI | Status/điều kiện | Role scope | UTC rule | Terminal | Drill-down | Empty |
|---|---|---|---|---|---:|---|---|
| `rfi.total` | Tổng RFI | Mọi RFI trong S | S | Không | Có | `/rfis` | 0 |
| `rfi.draft` | Draft | `Draft` | S | Không | Không | `/rfis?status=Draft` | 0 |
| `rfi.open` | Open | `Open` | S | Không | Không | `/rfis?status=Open` | 0 |
| `rfi.responded` | Responded | `Responded` | S | Không | Không | `/rfis?status=Responded` | 0 |
| `rfi.closed` | Closed | `Closed` | S | Không | Có | `/rfis?status=Closed` | 0 |
| `rfi.cancelled` | Cancelled | `Cancelled` | S | Không | Có | `/rfis?status=Cancelled` | 0 |
| `rfi.openWork` | RFI đang mở | Draft + Open + Responded | S | Không | Không | `/rfis` + UI non-terminal filter | 0 |
| `rfi.overdueOpen` | RFI quá hạn | `Status = Open AND DueAt IS NOT NULL AND DueAt < nowUtc` | S; Reviewer phải là current assignee | So sánh strict `<` với một `nowUtc` | Không | `/rfis?status=Open&overdue=true` | 0 |
| `rfi.awaitingMyAction` | RFI cần tôi xử lý | Theo bảng 6.4 | S | Stable order: DueAt rồi UpdatedAt UTC | Không | `/rfis` với filter theo role | 0 |
| `rfi.waitingForResponse` | Đang chờ phản hồi | Inspector own + `Open` | Inspector only | Không | Không | `/rfis?status=Open` | 0 |
| `rfi.respondedAwaitingCreator` | Đã phản hồi, chờ xử lý | Inspector own + `Responded` | Inspector only | Không | Không | `/rfis?status=Responded` | 0 |

`DueAt == nowUtc` chưa quá hạn; chỉ thời điểm nhỏ hơn `nowUtc` mới quá hạn. RFI đã
reassign chỉ tính cho current assignee vì predicate dùng `BusinessProcess.AssignedToUserId`.

### 6.4 Định nghĩa `AwaitingMyAction`

| Module | Inspector | Reviewer | Manager | Administrator |
|---|---|---|---|---|
| Inspection | Own `Draft` hoặc `Recheck` | Current-assigned `Submitted` hoặc `UnderReview` | 0; chưa có dữ liệu chứng minh cần Manager action | 0; Dashboard read-only |
| Checklist | Own `Draft`, `InProgress` hoặc `Recheck` | Current-assigned `Submitted` hoặc `UnderReview` | 0; chưa có reassignment/SLA signal | 0; Dashboard read-only |
| RFI | Own `Draft` hoặc `Responded` | Current-assigned `Open` | Current-scope `Open` đã quá hạn, để follow-up/reassign | 0; Dashboard read-only |

Manager vẫn thấy `openWork` và toàn bộ status distribution. Không biến mọi record
đang mở thành “cần Manager xử lý”, vì model chưa có escalation flag. Administrator
không có mutation shortcut nên `AwaitingMyAction` luôn bằng 0.

## 7. Action queue và drill-down

Action queue là contract bắt buộc vì nó trực tiếp trả lời “việc gần nhất cần làm”,
nhưng không thay Notification và không phải inbox bền vững.

- Tối đa 5 item mỗi module, tối đa 15 item toàn response.
- Chỉ gồm item thỏa `AwaitingMyAction` tại mục 6.4.
- Manager chỉ nhận RFI quá hạn; Administrator nhận mảng rỗng.
- `updatedAtUtc` trong queue là effective activity time:
  `UpdatedAtUtc ?? CreatedAtUtc`, nên luôn có giá trị.
- Thứ tự toàn queue: RFI quá hạn trước; item có `dueAtUtc` đứng trước item không có
  hạn và hạn gần nhất đứng trước; sau đó `updatedAtUtc ASC`, `entityType ASC`,
  `entityId ASC` làm deterministic tie-breaker.
- Mỗi module thực hiện cùng ordering trong SQL trước `Take(5)`. Application chỉ
  merge tối đa 15 DTO đã bounded bằng cùng comparator; không load unbounded entity
  list để sort trong memory.
- Queue là snapshot, không lock và không giữ chỗ cho user.

DTO item:

```json
{
  "entityType": "Rfi",
  "entityId": "00000000-0000-0000-0000-000000000000",
  "number": "RFI-2026-000001",
  "displayTitle": "Confirm fire pump pressure",
  "status": "Open",
  "actionType": "Respond",
  "dueAtUtc": "2026-10-26T03:00:00Z",
  "updatedAtUtc": "2026-10-23T03:00:00Z",
  "constructionObject": {
    "id": "00000000-0000-0000-0000-000000000001",
    "code": "FIRE-BASEMENT-01",
    "name": "Fire system - Basement"
  }
}
```

Không trả DataJson, Question/Response, Checklist criterion/note, Form schema,
StorageKey, JWT, MinIO URL hoặc arbitrary URL. API chỉ trả `entityType/entityId`;
frontend map bằng allowlist:

```text
Inspection -> /inspections/{id}
Checklist  -> /checklists/{id}
Rfi        -> /rfis/{id}
```

Status-card drill-down cũng do frontend map từ metric key sang route/filter cố
định. Không nhận hoặc thực thi route từ database.

Activity feed bị loại khỏi MVP: UC43 không yêu cầu dòng thời gian tổng hợp,
`ProcessHistory` có thể làm payload/query phình lớn và action queue đã đáp ứng nhu
cầu điều hướng. History tiếp tục được đọc tại detail của từng module.

## 8. API contract cuối

### 8.1 So sánh phương án

| Tiêu chí | A — một snapshot endpoint | B — summary + queue tách riêng |
|---|---|---|
| Route | `GET /api/dashboard` | `GET /api/dashboard/summary`, `GET /api/dashboard/actions` |
| Authorization | Một policy/scope | Phải giữ hai policy tuyệt đối đồng nhất |
| Query cost | Tối đa 6 SQL commands/request | Có thể tránh queue query khi không cần nhưng thường thành hai request |
| Cancellation | Một token xuyên toàn snapshot | Hai cancellation lifecycle |
| Cache | Một private/no-store response | Cache độc lập nhưng dễ lệch freshness |
| Failure | Toàn snapshot fail, không trả số liệu nửa vời | Summary có thể hiện dù queue lỗi; UX phức tạp hơn |
| Frontend | Một loading/error/refresh state | Có partial loading và partial error |
| Testability | Một contract snapshot | Hai contract và scope parity tests |
| Payload | Bounded vì queue tối đa 15 | Nhỏ hơn từng request nhưng tổng tương đương |

**Contract đã chốt:** đúng một endpoint `GET /api/dashboard`. Payload có giới hạn,
không có activity feed và cùng một `generatedAtUtc`, nên lợi ích đơn giản/nhất quán
lớn hơn việc tách endpoint. Không thêm endpoint thứ hai hoặc mutation endpoint.

### 8.2 Request và response

Không có query parameter trong MVP. Scope lấy hoàn toàn từ current user/roles.

```json
{
  "generatedAtUtc": "2026-10-26T11:00:00Z",
  "roles": ["INSPECTOR"],
  "scopeMode": "Own",
  "inspectionSummary": {
    "total": 4,
    "openWork": 2,
    "awaitingMyAction": 1,
    "statusCounts": {
      "draft": 1,
      "submitted": 0,
      "underReview": 1,
      "recheck": 0,
      "approved": 2,
      "rejected": 0
    }
  },
  "checklistSummary": {
    "total": 3,
    "openWork": 1,
    "awaitingMyAction": 1,
    "statusCounts": {
      "draft": 0,
      "inProgress": 1,
      "submitted": 0,
      "underReview": 0,
      "recheck": 0,
      "approved": 2,
      "rejected": 0
    }
  },
  "rfiSummary": {
    "total": 3,
    "openWork": 2,
    "overdueOpen": 1,
    "awaitingMyAction": 1,
    "waitingForResponse": 1,
    "respondedAwaitingCreator": 0,
    "statusCounts": {
      "draft": 1,
      "open": 1,
      "responded": 0,
      "closed": 1,
      "cancelled": 0
    }
  },
  "actionQueue": []
}
```

`scopeMode` là một trong `Own`, `Assigned`, `OwnAndAssigned`, `Full`. Với
Manager/Administrator là `Full`; multi-role Inspector+Reviewer là
`OwnAndAssigned`.

HTTP contract:

| HTTP | Ý nghĩa |
|---|---|
| 200 | Snapshot thành công, kể cả mọi count bằng 0 |
| 401 | Chưa xác thực hoặc token không hợp lệ |
| 403 | User không có role nghiệp vụ được hỗ trợ |
| 500 | Query không dự kiến thất bại; ProblemDetails chung, không lộ SQL/data |

Client cancellation dừng request và EF query qua `CancellationToken`; không map
thành business error. Không dùng 409 vì Dashboard không mutation/rowVersion.
Không trả partial payload khi một module query thất bại, tránh dashboard trộn số
liệu hợp lệ và thiếu mà không có provenance rõ ràng.

Response header đề xuất:

```text
Cache-Control: private, no-store
Vary: Authorization
```

## 9. Backend architecture và query strategy

### 9.1 Layering

```text
DashboardController
  -> ISender.Send(GetDashboardQuery)
    -> GetDashboardQueryHandler
      -> IDashboardReadService
        -> ApplicationDbContext / SQL Server
```

- API chỉ model-bind, authorize và trả DTO.
- Application định nghĩa query, DTO, role scope và clock boundary.
- Infrastructure triển khai SQL projection.
- Domain không cần entity/value object mới vì Dashboard là read model thuần.
- Không gọi ba list endpoint qua HTTP và không cộng count ở frontend.

### 9.2 Query shape

Mỗi module dùng một scoped base query từ aggregate join đúng process owner:

1. `AsNoTracking` từ đầu.
2. Áp role scope trong SQL.
3. `GroupBy(Status)` và `Count` một lần cho summary module.
4. Lấy tối đa 5 queue item bằng predicate role-specific, stable ordering và
   projection metadata.
5. Truyền cùng `nowUtc` và `CancellationToken` vào mọi query.

Ngân sách tối đa 6 SQL commands được phân bổ rõ ràng:

1. Inspection grouped summary: một command.
2. Inspection action queue `TOP (5)`: một command khi role có Inspection action.
3. Checklist grouped summary: một command.
4. Checklist action queue `TOP (5)`: một command khi role có Checklist action.
5. RFI grouped summary, gồm các conditional aggregate cho quá hạn/chờ phản hồi:
   một command.
6. RFI action queue `TOP (5)`: một command khi role có RFI action.

Query queue được bỏ qua khi role không có action của module đó, nên Administrator
chỉ cần ba summary commands. Không query riêng cho từng status. Các command chạy
tuần tự trên cùng DbContext vì EF Core DbContext không thread-safe; việc gộp an
toàn đã thực hiện ở cấp module.
Nếu profiling chứng minh round-trip là bottleneck, checkpoint sau có thể dùng
compiled query hoặc read service với nhiều result sets đã review; không đổi API.

Không dùng read transaction kéo dài. Snapshot là near-real-time và có thể quan sát
một commit xảy ra giữa hai module query; `generatedAtUtc` cho biết thời điểm bắt
đầu. UC43 không yêu cầu point-in-time accounting consistency và không được tăng
locking lên business workflow để đạt ảo giác atomic cross-module.

### 9.3 Index audit và migration decision

Index hiện có hữu ích:

- Aggregate: `(CreatedByUserId, CreatedAt DESC)` cho cả ba module.
- Inspection/Checklist: `(ReviewerUserId, CreatedAt DESC)`.
- Process: unique filtered owner indexes cho từng module.
- Process: `(EntityType, Status, UpdatedAt DESC)` cho global status distribution.

Khoảng trống:

- RFI current-assigned scope chưa có index bắt đầu bằng `AssignedToUserId`.
- RFI overdue range chưa có index bắt đầu bằng entity/status/due time.

**Quyết định Accepted:** migration chỉ được tạo nếu benchmark và actual execution
plan chứng minh index hiện tại không đạt budget. Nếu index hiện tại đạt target thì
không tạo migration chỉ vì tài liệu đã nêu candidate.

Hai candidate logical index để checkpoint implementation đánh giá:

1. Current assignment/status: key
   `(AssignedToUserId ASC, EntityType ASC, Status ASC, UpdatedAt ASC, Id ASC)`,
   filter `AssignedToUserId IS NOT NULL`; candidate include
   `InspectionId`, `ChecklistId`, `RfiId`, `DueAt`. Index này phục vụ current-assigned
   Reviewer scope và queue ordering.
2. RFI Open/DueAt: key `(DueAt ASC, UpdatedAt ASC, Id ASC)`, filter
   `EntityType = 'Rfi' AND Status = 'Open' AND DueAt IS NOT NULL`; candidate include
   `RfiId`, `AssignedToUserId`. Index này phục vụ global/current-assigned overdue
   range và ordering theo hạn gần nhất.

Các tên property trên đều tồn tại trong source `BusinessProcess`. Tên vật lý,
include list và direction cuối cùng phải dựa trên SQL do EF sinh và query plan,
không sao chép mù candidate vào migration.

Trước migration, checkpoint implementation bắt buộc inventory index hiện có,
capture SQL/query plan, benchmark dữ liệu đại diện, kiểm tra duplicate/redundant
index và review write/storage overhead. Nếu có evidence cần index, tạo migration
index-only riêng; không tạo Dashboard table, denormalized snapshot, backfill hoặc
thay dữ liệu nghiệp vụ.

## 10. Freshness, cache và performance budget

- Near-real-time trực tiếp từ SQL Server; không background aggregation.
- Load một lần khi route `/dashboard` được mount; người dùng chủ động dùng Manual
  Refresh khi cần dữ liệu mới.
- Không Redis, `localStorage`, `sessionStorage` hoặc persistent frontend cache nghiệp vụ.
- TanStack Query chỉ giữ in-memory cache của phiên hiện tại.
- Manual Refresh là cơ chế chính; nút bị disable khi request đang chạy.
- Không interval polling, timer, tự động refetch khi window focus hoặc request loop.
- API dùng `private, no-store`; không dùng shared/public cache.
- Payload mục tiêu nhỏ hơn 64 KiB; action queue tối đa 15 item.
- Tối đa 6 SQL commands, không N+1, không unbounded list.
- Mục tiêu Development sau warm-up: p95 ≤ 500 ms với 10.000 record mỗi module và
  30 request đồng thời; p99/production SLO chưa được tuyên bố.
- Không tăng command timeout để che việc không đạt benchmark. Khi miss target, phải
  profile SQL, capture actual execution plan và tối ưu query/index dựa trên evidence.
- Cancellation phải dừng query đang chờ khi client đổi route hoặc refresh request
  bị thay thế.
- Read-only query không tracking, không giữ transaction và không lock workflow row
  để ngăn concurrent user thực hiện transition.

Hiện chưa có dataset đủ lớn để coi latency trên là evidence. Checkpoint triển khai
phải seed dữ liệu integration/performance riêng, không dùng Development database,
đo query count, payload bytes, latency và execution plan trước khi tuyên bố đạt.

## 11. Frontend design

### 11.1 Route và landing

- Route đã chốt: `/dashboard`, bảo vệ bởi auth và capability của bốn role.
- Navigation thêm một mục `Dashboard` cho bốn role.
- Dashboard là landing sau login cho cả bốn role. Anonymous được chuyển tới
  `/login`.
- Direct route hiện có giữ nguyên; route guard không phá deep link. Redirect chỉ
  áp dụng tại login/home resolution và dùng router navigation để Browser Back/
  Forward tiếp tục hoạt động đúng.
- Không redesign AppShell hoặc các module hiện có.

### 11.2 Laptop-first layout

```text
+-----------------------------------------------------------------------+
| Dashboard                         Generated 18:00 UTC      [Refresh]   |
+----------------------+----------------------+--------------------------+
| Inspection summary   | Checklist summary    | RFI summary / overdue    |
| Total + status rows  | Total + status rows  | Total + status rows      |
+----------------------+----------------------+--------------------------+
| Cần xử lý                                                           |
| [Type] [Number + title] [Status text] [Due/updated time] [Open]      |
| ... tối đa 15 item, keyboard-accessible ...                          |
+-----------------------------------------------------------------------+
| Quick links: Inspections | Checklists | RFIs                          |
+-----------------------------------------------------------------------+
```

- Tối ưu 1366–1440 px, co xuống một cột ở viewport hẹp và hoạt động ở zoom 200%.
- Summary dùng semantic heading/list/table hoặc CSS bars; không thêm chart package.
- Nếu dùng CSS bar, luôn kèm số/text tương đương và `aria-label`; không chỉ dùng màu.
- Status luôn có text. Terminal/non-terminal không chỉ phân biệt bằng badge color.
- Loading dùng skeleton có accessible status; empty state nói rõ không có dữ liệu
  trong scope; generic error không lộ chi tiết và có nút Retry.
- Endpoint không có partial response nên UI có một generic error boundary, không
  giữ card cũ như dữ liệu mới. In-memory previous data có thể hiện với nhãn “đang
  làm mới”, nhưng không persist qua session.
- Refresh, card và queue link keyboard-focusable với outline hiện có.
- Refresh giữ nguyên kích thước/card layout, chỉ cập nhật trạng thái loading trong
  vùng hiện tại để tránh layout shift.
- Title/heading theo thứ bậc `h1` → module `h2` → subsection `h3`.

### 11.3 Role-specific presentation

- Inspector: ưu tiên own workload và shortcut tạo Inspection/Checklist/RFI theo
  capability hiện có; không có global card.
- Reviewer: ưu tiên assigned queue; không hiển thị create shortcut.
- Manager: ưu tiên global open work và RFI overdue; drill-down tới list hiện có.
- Administrator: global read, không có Dashboard mutation shortcut. Link tới màn
  quản trị hiện có vẫn nằm ở navigation riêng, không biến Dashboard thành admin UI.

Chỉ safe navigation/create shortcut đã có capability rõ ràng mới được xuất hiện.
Manager và Administrator không có nút mutation trái contract module.
Mọi drill-down tiếp tục đi qua authorization của module đích; quyền đọc metadata
toàn cục trên Dashboard không tự mở rộng quyền detail hoặc mutation của module.

## 12. Error, security và accessibility

### 12.1 Error behavior

| Tình huống | API/UI behavior |
|---|---|
| Anonymous | API 401; protected route chuyển login |
| Sai role | API 403; UI generic forbidden |
| Dataset rỗng | 200, count 0, queue rỗng, empty state |
| Request bị cancel | Dừng query; không hiện toast lỗi giả |
| SQL/unexpected failure | 500 ProblemDetails chung; Retry thủ công |

### 12.2 Security

- JWT chỉ ở Authorization header/session convention hiện có, không ở URL/log.
- Không log payload Dashboard nếu nó có user-scoped metadata; chỉ log trace ID,
  elapsed time, query outcome và count tổng hợp cần thiết.
- Không trả content, schema, storage identifier, PDF URL hoặc Identity internals.
- Plain text được render bằng React text node; không `dangerouslySetInnerHTML`.
- Backend scope trước aggregate/count để không rò số lượng ngoài scope.
- Không public cache; response thay đổi theo identity/role.
- `entityType` là enum allowlist; frontend tự dựng route từ ID canonical.
- Không truy cập MinIO và không phụ thuộc object-storage secret.

### 12.3 Accessibility acceptance

- Semantic landmarks/headings và accessible name cho card/section.
- Số, label và trạng thái đọc được không cần màu.
- Text alternative cho mọi CSS visualization.
- Keyboard navigation và visible focus.
- Screen-reader loading/error/refresh state dùng live region phù hợp.
- Layout không mất nội dung ở 1366×768, 1440×900, DPR 1/2 và zoom 200%.
- `prefers-reduced-motion` được tôn trọng; MVP không cần animation.

## 13. Backend test strategy

### 13.1 Application tests

- Anonymous/unknown role bị chặn; bốn role tạo đúng scope descriptor.
- Multi-role union và Manager/Admin full scope.
- Empty dataset trả toàn bộ key bằng 0.
- Mixed statuses tạo đúng total/status/openWork.
- `AwaitingMyAction` đúng riêng từng role/module theo mục 6.4.
- Reviewer reassignment loại Reviewer cũ và thêm Reviewer mới.
- RFI `DueAt < nowUtc` quá hạn; `DueAt == nowUtc` không quá hạn.
- Closed/Cancelled/Responded/Draft không bị tính RFI overdue.
- Inspection/Checklist không sinh overdue metric.
- Action queue giới hạn 5/module, stable ordering và metadata-only.
- CancellationToken truyền đến read service.
- DTO không có DataJson, SchemaJson, content hoặc route tùy ý.

### 13.2 Integration tests

- Anonymous 401; Inspector, Reviewer, Manager, Administrator đều 200 theo ADR.
- Scope isolation: Inspector khác và Reviewer cũ không ảnh hưởng bất kỳ count nào.
- Status/total/openWork/awaiting counts chính xác với mixed seeded data.
- Reassign Reviewer cập nhật Dashboard theo current assignment.
- RFI overdue boundary trước/bằng/sau `nowUtc`.
- Terminal states không vào queue.
- Queue limit/order ổn định khi nhiều timestamp trùng nhau.
- Không query storage và không cần ObjectStorage configuration.
- SQL command count không vượt budget; no tracking và cancellation hoạt động.
- Regression toàn bộ list/detail/workflow module hiện có.
- Index migration tương lai được kiểm tra generated SQL, applied history và query
  plan trên database test riêng có safety guard.

Không cần Domain model/test mới nếu implementation chỉ thêm query slice.

## 14. Frontend test strategy

- Protected route/anonymous redirect và bốn role navigation.
- Role-specific cards, action queue và shortcut visibility.
- Loading skeleton, zero/empty, 401/403/500 và cancellation.
- Refresh chỉ phát một request, disable khi pending và không request loop.
- Metric/card drill-down tạo đúng allowlisted list URL.
- RFI overdue hiển thị text; Inspection/Checklist không có overdue label.
- Không render content nhạy cảm dù mock response cố chèn HTML.
- Accessible card names, heading order, focus, keyboard và live region.
- Responsive structure ở 1366×768, 1440×900, DPR 1/2 và zoom 200%.
- Route change unmount hủy GET cũ; stale response không ghi đè route mới.
- Authorization header có nhưng JWT không nằm trong URL.

## 15. Rollout và rollback

1. Dùng 12 ADR Accepted tại mục 16 làm contract source checkpoint.
2. Triển khai Application contract/read service/controller và unit tests, chưa UI.
3. Benchmark và review query plan với index hiện có.
4. Chỉ khi có evidence theo DASH-012 mới tạo migration index-only riêng, review SQL
   và execution plan trước apply; không thay bảng/dữ liệu nghiệp vụ.
5. Chạy integration scope/count/performance trên database test riêng.
6. Triển khai `/dashboard`, navigation và landing; chạy component/visual QA.
7. Runtime smoke bốn role, cancellation, refresh, drill-down và security headers.
8. Chỉ commit source sau full quality gate ở checkpoint riêng.

Rollback UI/API không cần data migration. Nếu index migration đã apply, rollback chỉ
drop hai index sau khi xác nhận không có query production khác phụ thuộc; không xóa
dữ liệu. Existing list/workflow tiếp tục hoạt động độc lập với Dashboard.

## 16. ADR đã được chấp nhận

Mười hai quyết định dưới đây là contract cuối cho Dashboard UC43.

| ADR | Vấn đề | Quyết định | Lý do/trade-off | Trạng thái | Hệ quả triển khai |
|---|---|---|---|---|---|
| DASH-001 | Route/landing | `/dashboard` là landing cho bốn role; anonymous tới `/login`; giữ direct route, deep link và Back/Forward | Truy cập tổng quan thống nhất mà không phá route hiện có | Accepted | Thêm protected route/navigation và login landing |
| DASH-002 | Số endpoint | Chính xác một `GET /api/dashboard`; không endpoint thứ hai hoặc mutation | Một snapshot có cùng freshness và contract gọn | Accepted | Một query/response DTO, một controller action |
| DASH-003 | Metric bắt buộc | Status distribution, `total`, `openWork`, `awaitingMyAction`; RFI có Open chờ phản hồi, Responded chờ Inspector và Open quá hạn | Đủ phục vụ vận hành, không biến Dashboard thành BI | Accepted | Không tạo overdue cho Inspection/Checklist |
| DASH-004 | Action queue | Tối đa 5 item mỗi module, 15 tổng, stable ordering và chỉ metadata | Queue hữu ích nhưng vẫn bounded | Accepted | Ba bounded queue query trong command budget |
| DASH-005 | Activity feed | Không có activity feed; Dashboard không đọc `ProcessHistory` | Giảm payload/query và giữ audit ở detail workflow | Accepted | Không có feed DTO/query/UI |
| DASH-006 | Visualization | Cards + semantic status list/CSS bar; không chart package hoặc canvas-only; luôn có text | Nhẹ dependency và accessible | Accepted | Test text alternative, keyboard và màu sắc |
| DASH-007 | Administrator scope | Full operational metadata read, không mutation/large content; drill-down vẫn theo module authorization | Hỗ trợ giám sát mà không nâng quyền nghiệp vụ | Accepted | `awaitingMyAction` bằng 0 hoặc bỏ; không admin mutation shortcut |
| DASH-008 | Manager scope | Full operational scope, không Dashboard mutation; action đi qua module route/capability | Khớp authorization hiện hành khi chưa có scope model | Accepted | Chỉ đưa queue action Manager thực sự có quyền, ví dụ RFI reassign |
| DASH-009 | RFI overdue | Chỉ `Open`, `DueAt != null`, `DueAt < nowUtc`, trong scope/current assignment; Reviewer cũ bị loại sau reassign | Semantics đo được và đúng owner hiện tại | Accepted | Test boundary thời gian, scope và reassignment |
| DASH-010 | Refresh/cache | Load khi mount, Manual Refresh, `private, no-store`; không polling/timer/browser storage/request loop | Freshness theo yêu cầu, không tạo tải nền | Accepted | Cancel/deduplicate request và không tự retry loop |
| DASH-011 | Performance | 10.000 record/module, 30 concurrent, p95 Development ≤500 ms, payload <64 KiB, ≤6 SQL commands | Budget có thể đo; chưa coi là đạt trước benchmark | Accepted | Khi miss phải profile/query plan; không tăng timeout |
| DASH-012 | Migration | Chỉ tạo migration index-only nếu benchmark/query plan chứng minh cần; inventory index trước; không migration nếu index hiện có đạt | Tránh index thừa và thay đổi schema không có evidence | Accepted | Chỉ hai candidate ở mục 9.3; không table/snapshot/business-data change |

## 17. Điều kiện trước source

Không còn blocker quyết định nghiệp vụ cho Dashboard UC43. Checkpoint triển khai tiếp
theo phải giữ nguyên 12 ADR trên và benchmark với index hiện có trước khi quyết định
có cần migration index-only hay không. Việc bắt đầu source vẫn cần một checkpoint được
cho phép riêng; tài liệu này không cấp quyền sửa source, package, migration hoặc database.

Không có blocker về schema nghiệp vụ, Domain transition, object storage hoặc Elsa.

## 18. Definition of Done cho checkpoint triển khai tương lai

- 12/12 ADR có trạng thái Accepted và là contract cuối.
- API chỉ đọc, scope server-side, không content leakage và không mutation.
- Metric dictionary và `AwaitingMyAction` đúng cho từng role/module.
- Chỉ RFI có overdue semantics; reviewer cũ không còn trong count/queue.
- Query `AsNoTracking`, grouped server-side, bounded, cancellable và không N+1.
- Payload/query/latency đạt budget bằng database test riêng có dữ liệu đại diện.
- Index migration, nếu duyệt, chỉ thêm index và đã review SQL trước apply.
- Frontend đạt loading/empty/error/refresh/drill-down/role/accessibility/viewport tests.
- Existing 66 endpoint và workflow regression đều đạt.
- Không package mới, Dashboard table, Redis, scheduler, Notification hoặc Elsa.
- Build, unit, integration, frontend, formatter, vulnerability, EF, Git và secret
  quality gates đều đạt trước commit source.
