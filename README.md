# Construction Inspection Training

Construction Inspection Training là hệ thống web quản lý hoạt động kiểm tra, xác nhận và làm rõ thông tin trên các Construction Object. Ba module nghiệp vụ chính là:

- Inspection: biểu mẫu kiểm tra có revision và quy trình review.
- Checklist: danh sách tiêu chí và kết quả kiểm tra.
- RFI: Request for Information, phản hồi và làm rõ thông tin kỹ thuật.

## Tech stack dự kiến

- Backend: ASP.NET Core Web API, .NET 10, Clean Architecture, CQRS, MediatR, FluentValidation, Entity Framework Core và SQL Server.
- Frontend giai đoạn sau: React, TypeScript, Ant Design, TanStack React Query và FormIO renderer.
- Kiểm thử: xUnit; integration test dùng database test riêng trên SQL Server local khi được triển khai.

## Kiến trúc backend

Backend gồm bốn project production:

- `ConstructionInspection.Domain`: mô hình và quy tắc nghiệp vụ thuần.
- `ConstructionInspection.Application`: use case, command/query và abstraction.
- `ConstructionInspection.Infrastructure`: triển khai persistence và dịch vụ hạ tầng.
- `ConstructionInspection.Api`: ASP.NET Core Web API và composition root.

Dependency đi vào trong: API → Infrastructure/Application → Domain. Domain không phụ thuộc project nào khác.

## Trạng thái hiện tại

Dự án đã hoàn thành bộ khung solution và nền tảng persistence/Identity: EF Core SQL Server, ASP.NET Core Identity, `ApplicationDbContext`, migration `InitialIdentity` và bốn role đã được thiết lập. Chưa có Entity nghiệp vụ, JWT authentication/login, API nghiệp vụ hoặc frontend.

## Điều kiện môi trường

- .NET SDK `10.0.300`.
- SQL Server cho các giai đoạn persistence/integration test sau.
- Node.js và npm cho frontend ở giai đoạn sau.

## Restore và build backend

Từ thư mục gốc repository:

```powershell
dotnet restore backend/ConstructionInspection.sln
dotnet build backend/ConstructionInspection.sln --no-restore
dotnet test backend/ConstructionInspection.sln --no-build
```

Migration hiện tại chỉ tạo schema Identity; chưa có bảng nghiệp vụ. Connection string local được lưu bằng .NET User Secrets, không nằm trong repository. Frontend chưa được tạo.

## Tài liệu thiết kế

- `docs/01-product-scope-and-use-cases.md`
- `docs/02-erd-and-data-design.md`
- `docs/03-inspection-vertical-slice-design.md`
