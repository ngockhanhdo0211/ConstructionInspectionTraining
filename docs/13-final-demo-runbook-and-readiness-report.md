# Final Demo Runbook and Readiness Report

## 1. Mục đích và phạm vi báo cáo

Tài liệu này là runbook chuẩn bị và thực hiện buổi demo quản lý cho hệ thống `ConstructionInspectionTraining` tại baseline:

- Commit: `0dab93098e954da8b6008b41752fa28e4529043c`.
- Ngày kiểm tra: 21/10/2026, múi giờ `Asia/Saigon`.
- Môi trường: Windows host, .NET 10, SQL Server local, Docker Desktop/MinIO local, React production preview.
- Phạm vi demo: Authentication, Construction Object, Drawing/PDF Viewer/Mapping, Form Template, Inspection và Checklist.
- Không nằm trong demo hiện tại: RFI, Notification, Dashboard, deployment/hosting audit và production hardening.

Đây là báo cáo readiness cho **demo local có kiểm soát**, không phải chứng nhận production-ready. Tài liệu không chứa password, JWT, connection string, storage credential hoặc giá trị User Secret.

## 2. Giá trị sản phẩm và câu chuyện trình bày

Construction Inspection gom dữ liệu kiểm tra chất lượng công trình vào một luồng có thể truy vết:

1. Construction Object tạo cấu trúc `Project → Building → Floor → Room → WorkItem` làm trục dữ liệu.
2. Bản vẽ PDF được lưu trong object storage, metadata nằm trong SQL Server và nội dung chỉ được stream qua API có xác thực.
3. Rectangle Mapping nối đúng vùng trên bản vẽ với đúng Construction Object.
4. Form Template quản lý biểu mẫu có version; Inspection luôn pin đúng Published version tại thời điểm tạo.
5. Inspection và Checklist có ownership, reviewer assignment, optimistic concurrency, immutable submission/revision và append-only process history.
6. Role và state quyết định người dùng nhìn thấy gì và được phép thực hiện hành động nào.

Thông điệp ngắn cho quản lý:

> Hệ thống biến bản vẽ, đối tượng công trình, biểu mẫu và kết quả kiểm tra thành một hồ sơ số thống nhất. Mỗi quyết định review đều có actor, thời gian, trạng thái và snapshot lịch sử; dữ liệu đã duyệt không bị ghi đè.

## 3. Kiến trúc và boundary bảo mật

- Backend: ASP.NET Core Web API `net10.0`, Clean Architecture, CQRS/MediatR, FluentValidation, EF Core SQL Server.
- Frontend: React, TypeScript, Vite, TanStack Query và PDF.js.
- Identity: ASP.NET Core Identity phát JWT Bearer access token; không có public registration hoặc refresh token trong MVP.
- Binary PDF: lưu tại MinIO private bucket; SQL Server chỉ giữ metadata, hash, page count và storage key nội bộ.
- API không trả raw Entity, storage credential, MinIO URL hoặc JWT trong URL.
- Command thay đổi dữ liệu; Query chỉ đọc. Controller bind DTO và gửi MediatR, không chứa business rules.
- Domain không phụ thuộc Infrastructure.
- Mọi transition Inspection/Checklist kiểm tra role, ownership/assignment, status và rowVersion.
- Submission/revision đã submit là immutable; ProcessHistory append-only.

## 4. Trạng thái 43 Use Case

### 4.1 Kết quả tổng hợp

| Trạng thái | Số UC | Tỷ lệ |
|---|---:|---:|
| Complete | 30 | 69,8% |
| Partial | 0 | 0% |
| Not Started | 10 | 23,3% |
| Explicitly Deferred | 3 | 7,0% |
| Tổng | 43 | 100% |

So với snapshot cũ trong `docs/11` (`17 Complete / 5 Partial / 18 Not Started / 3 Deferred`):

- UC03–UC07 đã chuyển từ Partial sang Complete nhờ Construction Object management UI.
- UC23–UC30 đã chuyển từ Not Started sang Complete nhờ Checklist backend/data/frontend.
- Kết quả mới là `30 Complete / 0 Partial / 10 Not Started / 3 Deferred`.
- `README.md` và `docs/11` vẫn là snapshot lịch sử, không phản ánh đầy đủ trạng thái hiện tại. Tài liệu này là báo cáo readiness mới nhất; không sửa ngược tài liệu lịch sử trong checkpoint này.

### 4.2 Traceability UC01–UC43

| UC | Capability | Trạng thái | Bằng chứng chính |
|---|---|---|---|
| UC01 | Login | Complete | `POST /api/auth/login`, UI login, integration/runtime 200 |
| UC02 | Current user | Complete | `GET /api/auth/me`, role phục hồi trong shell |
| UC03 | List Construction Object | Complete | API paging/search/filter/sort và `/construction-objects` |
| UC04 | Construction Object detail | Complete | Detail, breadcrumb, parent/children, Drawing sources |
| UC05 | Create Construction Object | Complete | Admin create UI/API, hierarchy validation |
| UC06 | Update Construction Object | Complete | Admin edit, immutable ObjectType, rowVersion |
| UC07 | Deactivate Construction Object | Complete | Soft deactivate, active-descendant guard |
| UC08 | List Form Template/version | Complete | Admin list/detail/version history |
| UC09 | Create/update/publish Draft | Complete | Draft v1, schema validator, publish lifecycle |
| UC10 | Create version | Complete | `NextVersionNumber`, one Draft, concurrency guard |
| UC11 | Deactivate template | Complete | Active→Inactive, history retained |
| UC12 | List Inspection | Complete | Scoped list UI/API |
| UC13 | Inspection detail | Complete | Pinned schema, current revision/process |
| UC14 | Create Inspection | Complete | Server number, latest Published version pinned |
| UC15 | Save Draft | Complete | Manual save, three rowVersions, Recheck revision support |
| UC16 | Submit/resubmit | Complete | Reviewer assignment and required validation |
| UC17 | Start Review | Complete | Assigned Reviewer only |
| UC18 | Approve | Complete | Terminal Approved, self-review guard |
| UC19 | Reject | Complete | Required reason, terminal Rejected |
| UC20 | Request Recheck | Complete | Required reason, Recheck state |
| UC21 | Revise/resubmit | Complete | Immutable prior revision, `NextRevisionNumber` |
| UC22 | Inspection history | Complete | Process timeline and revision history/detail |
| UC23 | List Checklist | Complete | Scoped list, search/status/paging |
| UC24 | Create Checklist/items | Complete | Draft aggregate with ordered items |
| UC25 | Execute Checklist | Complete | Start/Resume, Pass/Fail/N/A/NotChecked |
| UC26 | Link Inspection | Complete | Same-object many-to-many link and snapshot |
| UC27 | Submit Checklist | Complete | Completion/link/reviewer validation, immutable snapshot |
| UC28 | Review/Approve Checklist | Complete | Start Review then Approve |
| UC29 | Reject/Recheck Checklist | Complete | Reason, Resume and resubmit |
| UC30 | Checklist history | Complete | Process history plus submission list/detail |
| UC31 | List RFI | Not Started | Không có aggregate/schema/API/UI |
| UC32 | Create/save Draft RFI | Not Started | Chưa thiết kế/triển khai source |
| UC33 | Submit RFI | Not Started | Chưa có workflow RFI |
| UC34 | Assign responder | Not Started | Chưa có assignment model |
| UC35 | Respond RFI | Not Started | Chưa có append-only response chain |
| UC36 | Request clarification | Not Started | Chưa có transition |
| UC37 | Close RFI | Not Started | Chưa có terminal Close |
| UC38 | Cancel RFI | Not Started | Chưa có transition/reason contract |
| UC39 | RFI history | Not Started | Chưa có process/response history |
| UC40 | View Notification | Explicitly Deferred | WF-014/CHK-010 hoãn Notification |
| UC41 | Mark Notification read | Explicitly Deferred | Hoãn cùng UC40 |
| UC42 | Navigate from Notification | Explicitly Deferred | Hoãn cùng UC40 |
| UC43 | Manager Dashboard | Not Started | Chưa có aggregate query/API/UI |

Drawing Slice 1–3 là capability bổ sung ngoài 43 UC và đang Complete: upload/list/detail, authenticated PDF content/Range/cache, Viewer và rectangle mapping.

## 5. Readiness theo module

| Module | Backend/data | Frontend | Runtime demo | Kết luận |
|---|---|---|---|---|
| Authentication | Complete | Complete | Login/me 200 cho 4 role | Ready |
| Construction Object | Complete | Complete | Full hierarchy CRUD/deactivate | Ready |
| Drawing upload/metadata | Complete | Complete | Upload 201, duplicate warning | Ready khi MinIO health 200 |
| PDF content/Viewer | Complete | Complete | 200/206/304/416, canvas render | Ready |
| Rectangle Mapping | Complete | Complete | Create/update/deactivate + active mapping | Ready |
| Form Template | Complete | Complete | Draft v1 → Published v1 → Published v2 | Ready |
| Inspection | Complete | Complete | Draft và Approved workflow | Ready |
| Checklist | Complete | Complete | Draft, Rejected, Approved 2 snapshots | Ready |
| RFI | Not Started | Not Started | Không demo | Out of scope |
| Notification | Deferred | Deferred | Không demo | Out of scope |
| Dashboard | Not Started | Not Started | Không demo | Out of scope |

## 6. Environment preflight

### 6.1 Công cụ và service đã xác nhận

- .NET SDK: `10.0.300`.
- Node.js: `v24.19.0`.
- SQL Server service `MSSQLSERVER`: Running.
- Development database: `ConstructionInspectionDb`.
- Integration database: `ConstructionInspectionIntegrationTestDb`.
- Migration mới nhất và đã applied: `20261020133408_AddChecklists`.
- EF: không có pending model changes.
- Docker Engine: hoạt động.
- Container: `construction-inspection-minio` ở trạng thái Up.
- MinIO health tại `ObjectStorage:Endpoint` cộng `/minio/health/live` trả 200; endpoint cụ thể không ghi trong tài liệu.
- Bucket tồn tại và anonymous request trả 403; bucket không public.
- OpenAPI: `http://localhost:5277/openapi/v1.json` trả 200.
- Frontend production preview chuẩn: `http://localhost:5173`.

Lưu ý: tại thời điểm audit RAM khả dụng khoảng 717 MiB. Trước demo nên đóng IDE/tab/app không cần thiết và không chạy build, integration test, Docker workload khác song song.

### 6.2 Tên cấu hình bắt buộc

Các key phải tồn tại trong User Secrets của API, nhưng tuyệt đối không trình chiếu giá trị:

```text
ConnectionStrings:DefaultConnection
ConnectionStrings:IntegrationTestConnection
SeedUsers:DefaultPassword
Jwt:SigningKey
ObjectStorage:Endpoint
ObjectStorage:AccessKey
ObjectStorage:SecretKey
ObjectStorage:BucketName
ObjectStorage:UseSsl
```

Không chạy `dotnet user-secrets list` trên màn hình đang share vì command này in cả giá trị. Nếu cần kiểm tra, chỉ xuất tên key bằng script đã che giá trị hoặc kiểm tra trước buổi họp.

### 6.3 Kiểm tra nhanh trước buổi họp

Từ PowerShell tại root repository:

```powershell
Get-Service MSSQLSERVER
docker ps --filter "name=construction-inspection-minio"
# Kiểm tra <ObjectStorage:Endpoint>/minio/health/live bằng URL đã cấu hình an toàn.
# Không dán hoặc in endpoint/credential trong terminal đang share.
git status --short
```

Kỳ vọng:

- SQL Server `Running`.
- MinIO container `Up`.
- Health code `200`.
- Git status không có file demo/media/log phát sinh.

## 7. Khởi động demo chuẩn

### 7.1 Terminal 1 — API

```powershell
cd D:\ConstructionInspectionTraining
dotnet run --project backend/src/ConstructionInspection.Api/ConstructionInspection.Api.csproj --no-build --launch-profile http
```

Chờ dòng:

```text
Now listening on: http://localhost:5277
```

Kiểm tra:

```powershell
curl.exe -s -o NUL -w "%{http_code}`n" http://localhost:5277/openapi/v1.json
```

Kỳ vọng `200`.

### 7.2 Terminal 2 — production preview frontend

```powershell
cd D:\ConstructionInspectionTraining\frontend
npm.cmd run build
npm.cmd run preview -- --host localhost --port 5173
```

Mở:

```text
http://localhost:5173/login
```

Phải dùng đúng `localhost:5173`, không dùng `127.0.0.1:4173`. Development API hiện allow CORS origin `http://localhost:5173`; origin khác làm browser hiện `Failed to fetch` dù API vẫn hoạt động.

### 7.3 Dừng demo

- Nhấn `Ctrl+C` ở terminal frontend.
- Nhấn `Ctrl+C` ở terminal API.
- Chỉ dừng MinIO nếu không còn dự án nào sử dụng:

```powershell
docker stop construction-inspection-minio
```

Không xóa container, bucket, database hoặc demo records sau buổi họp.

## 8. Demo data đã chuẩn bị

Tất cả record mới dùng prefix `DEMO-*`; không xóa record/history cũ.

### 8.1 Construction Object

| Code | ID | Type | Trạng thái |
|---|---|---|---|
| `DEMO-PROJECT` | `5371539d-f49c-4408-a9c2-20ca94a9deb0` | Project | Active |
| `DEMO-BUILDING` | `62c834b2-1bbe-4ef6-8462-e23c6e21264d` | Building | Active |
| `DEMO-FLOOR` | `a83a020e-f806-4811-a44c-2e1a1e9539ab` | Floor | Active |
| `DEMO-ROOM` | `f34f8bc5-8ee2-4cfe-a1bc-59a831003fc6` | Room | Active |
| `DEMO-WORKITEM` | `5bb8c917-5ea5-4189-ab55-b0ec74df224c` | WorkItem | Active |
| `DEMO-INACTIVE` | `367fbccc-f927-4b8a-91dc-9e5e53b3b783` | Project | Inactive |

### 8.2 Drawing và Mapping

- Hai upload `DEMO-Readiness.pdf` ở trạng thái Ready; cùng nội dung được phép và trả `duplicateWarning=true`.
- Drawing dùng cho active overlay: `84c7c44d-4ce8-4255-bb75-60b3c530576c`.
- Active Mapping: `4386265f-633a-4da6-b201-d7c01dab2b6d`, page 1, map tới `DEMO-ROOM`.
- Một mapping khác đã đi qua create → update → deactivate để chứng minh lifecycle.

### 8.3 Form Template và Inspection

- Template `DEMO-INSPECTION`: `d2c978e1-0650-4593-9011-1e69624d5c48`, Active, latest Published version 2.
- Draft Inspection: `INS-2026-000030`, ID `4bd09882-f30a-43aa-a2c4-755f9cb71028`.
- Approved Inspection: `INS-2026-000031`, ID `2ec68c92-3367-4091-8fca-ca65e6e0438a`.

### 8.4 Checklist

- Draft: `CHK-2026-000016`, ID `bcae58bf-40e6-4b1c-83de-caba97e1b686`.
- Approved với hai immutable snapshots: `CHK-2026-000017`, ID `047c1fed-c747-409d-aced-ed0ea7cd83eb`.
- Rejected: `CHK-2026-000018`, ID `3a48d9af-f003-43c8-8e95-4fcf7669e6e1`.
- Approved Checklist có 2 submissions và 10 ProcessHistory records; có link tới Approved Inspection cùng `DEMO-ROOM`.

## 9. Kịch bản demo 15–20 phút

Không bắt đầu bằng code hoặc database. Bắt đầu từ câu chuyện người dùng và chỉ mở DevTools khi cần chứng minh HTTP/security.

### 9.1 0–2 phút — Administrator đăng nhập

1. Mở `/login`.
2. Đăng nhập `administrator@local.test` bằng mật khẩu Development lấy từ nguồn bảo mật của người trình bày.
3. Xác nhận shell hiển thị Drawings, Construction Objects, Inspections, Checklists, Form Templates.
4. Có thể mở Network và chỉ chỉ ra `/api/auth/me` trả 200; không mở raw Authorization header.

Talking point: “JWT được gửi trong Authorization header; token không nằm trong URL.”

### 9.2 2–4 phút — Construction Object

1. Mở `/construction-objects` và search `DEMO-`.
2. Mở `DEMO-ROOM`.
3. Chỉ breadcrumb Project → Building → Floor → Room và direct child WorkItem.
4. Chỉ record `DEMO-INACTIVE` để giải thích soft deactivate.

Talking point: “Hierarchy tuyến tính và ObjectType bất biến giúp dữ liệu không bị gắn sai cấp; deactivate không xóa lịch sử.”

### 9.3 4–7 phút — Drawing, Viewer và Mapping

1. Mở Drawings, chọn `DEMO-Readiness.pdf`.
2. Mở Viewer; thử Next/Previous, Zoom, Fit Width, Fit Page, Rotate.
3. Mở Drawing ID `84c7c44d-4ce8-4255-bb75-60b3c530576c`, page 1 để chỉ active rectangle mapping tới `DEMO-ROOM`.
4. Zoom/fit/resize cửa sổ để chứng minh overlay giữ đúng geometry.
5. Nếu cần demo duplicate, upload lại cùng PDF: kỳ vọng 201 và cảnh báo trùng, không overwrite object cũ.

Talking point: “Backend tự xác thực PDF, page count, size và SHA-256. Viewer tải qua API bảo vệ, không cấp MinIO credential cho browser.”

### 9.4 7–9 phút — Form Template version lifecycle

1. Mở Form Templates, search `DEMO-INSPECTION`.
2. Mở detail và chuyển giữa Published version 1 và 2.
3. Chỉ read-only state của Published version và 7 component allowlist.

Talking point: “Inspection mới dùng latest Published version; Inspection cũ vẫn pin version lịch sử, nên biểu mẫu đã duyệt không đổi theo template mới.”

### 9.5 9–13 phút — Inspection workflow

1. Mở `DEMO-Inspection-Draft` để chỉ form editable và manual Save Draft.
2. Mở `DEMO-Inspection-Approved`.
3. Chỉ status Approved, reviewer, pinned template/version, revision và Process History.
4. Đăng xuất, đăng nhập Reviewer để chỉ assigned work/read-only decision result.

Nếu cần live transition, tạo một Inspection mới thay vì sửa Approved record. Trình tự: Inspector Create → Submit chọn Reviewer → Reviewer Start Review → Approve/Recheck/Reject.

Talking point: “Mọi transition kiểm tra status, role, assignment và rowVersion; revision đã submit bất biến.”

### 9.6 13–17 phút — Checklist workflow và snapshots

1. Đăng nhập Inspector, mở `CHK-2026-000016` để chỉ Draft items.
2. Mở `CHK-2026-000017` Approved.
3. Chỉ 2 criteria, kết quả, linked Inspection, Process History và submission 1/2.
4. Chuyển giữa hai snapshots để chứng minh snapshot 1 không bị sửa sau Recheck/Resume.
5. Mở `CHK-2026-000018` để chỉ terminal Rejected + reason.

Talking point: “Checklist không chỉ lưu trạng thái cuối; mỗi lần Submit chụp immutable snapshot của items và Inspection links.”

### 9.7 17–19 phút — Role matrix

- Administrator: full configuration/read; quản lý Object, Drawing Mapping, Form Template.
- Manager: read Drawings/Object/Inspection/Checklist; không mutation workflow.
- Inspector: tạo/sửa/submit hồ sơ mình sở hữu; không review hồ sơ của mình.
- Reviewer: chỉ xử lý hồ sơ được assign; phải Start Review trước decision.
- Anonymous: protected API trả 401.

Backend role matrix đã được smoke: Manager mutation 403, Inspector/Reviewer Drawing API 403, anonymous Checklist 401. UI hiện vẫn hiển thị link Drawings cho Inspector/Reviewer; click sẽ nhận 403. Đây là UX inconsistency không làm hở dữ liệu, nên không sửa trong checkpoint docs-only.

### 9.8 19–20 phút — Kết luận

Kết thúc ở Process History/submission history, không ở trang code:

> Điểm mạnh hiện tại là traceability: từ vùng bản vẽ tới đối tượng công trình, template version, hồ sơ kiểm tra, reviewer decision và snapshot lịch sử đều có liên kết và authorization phía server.

## 10. Full E2E smoke evidence

| Luồng | Kết quả runtime |
|---|---|
| Login + `/api/auth/me`, 4 role | 200, role đúng |
| Anonymous protected API | 401 |
| Manager mutation Checklist | 403 |
| Inspector/Reviewer Drawing API | 403 |
| Construction Object create full chain | 201; đúng 5 level |
| Construction Object update | 200 |
| Construction Object deactivate | 200, Inactive |
| Form Template create/update/publish v1 | 201/200/200 |
| Create/update/publish v2 | 201/200/200 |
| Inspection Draft create | 201, Draft |
| Inspection submit/start-review/approve | 200/200/200, Approved |
| Checklist Draft create | 201, Draft |
| Checklist link Inspection | 200 |
| Checklist submit/recheck/resume/resubmit/approve | 200 mỗi bước, Approved |
| Checklist immutable submissions | 2 snapshots |
| Checklist reject | 200, Rejected |
| PDF full content | 200 |
| PDF range `bytes=0-9` | 206 |
| PDF cache `If-None-Match: *` | 304 |
| PDF invalid range | 416 |
| Duplicate PDF upload | 201, `duplicateWarning=true` hai lượt |
| Mapping create/update/deactivate | 201/200/200 |
| Final active mapping | 201, Active |
| OpenAPI | 200 |
| MinIO health | 200 |
| Private bucket anonymous access | 403 |

## 11. Visual/UX QA

Đã chạy Microsoft Edge headless thật trên production preview:

- 1366×768, DPR 1.
- 1440×900, DPR 1 và DPR 2.
- Routes: Drawing list/detail/Viewer, Construction Object list/detail, Form Template list/detail, Inspection list/detail, Checklist list/detail.
- Không horizontal overflow bất thường.
- Không alert/error state khi dùng đúng origin.
- Không console error/warning mới trong browser run.
- Viewer render 1 canvas, deep route hoạt động.
- URL không chứa JWT.
- Checklist Approved và Construction Object detail có hierarchy/status/read-only state rõ ràng.
- Status không chỉ dựa vào màu; có text label.
- Button/link dùng semantic control và visible focus theo CSS hiện tại.
- Role navigation được kiểm tra cho Administrator, Manager, Inspector và Reviewer.

Finding không chặn demo:

1. Inspector/Reviewer vẫn thấy Drawings navigation dù backend deny 403.
2. Lint có 9 warning baseline: Fast Refresh export ở Auth/Drawings và `set-state-in-effect` ở Viewer; không có lint error.
3. Frontend suite lần đầu có một timing failure ở Drawing Mapping deep-link (`133/134`); test riêng pass `6/6` và full suite kế tiếp pass `134/134` mà không sửa code/test/timeout. Theo dõi flakiness sau demo.

## 12. Quality gate cuối

| Gate | Kết quả |
|---|---|
| `dotnet tool restore` | Pass |
| `dotnet restore` | Pass |
| Backend build | Pass, 0 warning, 0 error |
| Domain tests | 82/82 pass, 0 skip |
| Application tests | 124/124 pass, 0 skip |
| Integration run 1 | 36/36 pass, 0 skip |
| Integration run 2 | 36/36 pass, 0 skip |
| `dotnet format --verify-no-changes` | Pass |
| NuGet vulnerability scan | Không có advisory |
| EF pending model | Không có thay đổi |
| Migration status | Latest/applied `AddChecklists` |
| `npm ci` | Pass |
| Frontend tests final | 134/134 pass |
| Frontend lint | 0 error; 9 baseline warnings |
| Frontend production build | Pass |
| `npm audit` | 0 vulnerability |
| `git diff --check` | Pass |
| Secret pattern/value scan | 0 finding |
| Tracked artifact scan | 0 finding |

## 13. Troubleshooting an toàn

### API không mở ở 5277

- Kiểm tra có process cũ giữ port hay không.
- Kiểm tra SQL Server `Running`.
- Chạy API với `--launch-profile http` và `ASPNETCORE_ENVIRONMENT=Development` từ launch profile.
- Không in connection string để debug trên màn hình share.

### Frontend hiện `Failed to fetch`

- Xác nhận frontend URL là `http://localhost:5173`, không phải `127.0.0.1` hoặc port khác.
- Xác nhận API `http://localhost:5277/openapi/v1.json` trả 200.
- Reload browser sau khi API đã lên.

### Login đúng nhưng nhận 401

- Kiểm tra API chạy Development và seeder đã chạy.
- Kiểm tra tồn tại key `SeedUsers:DefaultPassword`; không đọc giá trị trên màn hình share.
- Xác nhận email đúng bốn seeded account.
- Không hard-code hoặc ghi password vào notes/demo script.

### PDF không render

- Kiểm tra MinIO health 200 và container Up.
- Kiểm tra Drawing status Ready.
- Không fallback filesystem và không mở MinIO URL trực tiếp.
- Nếu MinIO không phục hồi trong 2 phút, bỏ phần Drawing runtime và tiếp tục Object/Template/Inspection/Checklist; nói rõ dependency đang unavailable.

### Transition trả 409

- Đây thường là stale rowVersion hoặc state conflict.
- Giữ dữ liệu đang nhập, chọn Reload latest rồi thực hiện lại có chủ đích.
- Không auto retry và không sửa database trực tiếp.

### Máy thiếu RAM

- Dừng test runner, IDE indexer và browser tabs thừa.
- Chỉ giữ SQL Server, MinIO, API, frontend preview và một browser.
- Không chạy Integration suite trong lúc demo.

## 14. Regression checklist trước khi bước vào phòng họp

- [ ] Git status sạch hoặc chỉ có commit docs readiness đã duyệt.
- [ ] SQL Server Running.
- [ ] MinIO container Up, health 200.
- [ ] OpenAPI 200.
- [ ] Frontend login page 200 tại `localhost:5173`.
- [ ] Administrator login và `/me` thành công.
- [ ] `DEMO-ROOM` detail mở được.
- [ ] `DEMO-Readiness.pdf` Viewer render canvas.
- [ ] Active mapping hiện ở page 1.
- [ ] `DEMO-INSPECTION` Published v2 mở được.
- [ ] Approved Inspection mở được.
- [ ] Approved Checklist có 2 submissions.
- [ ] Reviewer mở được assigned records.
- [ ] Manager chỉ có read actions.
- [ ] Anonymous API trả 401.
- [ ] Không mở terminal chứa User Secret/raw JWT.
- [ ] Không có DevTools tab đang hiển thị Authorization header.

## 15. Evidence và video plan

Không commit video/screenshot lớn vào Git. Lưu ngoài repository trong thư mục buổi demo có quyền truy cập hạn chế.

Capture tối thiểu:

1. Environment health: SQL Running, MinIO health 200, OpenAPI 200; che mọi secret.
2. Administrator shell và Construction Object hierarchy.
3. PDF Viewer + active rectangle mapping.
4. Form Template Published version history.
5. Approved Inspection + Process History.
6. Approved Checklist + submission 1/2 + linked Inspection.
7. Role switch Reviewer/Manager và button visibility.
8. Quality gate summary từ tài liệu này, không quay raw log chứa payload.

Quy tắc quay:

- 1920×1080 hoặc 1440×900, browser zoom 100%.
- Tắt notification hệ điều hành, email/chat overlay và password manager popup.
- Không quay User Secrets, `.env`, raw JWT, Authorization header, MinIO console credential hoặc connection string.
- Dùng record `DEMO-*`; không mở dữ liệu cá nhân ngoài phạm vi.
- Nếu cần evidence HTTP, chỉ quay method/path/status và response metadata an toàn.

## 16. Risk, gap và cách diễn đạt trung thực

### Không chặn demo local

- Inspector/Reviewer thấy Drawings nav nhưng API deny 403.
- 9 lint warning baseline, không có lint error.
- Một frontend timing failure không tái hiện ở test riêng/full rerun.
- README/docs11 là snapshot cũ.
- Demo cần đúng CORS origin `localhost:5173`.

### Chặn tuyên bố production-ready

- Chưa có deployment/hosting audit, TLS/public ingress, backup/restore, observability/alerting và operational runbook production.
- Token lưu `sessionStorage` theo thiết kế hiện tại; cần threat review production.
- RFI và Dashboard chưa triển khai; Notification được hoãn.
- Chưa có load/performance/penetration test và disaster recovery evidence.
- Demo đang phụ thuộc SQL Server, Docker Desktop và MinIO trên cùng Windows host.

Không nói “MVP 100% hoàn thành”. Cách nói đúng:

> 30/43 use case đã hoàn chỉnh và có UI; 10 use case chưa bắt đầu, 3 notification use case được hoãn rõ ràng. Sáu module trong phạm vi demo hiện đã chạy xuyên suốt và đạt quality gate local.

## 17. Quyết định readiness

### GO — demo local có kiểm soát

Cho phép demo quản lý khi preflight tại mục 6 đạt ngay trước buổi họp. Lý do:

- Toàn bộ module trong kịch bản demo có backend, data, authorization, UI và runtime evidence.
- Demo records có prefix rõ ràng và bao phủ Draft/Approved/Rejected/two snapshots.
- Build/tests/audit/formatter/EF/repository hygiene đạt.
- Visual QA thật đạt hai viewport và DPR 1/2.
- Không có finding source blocker hoặc secret/artifact leak.

### NO-GO — production release

Không dùng báo cáo này để phê duyệt production. Cần checkpoint riêng cho RFI/Dashboard nếu thuộc release target, deployment/security/backup/monitoring audit, performance test, UX cleanup và hosted environment validation.
