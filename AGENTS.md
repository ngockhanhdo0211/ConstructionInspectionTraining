# Quy tắc làm việc

- Luôn đọc đủ ba file trong `docs` trước khi sửa nghiệp vụ.
- Không tự thay đổi phạm vi MVP.
- Không đưa business logic vào Controller.
- Domain không phụ thuộc Infrastructure hoặc project bên ngoài kiến trúc đã duyệt.
- Command thay đổi dữ liệu; Query chỉ đọc dữ liệu.
- Không trả Entity trực tiếp qua API; dùng request/response DTO.
- Không hard-code secret, password hoặc connection string thật.
- Không dùng `MAX + 1` để sinh Inspection Number.
- Không xóa ProcessHistory hoặc revision đã Submit.
- Mọi transition phải kiểm tra status, role và rowVersion.
- Không tự tạo migration khi chưa được yêu cầu.
- Sau mỗi thay đổi phải chạy build hoặc test phù hợp.
- Không tự commit hoặc push Git.
- Phải báo cáo các file đã thay đổi và lý do.
