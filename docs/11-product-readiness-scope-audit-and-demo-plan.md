# Product Readiness, Scope Audit và Demo Plan

## 1. Mục đích và phương pháp audit

Tài liệu này là ảnh chụp trạng thái sản phẩm tại commit `a6b728ce8d3360941a55731515717f3465f6de4c` (`feat(inspections): add review workflow UI`). Kết luận được đối chiếu từ `README.md`, `docs/01`–`docs/10`, toàn bộ Controller, route/navigation frontend, Domain aggregate, Application command/query, EF migration, unit test, integration test và frontend test hiện có.

Quy ước trạng thái:

- **Complete**: có backend, dữ liệu, authorization, test và UI khi Use Case cần thao tác người dùng; đã có runtime evidence ở checkpoint gần nhất.
- **Partial**: đã có một phần chạy được nhưng thiếu một lớp hoặc luồng người dùng cần thiết.
- **Not Started**: chưa có source, schema, endpoint và UI đặc thù.
- **Explicitly Deferred**: đã có quyết định tài liệu rõ ràng hoãn khỏi MVP workflow hiện tại.
- **Superseded**: contract cũ đã bị contract mới thay thế; không dùng nhãn này để che một Use Case chưa làm.

Không coi việc chỉ có bảng hoặc API là hoàn thành nếu Use Case cần UI để người dùng thực hiện. Runtime evidence trong tài liệu là bằng chứng đã được xác nhận ở checkpoint ngay trước audit, không phải một lần chạy lại test trong checkpoint tài liệu này.

## 2. Executive summary

| Trạng thái | Số UC | Tỷ lệ trên 43 UC |
|---|---:|---:|
| Complete | 17 | 39,5% |
| Partial | 5 | 11,6% |
| Not Started | 18 | 41,9% |
| Explicitly Deferred | 3 | 7,0% |
| Superseded | 0 | 0,0% |

Sản phẩm hiện đã có một vertical slice demo tốt từ đăng nhập, bản vẽ PDF, mapping, Form Template đến Inspection review/revision. Tuy nhiên, **toàn bộ MVP 43 UC chưa hoàn thành**: thiếu UI quản trị Construction Object, toàn bộ Checklist, toàn bộ RFI, Dashboard; Notification đã được hoãn có chủ đích trong contract cuối của Inspection workflow.

Mức hoàn thành nghiêm ngặt là `17 / 43 = 39,5%`. Nếu tính cả Use Case đã có backend nhưng thiếu UI thì vùng chức năng đã được chạm tới là `22 / 43 = 51,2%`. Con số này không thay thế tỷ lệ Complete.

Khuyến nghị demo hiện tại: **demo local có kiểm soát**, dùng dữ liệu chuẩn bị trước, SQL Server và MinIO đã health-check. Chưa nên gọi đây là production-ready hoặc hosted-demo-ready.

## 3. Ma trận UC01–UC43

### 3.1 Authentication và Construction Object

| UC | Tên / actor / mục tiêu | Trạng thái | API và UI | Implementation và dữ liệu | Test / runtime evidence | Gap và khuyến nghị |
|---|---|---|---|---|---|---|
| UC01 | Đăng nhập; mọi người dùng; nhận JWT hợp lệ | Complete | `POST /api/auth/login`; `/login` | `LoginCommand`, Identity, JWT Bearer; bảng Identity | Unit + `AuthEndpointsTests`; login runtime `200` | Không có refresh token theo phạm vi; giữ nguyên cho demo |
| UC02 | Xem tài khoản hiện tại; người dùng; nhận identity/role | Complete | `GET /api/auth/me`; `AuthContext` khôi phục session và shell | `GetCurrentUserQuery`; Identity projection | Unit + integration; runtime `/me` `200` | Session token nằm trong `sessionStorage` theo thiết kế hiện tại; cần threat review trước production |
| UC03 | Xem danh sách object; bốn role; paging/search/filter/sort và scope | Partial | `GET /api/construction-objects`; chỉ được dùng trong selector Mapping/Inspection, không có route danh sách riêng | Query/read service, scope active/toàn bộ; `ConstructionObjects` | Unit + integration chứng minh list/scope | Thiếu trang quản trị/danh sách đầy đủ, paging/filter/sort UI; làm UI P0 nếu demo bắt đầu bằng quản trị object |
| UC04 | Xem chi tiết; actor có quyền; hierarchy và dữ liệu liên quan | Partial | `GET /api/construction-objects/{id}` và source mapping API; không có route detail object | Detail DTO có breadcrumb/children; mapping source query; `ConstructionObjects`, `DrawingObjectMappings` | Integration cho detail/scope | Thiếu màn detail và điều hướng source tổng hợp; bổ sung cùng UI UC03–UC07 |
| UC05 | Tạo object; Administrator; hierarchy hợp lệ | Partial | `POST /api/construction-objects`; UI chỉ tạo atomically khi khoanh Drawing | Domain hierarchy Project→Building→Floor→Room→WorkItem; audit/FK/index | Domain/application/integration; mapping runtime đã được smoke | Thiếu form tạo độc lập; atomic-create không thay thế quản trị cây tổng quát |
| UC06 | Cập nhật object; Administrator; optimistic concurrency | Partial | `PUT /api/construction-objects/{id}`; không có UI | Immutable `ObjectType`, cycle/depth validation, rowVersion | Unit + integration gồm stale rowVersion | Thiếu edit UI và conflict recovery UX |
| UC07 | Ngừng sử dụng; Administrator; không hard delete | Partial | `POST /api/construction-objects/{id}/deactivate`; không có UI | Chặn khi còn active descendant trực tiếp/gián tiếp; audit | Unit + integration | Thiếu confirmation/UI và cách người dùng thấy inactive object |

### 3.2 Form Template và Inspection

| UC | Tên / actor / mục tiêu | Trạng thái | API và UI | Implementation và dữ liệu | Test / runtime evidence | Gap và khuyến nghị |
|---|---|---|---|---|---|---|
| UC08 | Danh sách/lịch sử version; Administrator | Complete | 3 GET trong 8 Form Template endpoint; `/form-templates`, detail/version switch | Query DTO/read service; `FormTemplates`, `FormTemplateVersions` | Unit/integration/frontend; visual QA đã đạt | Không có gap trong contract MVP hiện tại |
| UC09 | Tạo template Draft v1, sửa và publish; Administrator | Complete | POST create, PUT Draft, POST publish; structured editor + JSON preview | 7 component allowlist, UTF-8/depth/count checks, immutable Published | Unit/integration/frontend; runtime lifecycle đã smoke | Không dùng FormIO package renderer; renderer nội bộ chỉ hỗ trợ allowlist đã chốt |
| UC10 | Tạo Draft version mới; Administrator | Complete | `POST /api/form-templates/{id}/versions`; action trên detail UI | `NextVersionNumber`, one-Draft filtered unique index, transaction/concurrency | Unit + concurrent integration + frontend | Gap version number được chấp nhận theo contract |
| UC11 | Deactivate template; Administrator; giữ lịch sử | Complete | deactivate endpoint + confirmation UI | Active→Inactive, audit, chặn authoring mới | Domain/integration/frontend | Không reactivate/hard delete theo phạm vi |
| UC12 | Danh sách Inspection; actor theo scope | Complete | `GET /api/inspections`; `/inspections` với search/status/paging | Scoped `AsNoTracking` projection | Integration + frontend; role/runtime smoke đạt | Không có manager-scope phức tạp trong MVP |
| UC13 | Chi tiết Inspection; actor liên quan | Complete | `GET /api/inspections/{id}`; `/inspections/{id}` | Pinned schema/current revision/process DTO; ngoài scope `404` | Integration + frontend + deep-route visual QA | Không nhúng toàn bộ history vào detail; dùng API phân trang riêng |
| UC14 | Tạo Inspection; Inspector; pin latest Published template | Complete | `POST /api/inspections`; `/inspections/new`; support lookups | SQL sequence `INS-{YEAR}-{SEQUENCE}`, atomic revision 1/process/history, pinned version | Domain/application/integration/frontend; concurrent number test | Chỉ Inspector tạo theo contract cuối |
| UC15 | Lưu Draft/Recheck draft; creator Inspector | Complete | `PUT /api/inspections/{id}/draft`; dynamic form UI | Ba rowVersion; Draft sửa revision 1; first Recheck save cấp revision N+1 | Unit/integration/frontend; runtime revision 2 đạt | Save thủ công, không autosave theo phạm vi |
| UC16 | Submit/resubmit; creator Inspector; validate và gán Reviewer | Complete | `POST /api/inspections/{id}/submit`; reviewer picker/confirm | Pinned schema validation, reviewer eligibility, atomic assignment/status/history | Application/integration/frontend; runtime submit/resubmit đạt | SLA/Notification trong docs/01–03 đã bị WF-014 hoãn; cần đồng bộ tài liệu cũ |
| UC17 | Start Review; assigned Reviewer | Complete | `POST /api/inspections/{id}/start-review`; action UI theo trạng thái | Domain transition, role+assignment+active reviewer, rowVersion/history | Domain/integration/frontend; runtime đạt | Không auto-start; đúng contract |
| UC18 | Approve; assigned Reviewer; cấm tự duyệt | Complete | approve endpoint + confirm UI | UnderReview→Approved terminal; history atomic | Domain/integration/frontend; runtime approve đạt | Không reopen theo contract |
| UC19 | Reject có lý do; assigned Reviewer | Complete | reject endpoint + reason UI | UnderReview→Rejected terminal, required reason, history | Domain/integration/frontend; runtime reject đạt | Tạo Inspection mới nếu làm lại |
| UC20 | Request Recheck có lý do; assigned Reviewer | Complete | recheck endpoint + reason UI | UnderReview→Recheck; history; revision chưa tạo cho đến Save đầu | Domain/integration/frontend; runtime đạt | Hành vi có chủ đích, tránh revision rỗng |
| UC21 | Sửa và gửi lại revision mới; creator Inspector | Complete | Save Draft + submit lại trên detail UI | `NextRevisionNumber`, submitted revision bất biến, reviewer giữ nguyên | Concurrency integration + frontend; runtime revision 2/resubmit đạt | Không dùng `MAX + 1` |
| UC22 | Xem Process History và revision history; actor liên quan | Complete | history, revisions, revision detail GET; timeline/version UI | `ProcessHistories` append-only; historical submission read-only | Integration/frontend; runtime history 8, revisions 2 | Chỉ hoàn chỉnh cho Inspection; không suy rộng sang Checklist/RFI |

### 3.3 Checklist, RFI, Notification và Dashboard

| UC | Tên / actor / mục tiêu | Trạng thái | API và UI | Implementation và dữ liệu | Test / runtime evidence | Gap và khuyến nghị |
|---|---|---|---|---|---|---|
| UC23 | Danh sách Checklist; actor có quyền | Not Started | Không có endpoint/route | Không có aggregate/table | Không có test | Thiết kế slice trước khi code; P1 |
| UC24 | Tạo Checklist và item; Inspector | Not Started | Không có | Không có `Checklist`/`ChecklistItem` | Không có | Chốt item schema và transaction; P1 |
| UC25 | Thực hiện Checklist; Inspector | Not Started | Không có | Không có result/status model | Không có | Cần state machine và UI nhập kết quả; P1 |
| UC26 | Liên kết Inspection; Inspector | Not Started | Không có | Không có join table `ChecklistInspections` | Không có | Cần invariant cùng Construction Object; P1 |
| UC27 | Submit Checklist; Inspector | Not Started | Không có | Không có process/history cho Checklist | Không có | Phải quyết định notification theo scope mới; P1 |
| UC28 | Review/Approve Checklist; Reviewer | Not Started | Không có | Không có transition/assignment | Không có | Có thể tái dùng pattern, không tái dùng aggregate Inspection; P1 |
| UC29 | Reject/Recheck Checklist; Reviewer | Not Started | Không có | Không có reason/history/recheck model | Không có | Thiết kế revision/item edit semantics; P1 |
| UC30 | Lịch sử Checklist; actor liên quan | Not Started | Không có | Không có history linkage | Không có | Chỉ làm sau UC24–UC29; P1 |
| UC31 | Danh sách RFI; actor có quyền | Not Started | Không có | Không có `Rfis` | Không có | Thiết kế RFI slice; P1 |
| UC32 | Tạo/lưu Draft RFI; Inspector | Not Started | Không có | Không có aggregate/table | Không có | Chốt content/audit/concurrency; P1 |
| UC33 | Submit RFI; creator | Not Started | Không có | Không có Draft→Open/history | Không có | Chốt assignee timing cùng UC34; P1 |
| UC34 | Giao người phản hồi; actor được phép | Not Started | Không có | Không có assignment model | Không có | Quyền giao/reassign còn cần contract cuối; P1 |
| UC35 | Phản hồi; assignee | Not Started | Không có | Không có append-only `RfiResponses` | Không có | Phản hồi phải không ghi đè; P1 |
| UC36 | Yêu cầu làm rõ; creator | Not Started | Không có | Không có Responded→Open | Không có | Chốt notification nếu vẫn deferred; P1 |
| UC37 | Đóng RFI; creator | Not Started | Không có | Không có terminal Closed | Không có | Cần authorization/history; P1 |
| UC38 | Hủy RFI; actor được phép | Not Started | Không có | Không có Cancelled/reason | Không có | Chốt quyền hủy từ Draft/Open; P1 |
| UC39 | Lịch sử RFI/response; actor liên quan | Not Started | Không có | Không có process/response chain | Không có | Làm sau lifecycle; P1 |
| UC40 | Xem Notification; người dùng | Explicitly Deferred | Không có endpoint/route | Không có `Notifications` table/service | Không có | WF-014 hoãn email/in-app notification; đưa vào P2/future scope |
| UC41 | Đánh dấu notification đã đọc; recipient | Explicitly Deferred | Không có | Không có `ReadAt` persistence | Không có | Làm cùng UC40, không thêm riêng lẻ |
| UC42 | Điều hướng từ notification; recipient | Explicitly Deferred | Không có | Không có notification→process link | Không có | Khi làm phải re-check quyền tại đích |
| UC43 | Dashboard cơ bản; Manager | Not Started | Không có endpoint/route | Chưa có query tổng hợp hoặc bảng riêng | Không có | P1 sau Checklist/RFI nếu dashboard phải phủ đủ module |

## 4. Drawing Slice 1–3 — ma trận capability bổ sung

Drawing là capability được bổ sung ngoài danh mục UC01–UC43; không tạo UC ID mới.

| Slice | Capability | Trạng thái | Backend/data | Frontend | Test và runtime evidence | Giới hạn có chủ đích |
|---|---|---|---|---|---|---|
| Slice 1 | Upload, list, detail PDF | Complete | `POST/GET /api/drawings`, detail; `DrawingDocuments`; MinIO; PDF signature/corruption/page count/50 MiB/SHA-256/compensation | Upload progress, duplicate warning, list/detail | Domain/application/integration/frontend; real upload và duplicate smoke đạt | Admin upload; Manager read; binary không ở SQL |
| Slice 2 | Authenticated PDF content/viewer | Complete | `GET /api/drawings/{id}/content`; 200/206/304/416, Range, ETag, stream từ MinIO | PDF.js viewer, page/zoom/fit/rotate, deep route, cleanup | Integration/frontend; full/range và visual smoke đạt | Inspector/Reviewer bị deny cho tới khi có assignment scope |
| Slice 3 | Rectangle mapping giữa Drawing và Construction Object | Complete | 6 mapping endpoints; `DrawingObjectMappings`; normalized geometry, rowVersion, filtered uniqueness, atomic object+mapping | Overlay view/create/edit/deactivate, selector, auto-create, deep-link highlight | Domain/application/integration/frontend; geometry qua zoom/fit/resize/rotation đã smoke | Chỉ Rectangle; tối đa 200 active mappings/trang; Admin mutate, Manager view |

## 5. Readiness theo module

| Module | Backend/data | Frontend | Authorization | Test evidence | Demo readiness | Kết luận |
|---|---|---|---|---|---|---|
| Authentication | Đủ login/me, Identity/JWT/seeder | Đủ login/session/shell/logout | JWT + role claims | Unit/integration/runtime | Sẵn sàng | Complete cho MVP hiện tại |
| Construction Objects | Đủ 5 API, hierarchy/audit/concurrency | Chỉ lookup và atomic-create trong ngữ cảnh | Read scope và Admin mutation | Unit/integration | Có thể demo gián tiếp | Partial vì thiếu management UI |
| Drawing Management | Đủ upload/list/detail + MinIO | Đủ upload/list/detail | Admin write, Admin/Manager read | Đủ nhiều tầng + smoke | Sẵn sàng nếu MinIO khỏe | Complete |
| PDF Viewer | Streaming/Range/cache đầy đủ | PDF.js controls và deep route | Fail closed theo role | Integration/frontend/runtime | Sẵn sàng | Complete |
| Drawing Mapping | Đủ CRUD/deactivate/source/atomic create | Overlay đầy đủ | Admin mutate, Manager read | Đủ nhiều tầng + geometry smoke | Sẵn sàng | Complete |
| Form Templates | Đủ 8 endpoint/version lifecycle | Admin list/editor/detail/history | Administrator-only | Unit/integration/frontend/visual | Sẵn sàng | Complete |
| Inspection Core | Đủ create/list/detail/save/lookups | Inspector create/dynamic Draft, scoped lists | Owner/assignee/global read | Unit/integration/frontend/runtime | Sẵn sàng | Complete |
| Inspection Workflow | Đủ submit/start/approve/reject/recheck/reassign | Action theo role/state, conflict UX | Role + assignment + state + rowVersion | 58/97/31/99 baseline và runtime | Sẵn sàng | Complete theo docs/10 |
| Process History | Đủ append/read cho Inspection | Timeline trên Inspection detail | Theo read scope | Integration/frontend/runtime | Sẵn sàng cho Inspection | Chưa generic cho Checklist/RFI |
| Revision History | Đủ list/detail immutable revisions | Switch/read-only/deep route | Theo read scope | Integration/frontend/runtime | Sẵn sàng | Complete cho Inspection |
| Checklist | Chưa có | Chưa có | Chưa có | Chưa có | Không | Not Started |
| RFI | Chưa có | Chưa có | Chưa có | Chưa có | Không | Not Started |
| Notification | Hoãn | Hoãn | Chưa có | Chưa có | Không cần cho demo đã duyệt | Explicitly Deferred |
| Dashboard | Chưa có | Chưa có | Chưa có | Chưa có | Không | Not Started |

## 6. Luồng E2E hiện có thể demo

Luồng dưới đây chỉ dùng chức năng đã tồn tại. Dữ liệu cần chuẩn bị: bốn demo user, ít nhất chuỗi parent hợp lệ hoặc object active, một PDF hợp lệ, một Form Template Published và MinIO/SQL Server khỏe.

| Bước | Role | Route / API chính | Kỳ vọng HTTP/UI | Thay đổi dữ liệu | Bằng chứng demo |
|---:|---|---|---|---|---|
| 1 | Administrator | `/login`; login + `/api/auth/me` | `200`, shell hiện Drawings/Inspections/Form Templates | Không | Tên và role đúng, không lộ token |
| 2 | Administrator | API Construction Object hoặc atomic-create trong Viewer | `201`; object active đúng hierarchy | Thêm `ConstructionObjects` | Do chưa có management UI, ưu tiên object chuẩn bị trước hoặc giải thích atomic-create |
| 3 | Administrator | `/drawings`; `POST /api/drawings` | `201`, progress, `Ready` | Metadata SQL + object MinIO | Tên, size, page count, timestamp |
| 4 | Administrator | Upload lại cùng PDF | `201`, duplicate warning | Drawing mới, SHA-256 trùng, StorageKey khác | Hai record, không overwrite |
| 5 | Administrator | `/drawings/{id}/viewer` | PDF render qua protected API | Không | URL không có JWT/MinIO URL |
| 6 | Administrator | Viewer controls | page/zoom/fit/rotate hoạt động | Không | Không vượt giới hạn, refresh deep route được |
| 7 | Administrator | Mapping mode + create existing object | `201`, overlay hiện đúng | Thêm `DrawingObjectMappings` | Geometry giữ đúng khi zoom/fit/resize/rotate |
| 8 | Administrator | Atomic create object + mapping | `201` atomic | Thêm object và mapping trong một SQL transaction | Không có record dở dang nếu lỗi |
| 9 | Manager | Viewer/list/mapping | `200`, chỉ xem; mutation `403` | Không | Role matrix fail closed |
| 10 | Administrator | `/form-templates/new` | Tạo Draft v1 | Thêm template/version | Structured editor hỗ trợ 7 component |
| 11 | Administrator | `/form-templates/{id}` | Update Draft rồi Publish | Version thành Published, bất biến | Version history và read-only state |
| 12 | Inspector | `/inspections/new` | Tạo Draft `201` từ object active + latest Published | Inspection, revision 1, process Draft, initial history | Number do server sinh; version được pin |
| 13 | Inspector | `/inspections/{id}` | Lưu Draft `200`; Submit `200` sau khi chọn Reviewer | DataJson, assignment, status Submitted, history | Required validation và rowVersion |
| 14 | Reviewer được giao | Inspection detail | Start Review `200` | Status UnderReview + history | Reviewer khác ngoài scope không xử lý được |
| 15 | Reviewer được giao | Request Recheck | `200`, reason bắt buộc | Status Recheck + history | Reason hiển thị an toàn |
| 16 | Inspector | Save rồi resubmit | Revision 2 được tạo/lưu, submit `200` | Revision 1 bất biến; revision 2 Submitted | Revision switch và history |
| 17 | Reviewer được giao | Start Review rồi Approve | Hai lần `200`; Approved read-only | Status terminal + history | Timeline actor/status/revision đúng |

Nhánh phụ nên demo nếu còn thời gian: Reject trên Inspection khác; Manager reassign từ UnderReview làm status quay về Submitted; Reviewer cũ mất scope; request dùng rowVersion cũ nhận `409` và UI giữ nội dung.

## 7. Kiến trúc thực tế

### 7.1 Backend

- `ConstructionInspection.Domain`: aggregate và invariant thuần cho Construction Object, Drawing, Mapping, Form Template và Inspection workflow.
- `ConstructionInspection.Application`: MediatR Command thay đổi dữ liệu, Query chỉ đọc; FluentValidation; DTO và abstraction repository/read service/storage/identity.
- `ConstructionInspection.Infrastructure`: EF Core SQL Server, ASP.NET Core Identity, JWT token service, MinIO/S3 adapter, PDFsharp metadata, repository và projection.
- `ConstructionInspection.Api`: composition root, JWT/CORS/OpenAPI, ProblemDetails, Controller chỉ map DTO và gửi MediatR request.
- Transaction/optimistic concurrency bảo vệ workflow; SQL sequence cấp Inspection Number; `NextVersionNumber` và `NextRevisionNumber` tránh `MAX + 1`.
- Binary PDF nằm trong MinIO; SQL chỉ giữ metadata, hash và StorageKey.

### 7.2 Frontend

- React 19 + TypeScript + Vite; React Router cho route; TanStack React Query cho server state.
- `AuthContext` giữ phiên hiện tại và cấu hình Authorization header; protected shell chặn anonymous.
- UI capability dựa trên role nhưng backend vẫn là security boundary.
- Dynamic Form renderer dùng allowlist 7 component, không render arbitrary HTML/script.
- PDF.js tải URL API có xác thực, không cấp credential MinIO cho browser.
- Mutation dùng pending guard; GET nhận `AbortSignal`; conflict `409` không auto-retry và giữ input cục bộ.
- CSS dùng stylesheet chung (`App.css`, `index.css`, `tokens.css`), không dùng Ant Design dù `docs/01` từng nêu dự kiến Ant Design.

### 7.3 Workflow và lý do chưa dùng Elsa

Không có package, runtime, definition hoặc persistence của Elsa trong solution. Workflow hiện dùng state machine rõ ràng trong `BusinessProcess`, MediatR command cho từng transition, SQL transaction, rowVersion và append-only `ProcessHistory`.

Đây là lựa chọn phù hợp hiện tại vì:

1. Luồng Inspection nhỏ, cố định và đã có invariant domain mạnh.
2. Transition cần transaction chặt với revision, assignment và history.
3. Cách hiện tại dễ test đồng thời và không thêm hạ tầng vận hành.
4. `docs/01` xác định Elsa đầy đủ là phần mở rộng sau MVP.

Chỉ đánh giá Elsa lại khi có workflow cấu hình động, nhiều cấp duyệt, timer/delay dài, compensation liên hệ thống hoặc business user cần thiết kế workflow. Không nên thêm Elsa chỉ để thay state machine đang ổn định.

## 8. API inventory và evidence

Repository hiện có **40 endpoint** trong 6 Controller.

| Nhóm | Số | Endpoint | Role chính | Evidence |
|---|---:|---|---|---|
| Auth | 2 | `POST /api/auth/login`; `GET /api/auth/me` | Anonymous / authenticated | Auth unit + 5 integration cases + runtime |
| Construction Object | 5 | POST, list GET, detail GET, PUT, deactivate POST | Read authenticated; write Admin | Domain/application + 3 integration scenarios |
| Drawing | 4 | Upload, list, detail, content | Admin write; Admin/Manager read | Domain/application/integration/frontend + MinIO smoke |
| Drawing Mapping | 6 | Page list, create, update, deactivate, object sources, atomic create | Admin mutate; Admin/Manager read | Domain/application + 4 integration scenarios + frontend geometry tests |
| Form Template | 8 | List, version list/detail, create, update Draft, publish, create version, deactivate | Administrator | Schema/domain/application + 4 integration scenarios + frontend lifecycle tests |
| Inspection | 15 | List, two lookups, detail, create, save, 6 workflow actions, history, revision list/detail | Scope/role theo docs/10 | Domain/application + 13 integration scenarios + frontend workflow tests/runtime |

Response không trả entity persistence trực tiếp. Public contracts dùng DTO/record; lỗi đi qua ProblemDetails. Kiểm tra scope nằm ở Application/read service, không chỉ ở việc ẩn nút UI.

## 9. Dữ liệu và migration

Migration đã có theo thứ tự:

1. `InitialIdentity` — ASP.NET Core Identity tables và role seed.
2. `AddConstructionObjects` — `ConstructionObjects`.
3. `AddDrawingDocuments` — `DrawingDocuments`.
4. `AddDrawingObjectMappings` — `DrawingObjectMappings`.
5. `AddFormTemplates` — `FormTemplates`, `FormTemplateVersions`.
6. `AddInspectionCore` — sequence, `Inspections`, `InspectionSubmissions`, `BusinessProcesses`, `ProcessHistories`.
7. `AddInspectionWorkflow` — reviewer audit, `NextRevisionNumber`, workflow constraints/index/FK.

Không có bảng Checklist, ChecklistItems, ChecklistInspections, RFI, RfiResponses hoặc Notifications trong model hiện tại. `BusinessProcesses` hiện liên kết trực tiếp `InspectionId`, không phải polymorphic process cho các module chưa triển khai.

## 10. Test và runtime evidence tại baseline

Checkpoint được chấp nhận ngay trước audit ghi nhận:

| Gate | Kết quả gần nhất |
|---|---|
| Domain tests | 58/58 pass |
| Application tests | 97/97 pass |
| Integration tests | 31/31 pass, chạy hai lần liên tiếp trên DB integration riêng |
| Frontend tests | 99/99 pass |
| Frontend production build | Pass |
| NuGet/npm vulnerability audit | Không còn advisory được báo cáo |
| EF model/migration | Không pending model change; latest migration `AddInspectionWorkflow` |
| Workflow runtime | Submit, recheck, revision 2, resubmit, approve, reject, reassign, stale 409, history/revisions đều đạt |
| Visual QA | 1366×768 DPR1; 1440×900 DPR1 và DPR2; deep route/history/revision đạt |

Finding còn tồn tại: frontend lint có **9 warning baseline** trong khu vực Auth/Drawings; không có error. Đây là debt P2, không phải blocker cho local demo nhưng không nên để thành baseline vĩnh viễn.

## 11. Findings và chênh lệch tài liệu

| Finding | Phân loại | Ảnh hưởng | Khuyến nghị |
|---|---|---|---|
| `README.md` vẫn nói chưa có entity, JWT, API nghiệp vụ hoặc frontend | Documentation drift | Người mới hiểu sai toàn bộ trạng thái | Cập nhật ở checkpoint tài liệu riêng sau khi docs/11 được duyệt |
| `docs/01–03` vẫn đưa SLA 72 giờ/Notification vào UC16 transaction | Superseded contract text | Mâu thuẫn với WF-014 | Đồng bộ rõ docs/10 là nguồn cuối; không đổi source hiện tại |
| `docs/01` nói Ant Design dự kiến, source dùng React/CSS thuần | Architecture drift | Không ảnh hưởng runtime, gây kỳ vọng sai | Cập nhật stack thực tế, không thêm package chỉ để khớp docs |
| Một số docs Drawing còn dùng ngôn ngữ “Future Slice” | Documentation snapshot drift | Khó biết slice đã triển khai | Thêm status banner hoặc update ở checkpoint riêng |
| Không có UI quản trị Construction Object | Product gap | UC03–UC07 chỉ Partial; demo tạo cây không trọn vẹn | P0 nếu yêu cầu demo từ dữ liệu trống |
| Checklist/RFI/Dashboard chưa có | MVP scope gap | 18 UC chưa hoàn thành | Lập roadmap P1 theo dependency |
| Notification/SLA không có | Accepted limitation | Không có inbox/reminder/overdue | Giữ Deferred theo WF-014; chỉ mở lại bằng quyết định scope |
| 9 lint warnings baseline | Engineering debt | Noise trong quality gate | Sửa nhỏ P2, không trộn feature commit |
| Không có CI workflow | Delivery gap | Quality gate phụ thuộc máy cá nhân | Thêm CI P1 trước nhiều contributor/release |
| Không có deployment manifest/runbook | Operations gap | Hosted demo khó tái lập | Viết runbook + cấu hình môi trường P1 |
| Secrets chỉ được quản lý local bằng User Secrets | Environment constraint | Phù hợp dev, không đủ production | Chọn secret manager của host trước deployment |
| Development DB có smoke data | Demo-data risk | Demo có thể nhiễu/không lặp lại | Chuẩn bị script/runbook dữ liệu demo có kiểm soát, không reset nhầm DB |
| MinIO/Docker và SQL Server là dependency ngoài process | Environment constraint | API khởi động nhưng Drawing có thể fail khi dependency xuống | Preflight health trước demo; không fallback filesystem |
| Không có Git remote | Repository state | Không backup/collaboration/CI từ remote | Không phải runtime blocker; thiết lập chỉ khi quản lý cho phép |

## 12. Backlog ưu tiên

Ước lượng tương đối: S (1–2 ngày), M (3–7 ngày), L (1–3 sprint), tùy review và regression.

### P0 — cần để demo end-to-end từ dữ liệu trống

| Hạng mục | UC | Gap / tác động | Dependency | Rủi ro | Ước lượng | Thứ tự |
|---|---|---|---|---|---|---:|
| Construction Object management UI | UC03–UC07 | Không thể quản trị cây đầy đủ trên web; demo hiện phải dùng dữ liệu chuẩn bị/API hoặc atomic-create | 5 API đã có | Medium: hierarchy, conflict UX | M | 1 |
| Demo-data và environment preflight runbook | Cross-cutting | Demo có thể fail vì DB/MinIO/secret/port hoặc dữ liệu lẫn smoke | SQL Server, MinIO, 4 users | Low | S | 2 |

Nếu quản lý chấp nhận demo bằng object chuẩn bị trước, UI Construction Object có thể chuyển xuống P1; khi đó không còn P0 source blocker cho luồng Drawing→Template→Inspection hiện tại.

### P1 — cần để hoàn thành phạm vi MVP gốc hoặc release có kiểm soát

| Hạng mục | UC | Gap / tác động | Dependency | Rủi ro | Ước lượng | Thứ tự |
|---|---|---|---|---|---|---:|
| Checklist vertical slice | UC23–UC30 | Thiếu toàn bộ nghiệp vụ kiểm tra tiêu chí và liên kết Inspection | CO, process/history pattern | High | L | 3 |
| RFI vertical slice | UC31–UC39 | Thiếu toàn bộ làm rõ thông tin kỹ thuật | CO, identity, process/history pattern | High | L | 4 |
| Dashboard cơ bản | UC43 | Manager chưa có tổng quan/overdue | Nên làm sau Checklist/RFI; SLA decision | Medium | M | 5 |
| CI quality gate | Cross-cutting | Không có build/test/audit tự động | Remote/runner/SQL test strategy | Medium | M | 6 |
| Deployment runbook và hosted configuration | Cross-cutting | Chưa có repeatable deployment, TLS, secret manager, backup | Hosting target chưa chọn | High | M | 7 |
| Đồng bộ README/docs cũ | Cross-cutting | Documentation drift | Quyết định docs/11 | Low | S | 8 |

### P2 — future scope hoặc polish

| Hạng mục | UC | Gap / tác động | Dependency | Rủi ro | Ước lượng | Thứ tự |
|---|---|---|---|---|---|---:|
| Notification inbox/read/navigation | UC40–UC42 | Không có thông báo in-app | Quyết định mở lại WF-014 | Medium | M | 9 |
| SLA/overdue/reminder | Cross-cutting | `DueAt` chưa được tính, không reminder | Product SLA và scheduler | High | M–L | 10 |
| Xử lý 9 lint warnings | Cross-cutting | Giảm noise quality gate | Không | Low | S | 11 |
| Elsa evaluation | Future | Chỉ cần nếu workflow động/phức tạp | Checklist/RFI và yêu cầu workflow mới | High nếu migrate sớm | Spike S | 12 |
| Advanced dashboard, realtime, refresh token | Future | Không thuộc MVP đã chốt | Nhu cầu sản phẩm/ops | Medium | L | 13 |

## 13. Deployment readiness

### 13.1 Local controlled demo

**Khả thi và được khuyến nghị.** Điều kiện:

- Windows host có .NET SDK 10.0.300, Node/npm phù hợp và SQL Server reachable.
- Development database đã apply latest migration.
- MinIO container/service chạy và health endpoint trả `200`.
- User Secrets có đủ connection, JWT, seed password và object-storage configuration; chỉ kiểm tra tên key, không in giá trị.
- API origin/CORS và `VITE_API_BASE_URL` đúng.
- Dữ liệu demo được chuẩn bị, có rollback/cleanup thủ công an toàn và không reset nhầm Development DB.
- Chạy preflight test ngắn trước buổi họp và không cập nhật package ngay trước demo.

### 13.2 Hosted demo

**Chưa sẵn sàng.** Repository không có CI, deployment manifest, reverse-proxy/TLS config, hosted secret manager, production database migration procedure, MinIO persistence/backup policy, centralized logging/monitoring hay recovery runbook. `appsettings` không chứa secret thật là đúng, nhưng User Secrets không phải cơ chế production.

Để hosted demo an toàn cần tối thiểu:

1. Chọn host và topology cho API, SPA, SQL Server và S3-compatible storage.
2. Dùng TLS end-to-end, origin allowlist cụ thể và network rule tối thiểu.
3. Đưa secret vào secret manager của host; rotation và least privilege.
4. Tạo migration/backup/restore runbook và kiểm thử trên staging.
5. Bật structured logging, health/readiness checks và retention phù hợp.
6. Chạy CI build/test/audit rồi deploy artifact bất biến.
7. Seed demo user/data bằng quy trình riêng, đổi hoặc loại bỏ credential sau demo.

## 14. Kịch bản demo 15–20 phút

| Thời lượng | Nội dung | Thông điệp quản lý |
|---:|---|---|
| 1 phút | Mở mục tiêu và vai trò | Một nguồn dữ liệu có kiểm soát cho bản vẽ, biểu mẫu và kiểm tra |
| 2 phút | Login Admin, `/me`, role navigation | Backend xác thực mọi API; UI chỉ phản ánh capability |
| 3 phút | Upload PDF và duplicate warning | Metadata kỹ thuật do server xác minh; file ở object storage, không ở SQL |
| 3 phút | Viewer + Range + khoanh mapping | Bản vẽ trở thành điểm điều hướng tới đối tượng thực tế |
| 3 phút | Tạo/publish Form Template | Biểu mẫu có version; Published bất biến; Inspection pin đúng version |
| 3 phút | Inspector tạo, lưu và Submit | Số do server sinh; dynamic form; reviewer được gán có kiểm soát |
| 3 phút | Reviewer Recheck; Inspector revision 2; Approve | Không ghi đè revision cũ; đầy đủ timeline/actor/reason |
| 1–2 phút | Manager reassign hoặc history; kết luận roadmap | Quản trị exception có audit; nêu rõ Checklist/RFI còn trong roadmap |

Quy tắc trình bày: không mở code trước; không gọi sản phẩm là full MVP; không demo secret/raw JWT/connection string; không live-edit database.

## 15. Kịch bản demo 5 phút

1. Đăng nhập Admin và mở một Drawing đã chuẩn bị.
2. Mở PDF Viewer, zoom và click overlay mapping.
3. Mở Form Template Published để chỉ ra version bất biến.
4. Chuyển sang Inspector: mở Inspection Submitted/Recheck có sẵn.
5. Chuyển Reviewer: Start Review/Approve trên hồ sơ chuẩn bị riêng.
6. Mở Process History và revision 1/2, kết luận bằng khả năng truy vết.

Không upload file lớn hoặc tạo hierarchy từ đầu trong demo 5 phút; các thao tác đó tốn thời gian và tăng rủi ro môi trường.

## 16. Manager Q&A

1. **Sản phẩm giải quyết gì?** Quản lý bản vẽ, biểu mẫu và Inspection theo đối tượng xây dựng với quyền, version và audit rõ ràng.
2. **Đã hoàn thành toàn bộ MVP chưa?** Chưa; 17/43 UC Complete, 5 Partial, 18 Not Started, 3 Deferred.
3. **Phần nào demo tốt nhất?** Drawing Slice 1–3, Form Template và Inspection end-to-end.
4. **Vì sao Construction Object chỉ Partial?** Backend đủ nhưng chưa có màn quản trị cây độc lập.
5. **Checklist có dùng được chưa?** Chưa, chưa có schema/API/UI.
6. **RFI có dùng được chưa?** Chưa, chưa có aggregate/API/UI.
7. **Notification ở đâu?** Được hoãn rõ ràng theo WF-014; hiện xem trạng thái qua danh sách/detail/history.
8. **Có SLA 72 giờ chưa?** Chưa; `DueAt` giữ nullable và workflow hiện không tính SLA.
9. **Có mất lịch sử khi sửa không?** Submitted revision bất biến; Recheck tạo revision mới; Process History append-only.
10. **Ai được duyệt?** Chỉ Reviewer active đang được gán và đã Start Review; không tự duyệt.
11. **Manager làm gì?** Xem toàn bộ Inspection và reassign Reviewer; không sửa DataJson.
12. **Administrator có sửa hồ sơ Approved không?** Không; trạng thái terminal read-only.
13. **PDF lưu ở đâu?** Binary trong MinIO; SQL Server chỉ lưu metadata/hash/key.
14. **Nếu upload trùng?** Được phép, có warning; cùng SHA-256 nhưng StorageKey khác.
15. **Browser có biết credential MinIO không?** Không; PDF đi qua API bảo vệ.
16. **File lớn có làm đầy memory không?** Upload/content dùng stream và staging; giới hạn 50 MiB được kiểm tra server-side.
17. **Form có chạy script tùy ý không?** Không; chỉ 7 component allowlist, cấm executable/HTML tùy ý/remote data.
18. **Template mới có đổi Inspection cũ không?** Không; Inspection pin chính xác `FormTemplateVersionId`.
19. **Số Inspection có trùng khi tạo đồng thời không?** SQL Server sequence và unique index bảo vệ; không dùng `MAX + 1`.
20. **Nếu hai người thao tác cùng lúc?** RowVersion và transaction làm một request thắng, request stale nhận `409`.
21. **Có tự retry transition không?** Không; UI giữ dữ liệu và yêu cầu reload có chủ đích.
22. **Có audit ai làm gì không?** Có actor, thời gian, from/to status, action, note/reason, submission và reviewer trước/sau.
23. **Tại sao chưa dùng Elsa?** State machine hiện nhỏ, cố định và cần transaction chặt; Elsa là future khi workflow thực sự động/phức tạp.
24. **Có thể deploy ngay không?** Local controlled demo được; hosted/production chưa đủ runbook, CI, secret manager và observability.
25. **Security hiện tại thế nào?** JWT, role/object scope, fail-closed, DTO, validation; vẫn cần production threat review/TLS/secret management.
26. **Tests có bao phủ gì?** Domain invariant, handler, SQL integration/concurrency, role matrix, frontend state/interaction và runtime smoke.
27. **Kết quả test gần nhất?** 58 Domain, 97 Application, 31 Integration hai lần và 99 frontend đều pass.
28. **Rủi ro demo lớn nhất?** SQL Server/MinIO/secret/port và dữ liệu demo; vì vậy cần preflight.
29. **Việc tiếp theo nên làm gì?** Chốt có cần UI Construction Object cho demo, sau đó Checklist, RFI, Dashboard và delivery hardening.
30. **Có thể gọi đây là production-ready không?** Không; đây là feature-rich local MVP slice, chưa có production operations package.

## 17. Final acceptance checklist

### Product và scope

- [ ] Quản lý chấp nhận cách tính 17 Complete / 5 Partial / 18 Not Started / 3 Deferred.
- [ ] Xác nhận UI Construction Object là P0 hay chấp nhận demo bằng dữ liệu chuẩn bị.
- [ ] Xác nhận Checklist và RFI vẫn thuộc MVP phải hoàn thành.
- [ ] Xác nhận Notification/SLA tiếp tục Deferred theo WF-014.
- [ ] Xác nhận Dashboard làm sau Checklist/RFI.

### Demo

- [ ] SQL Server reachable và latest migration đã apply.
- [ ] MinIO health `200`, bucket accessible bằng credential cấu hình.
- [ ] Bốn demo user đăng nhập được và mỗi user đúng một role.
- [ ] Có PDF hợp lệ, object active, template Published và Inspection mẫu.
- [ ] Chạy smoke login/me, Drawing content, Form Template và Inspection workflow.
- [ ] Không hiển thị User Secrets, raw JWT hoặc connection string.
- [ ] Có phương án quay về dữ liệu mẫu nếu thao tác demo lỗi.

### Engineering quality

- [ ] Restore/build/backend tests/frontend tests/audit đều pass trên máy demo.
- [ ] Integration test dùng riêng `ConstructionInspectionIntegrationTestDb`.
- [ ] EF không pending model changes và không có migration chưa review.
- [ ] Không track `bin/`, `obj/`, `node_modules/`, `dist/`, token hoặc file tạm.
- [ ] 9 lint warnings được chấp nhận cho demo hoặc xử lý ở checkpoint riêng.

### Hosted/production release

- [ ] Có CI, artifact versioning và approval gate.
- [ ] Có TLS, CORS allowlist và secret manager production.
- [ ] Có database migration/backup/restore/rollback runbook.
- [ ] Có object-storage persistence/backup/lifecycle policy.
- [ ] Có logging, health/readiness, monitoring và incident owner.
- [ ] Có security review và test phục hồi.

## 18. Kết luận audit

Baseline hiện tại đủ thuyết phục để demo năng lực cốt lõi **Drawing → Mapping → Form Template → Inspection workflow có revision/audit** trên môi trường local được kiểm soát. Điểm mạnh nhất là consistency và traceability: version được pin, revision đã submit bất biến, transition có rowVersion và lịch sử, PDF không lộ storage credential.

Không nên trình bày đây là toàn bộ sản phẩm đã hoàn thiện. Khoảng trống thật sự là UI Construction Object, Checklist, RFI, Dashboard và delivery/operations cho hosted environment. Notification và SLA không phải lỗi implementation của checkpoint workflow; đó là scope đã được hoãn rõ trong docs/10, nhưng docs/01–03 cần được đồng bộ ở checkpoint tài liệu riêng.

Khuyến nghị quyết định ngay trước bước phát triển tiếp theo:

1. Nếu buổi demo phải bắt đầu từ dữ liệu trống, làm Construction Object management UI trước.
2. Nếu demo chấp nhận dữ liệu chuẩn bị, khóa code feature hiện tại, viết preflight/runbook và diễn tập local demo.
3. Sau demo, triển khai Checklist rồi RFI; Dashboard sau khi có đủ nguồn dữ liệu.
4. Chỉ mở lại Notification/SLA bằng quyết định scope riêng.
