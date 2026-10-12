import type { CreateConstructionObjectInput, DrawingObjectMapping, DrawingPageMappings, DrawingRectangle, LoginResponse, PagedResult, Drawing, Problem, UploadResponse, ConstructionObjectListItem } from './types'

export const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5277').replace(/\/$/, '')
let getToken: () => string | null = () => null
let onUnauthorized: () => void = () => undefined

export class ApiError extends Error {
  readonly status: number
  readonly problem?: Problem
  constructor(status: number, problem?: Problem) { super(problem?.detail ?? problem?.title ?? `Request failed (${status})`); this.status = status; this.problem = problem }
}

export function configureApi(tokenProvider: () => string | null, unauthorized: () => void) { getToken = tokenProvider; onUnauthorized = unauthorized }

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = getToken()
  const response = await fetch(`${apiBaseUrl}${path}`, { ...init, headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...init?.headers } })
  if (!response.ok) {
    if (response.status === 401) onUnauthorized()
    const problem = await response.json().catch(() => undefined) as Problem | undefined
    throw new ApiError(response.status, problem)
  }
  return response.json() as Promise<T>
}

export const login = (email: string, password: string) => request<LoginResponse>('/api/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) })
export const getDrawings = () => request<PagedResult<Drawing>>('/api/drawings?pageNumber=1&pageSize=50&sortBy=createdat&sortDirection=desc')
export const getDrawing = (id: string) => request<Drawing>(`/api/drawings/${id}`)
export const getDrawingContentUrl = (id: string) => `${apiBaseUrl}/api/drawings/${encodeURIComponent(id)}/content`
export const getDrawingMappings = (drawingId: string, pageNumber: number, signal?: AbortSignal) =>
  request<DrawingPageMappings>(`/api/drawings/${encodeURIComponent(drawingId)}/mappings?pageNumber=${pageNumber}`, { signal })
export const getConstructionObjects = (search = '', signal?: AbortSignal) =>
  request<PagedResult<ConstructionObjectListItem>>(`/api/construction-objects?pageNumber=1&pageSize=100&isActive=true&sortBy=name&sortDirection=asc${search ? `&search=${encodeURIComponent(search)}` : ''}`, { signal })
export const createDrawingMapping = (drawingId: string, pageNumber: number, constructionObjectId: string, geometry: DrawingRectangle) =>
  request<DrawingObjectMapping>(`/api/drawings/${encodeURIComponent(drawingId)}/mappings`, { method: 'POST', body: JSON.stringify({ pageNumber, constructionObjectId, shapeType: 'Rectangle', geometry }) })
export const updateDrawingMapping = (mappingId: string, constructionObjectId: string, geometry: DrawingRectangle, rowVersion: string) =>
  request<DrawingObjectMapping>(`/api/drawing-object-mappings/${encodeURIComponent(mappingId)}`, { method: 'PUT', body: JSON.stringify({ constructionObjectId, geometry, rowVersion }) })
export const deactivateDrawingMapping = (mappingId: string, rowVersion: string) =>
  request<{ id: string; status: 'Inactive'; rowVersion: string }>(`/api/drawing-object-mappings/${encodeURIComponent(mappingId)}/deactivate`, { method: 'POST', body: JSON.stringify({ rowVersion }) })
export const createObjectWithDrawingMapping = (drawingId: string, pageNumber: number, geometry: DrawingRectangle, object: CreateConstructionObjectInput) =>
  request<{ constructionObject: ConstructionObjectListItem; mapping: DrawingObjectMapping }>(`/api/drawings/${encodeURIComponent(drawingId)}/mappings/with-construction-object`, {
    method: 'POST', body: JSON.stringify({ pageNumber, shapeType: 'Rectangle', geometry, ...object }),
  })

export function uploadDrawing(file: File, onProgress: (value: number) => void): Promise<UploadResponse> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest(); const data = new FormData(); data.append('file', file)
    xhr.open('POST', `${apiBaseUrl}/api/drawings`)
    const token = getToken(); if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`)
    xhr.upload.onprogress = event => { if (event.lengthComputable) onProgress(Math.round(event.loaded / event.total * 100)) }
    xhr.onerror = () => reject(new Error('Không thể kết nối tới API.'))
    xhr.onload = () => {
      let payload: unknown
      try { payload = xhr.responseText ? JSON.parse(xhr.responseText) : undefined } catch { payload = undefined }
      if (xhr.status >= 200 && xhr.status < 300) resolve(payload as UploadResponse)
      else { if (xhr.status === 401) onUnauthorized(); reject(new ApiError(xhr.status, payload as Problem)) }
    }
    xhr.send(data)
  })
}
