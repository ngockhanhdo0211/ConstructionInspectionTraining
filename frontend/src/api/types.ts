export type User = { id: string; email: string; fullName: string; roles: string[] }
export type LoginResponse = { accessToken: string; tokenType: string; expiresAt: string; user: User }
export type Drawing = { id: string; originalFileName: string; contentType?: string; fileSize: number; sha256?: string; pageCount: number; status: 'Uploading' | 'Ready' | 'Failed'; createdAt: string; createdByUserId: string; rowVersion?: string }
export type PagedResult<T> = { items: T[]; pageNumber: number; pageSize: number; totalCount: number; totalPages: number }
export type UploadResponse = { drawing: Drawing; duplicateWarning: boolean }
export type Problem = { title?: string; detail?: string; status?: number; errors?: Record<string, string[]> }
