import type { CreateConstructionObjectInput, ConstructionObjectDetail, ConstructionObjectInput, ConstructionObjectType, DeactivatedConstructionObject, UpdateConstructionObjectInput, DrawingObjectMapping, DrawingPageMappings, DrawingRectangle, LoginResponse, PagedResult, Drawing, Problem, UploadResponse, ConstructionObjectListItem, FormTemplateListItem, FormTemplateVersions, FormTemplateVersionDetail, FormTemplateCreated, FormTemplateDeactivated, InspectionListItem, InspectionStatus, FormTemplateOption, CreateInspectionInput, InspectionDetail, SaveInspectionDraftInput, InspectionDraft, ReviewerOption, InspectionWorkflowResult, ProcessHistory, InspectionRevisionListItem, InspectionRevisionDetail, ChecklistStatus, ChecklistListItem, ChecklistDetail, CreateChecklistInput, ChecklistWorkflowResult, UpdateChecklistItemsInput, ChecklistItemsResult, ChecklistLinksResult, ChecklistHistory, ChecklistSubmissionListItem, ChecklistSubmissionDetail, RfiStatus, RfiListItem, RfiDetail, RfiWorkflowResult, RfiHistory, RfiResponderOption, CreateRfiInput, DashboardResponse } from './types'

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
export const getDashboard = (signal?: AbortSignal) => request<DashboardResponse>('/api/dashboard', { signal })
export const getDrawings = () => request<PagedResult<Drawing>>('/api/drawings?pageNumber=1&pageSize=50&sortBy=createdat&sortDirection=desc')
export const getDrawing = (id: string) => request<Drawing>(`/api/drawings/${id}`)
export const getDrawingContentUrl = (id: string) => `${apiBaseUrl}/api/drawings/${encodeURIComponent(id)}/content`
export const getDrawingMappings = (drawingId: string, pageNumber: number, signal?: AbortSignal) =>
  request<DrawingPageMappings>(`/api/drawings/${encodeURIComponent(drawingId)}/mappings?pageNumber=${pageNumber}`, { signal })
export const getConstructionObjects = (search = '', signal?: AbortSignal) =>
  request<PagedResult<ConstructionObjectListItem>>(`/api/construction-objects?pageNumber=1&pageSize=100&isActive=true&sortBy=name&sortDirection=asc${search ? `&search=${encodeURIComponent(search)}` : ''}`, { signal })
export type ConstructionObjectListParameters = {
  pageNumber: number; pageSize: number; search?: string; objectType?: ConstructionObjectType | ''
  isActive?: 'true' | 'false' | ''; sortBy?: 'code' | 'name' | 'objectType' | 'createdAt' | 'updatedAt'
  sortDirection?: 'asc' | 'desc'
}
export const getConstructionObjectPage = (parameters: ConstructionObjectListParameters, signal?: AbortSignal) =>
  request<PagedResult<ConstructionObjectListItem>>(`/api/construction-objects?${queryString(parameters)}`, { signal })
export const getConstructionObject = (id: string, signal?: AbortSignal) =>
  request<ConstructionObjectDetail>(`/api/construction-objects/${encodeURIComponent(id)}`, { signal })
export const createConstructionObject = (input: ConstructionObjectInput) =>
  request<ConstructionObjectDetail>('/api/construction-objects', { method: 'POST', body: JSON.stringify(input) })
export const updateConstructionObject = (id: string, input: UpdateConstructionObjectInput) =>
  request<ConstructionObjectDetail>(`/api/construction-objects/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(input) })
export const deactivateConstructionObject = (id: string, rowVersion: string) =>
  request<DeactivatedConstructionObject>(`/api/construction-objects/${encodeURIComponent(id)}/deactivate`, { method: 'POST', body: JSON.stringify({ rowVersion }) })
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

export const getFormTemplates = (signal?: AbortSignal) =>
  request<PagedResult<FormTemplateListItem>>('/api/form-templates?pageNumber=1&pageSize=100', { signal })
export const getFormTemplateVersions = (id: string, signal?: AbortSignal) =>
  request<FormTemplateVersions>(`/api/form-templates/${encodeURIComponent(id)}/versions?pageNumber=1&pageSize=100`, { signal })
export const getFormTemplateVersion = (id: string, versionNumber: number, signal?: AbortSignal) =>
  request<FormTemplateVersionDetail>(`/api/form-templates/${encodeURIComponent(id)}/versions/${versionNumber}`, { signal })
export const createFormTemplate = (input: { code: string; name: string; description?: string | null; schemaJson: string }) =>
  request<FormTemplateCreated>('/api/form-templates', { method: 'POST', body: JSON.stringify(input) })
export const updateFormTemplateDraft = (id: string, versionNumber: number, schemaJson: string, rowVersion: string) =>
  request<FormTemplateVersionDetail>(`/api/form-templates/${encodeURIComponent(id)}/versions/${versionNumber}`, { method: 'PUT', body: JSON.stringify({ schemaJson, rowVersion }) })
export const publishFormTemplateDraft = (id: string, versionNumber: number, templateRowVersion: string, versionRowVersion: string) =>
  request<FormTemplateVersionDetail>(`/api/form-templates/${encodeURIComponent(id)}/versions/${versionNumber}/publish`, { method: 'POST', body: JSON.stringify({ templateRowVersion, versionRowVersion }) })
export const createFormTemplateVersion = (id: string, schemaJson: string, templateRowVersion: string) =>
  request<FormTemplateVersionDetail>(`/api/form-templates/${encodeURIComponent(id)}/versions`, { method: 'POST', body: JSON.stringify({ schemaJson, templateRowVersion }) })
export const deactivateFormTemplate = (id: string, rowVersion: string) =>
  request<FormTemplateDeactivated>(`/api/form-templates/${encodeURIComponent(id)}/deactivate`, { method: 'POST', body: JSON.stringify({ rowVersion }) })

export type InspectionListParameters = {
  pageNumber: number; pageSize: number; search?: string; status?: InspectionStatus | ''
  constructionObjectId?: string; reviewerUserId?: string
  sortBy?: 'number' | 'title' | 'status' | 'createdAt' | 'updatedAt'; sortDirection?: 'asc' | 'desc'
}
function queryString(parameters: Record<string, string | number | boolean | undefined>) {
  const search = new URLSearchParams()
  Object.entries(parameters).forEach(([key, value]) => { if (value !== undefined && value !== '') search.set(key, String(value)) })
  return search.toString()
}
export const getInspections = (parameters: InspectionListParameters, signal?: AbortSignal) =>
  request<PagedResult<InspectionListItem>>(`/api/inspections?${queryString(parameters)}`, { signal })
export const getInspection = (id: string, signal?: AbortSignal) =>
  request<InspectionDetail>(`/api/inspections/${encodeURIComponent(id)}`, { signal })
export const getInspectionConstructionObjects = (pageNumber: number, pageSize: number, search = '', signal?: AbortSignal) =>
  request<PagedResult<ConstructionObjectListItem>>(`/api/construction-objects?${queryString({ pageNumber, pageSize, isActive: 'true', sortBy: 'name', sortDirection: 'asc', search })}`, { signal })
export const getInspectionFormTemplateOptions = (pageNumber: number, pageSize: number, search = '', signal?: AbortSignal) =>
  request<PagedResult<FormTemplateOption>>(`/api/inspections/form-template-options?${queryString({ pageNumber, pageSize, search })}`, { signal })
export const createInspection = (input: CreateInspectionInput) =>
  request<InspectionDetail>('/api/inspections', { method: 'POST', body: JSON.stringify(input) })
export const saveInspectionDraft = (id: string, input: SaveInspectionDraftInput) =>
  request<InspectionDraft>(`/api/inspections/${encodeURIComponent(id)}/draft`, { method: 'PUT', body: JSON.stringify(input) })
export const getInspectionReviewerOptions = (pageNumber: number, pageSize: number, search = '', signal?: AbortSignal) =>
  request<PagedResult<ReviewerOption>>(`/api/inspections/reviewer-options?${queryString({ pageNumber, pageSize, search })}`, { signal })
export const submitInspection = (id: string, reviewerUserId: string, inspectionRowVersion: string, submissionRowVersion: string, processRowVersion: string) =>
  request<InspectionWorkflowResult>(`/api/inspections/${encodeURIComponent(id)}/submit`, { method: 'POST', body: JSON.stringify({ reviewerUserId, inspectionRowVersion, submissionRowVersion, processRowVersion }) })
export const startInspectionReview = (id: string, processRowVersion: string) =>
  request<InspectionWorkflowResult>(`/api/inspections/${encodeURIComponent(id)}/start-review`, { method: 'POST', body: JSON.stringify({ processRowVersion }) })
export const approveInspection = (id: string, comment: string | null, processRowVersion: string) =>
  request<InspectionWorkflowResult>(`/api/inspections/${encodeURIComponent(id)}/approve`, { method: 'POST', body: JSON.stringify({ comment, processRowVersion }) })
export const rejectInspection = (id: string, reason: string, processRowVersion: string) =>
  request<InspectionWorkflowResult>(`/api/inspections/${encodeURIComponent(id)}/reject`, { method: 'POST', body: JSON.stringify({ reason, processRowVersion }) })
export const requestInspectionRecheck = (id: string, reason: string, processRowVersion: string) =>
  request<InspectionWorkflowResult>(`/api/inspections/${encodeURIComponent(id)}/recheck`, { method: 'POST', body: JSON.stringify({ reason, processRowVersion }) })
export const reassignInspectionReviewer = (id: string, reviewerUserId: string, reason: string, inspectionRowVersion: string, processRowVersion: string) =>
  request<InspectionWorkflowResult>(`/api/inspections/${encodeURIComponent(id)}/reassign-reviewer`, { method: 'POST', body: JSON.stringify({ reviewerUserId, reason, inspectionRowVersion, processRowVersion }) })
export const getInspectionHistory = (id: string, pageNumber: number, pageSize: number, signal?: AbortSignal) =>
  request<PagedResult<ProcessHistory>>(`/api/inspections/${encodeURIComponent(id)}/history?${queryString({ pageNumber, pageSize })}`, { signal })
export const getInspectionRevisions = (id: string, pageNumber: number, pageSize: number, signal?: AbortSignal) =>
  request<PagedResult<InspectionRevisionListItem>>(`/api/inspections/${encodeURIComponent(id)}/revisions?${queryString({ pageNumber, pageSize })}`, { signal })
export const getInspectionRevision = (id: string, revisionNumber: number, signal?: AbortSignal) =>
  request<InspectionRevisionDetail>(`/api/inspections/${encodeURIComponent(id)}/revisions/${revisionNumber}`, { signal })

export type ChecklistListParameters = {
  pageNumber: number; pageSize: number; search?: string; status?: ChecklistStatus | ''
  constructionObjectId?: string; reviewerUserId?: string
  sortBy?: 'number' | 'title' | 'status' | 'createdAt' | 'updatedAt'; sortDirection?: 'asc' | 'desc'
}
export const getChecklists = (parameters: ChecklistListParameters, signal?: AbortSignal) =>
  request<PagedResult<ChecklistListItem>>(`/api/checklists?${queryString(parameters)}`, { signal })
export const getChecklist = (id: string, signal?: AbortSignal) =>
  request<ChecklistDetail>(`/api/checklists/${encodeURIComponent(id)}`, { signal })
export const createChecklist = (input: CreateChecklistInput) =>
  request<ChecklistWorkflowResult>('/api/checklists', { method: 'POST', body: JSON.stringify(input) })
export const startChecklist = (id: string, checklistRowVersion: string, processRowVersion: string) =>
  request<ChecklistWorkflowResult>(`/api/checklists/${encodeURIComponent(id)}/start`, { method: 'POST', body: JSON.stringify({ checklistRowVersion, processRowVersion }) })
export const updateChecklistItems = (id: string, input: UpdateChecklistItemsInput) =>
  request<ChecklistItemsResult>(`/api/checklists/${encodeURIComponent(id)}/items`, { method: 'PUT', body: JSON.stringify(input) })
export const replaceChecklistInspections = (id: string, inspectionIds: string[], checklistRowVersion: string, processRowVersion: string) =>
  request<ChecklistLinksResult>(`/api/checklists/${encodeURIComponent(id)}/inspections`, { method: 'PUT', body: JSON.stringify({ inspectionIds, checklistRowVersion, processRowVersion }) })
export const submitChecklist = (id: string, reviewerUserId: string, checklistRowVersion: string, processRowVersion: string) =>
  request<ChecklistWorkflowResult>(`/api/checklists/${encodeURIComponent(id)}/submit`, { method: 'POST', body: JSON.stringify({ reviewerUserId, checklistRowVersion, processRowVersion }) })
export const startChecklistReview = (id: string, processRowVersion: string) => checklistProcess(id, 'start-review', { processRowVersion })
export const approveChecklist = (id: string, comment: string | null, processRowVersion: string) => checklistProcess(id, 'approve', { comment, processRowVersion })
export const rejectChecklist = (id: string, reason: string, processRowVersion: string) => checklistProcess(id, 'reject', { reason, processRowVersion })
export const requestChecklistRecheck = (id: string, reason: string, processRowVersion: string) => checklistProcess(id, 'recheck', { reason, processRowVersion })
const checklistProcess = (id: string, action: string, body: Record<string, unknown>) =>
  request<ChecklistWorkflowResult>(`/api/checklists/${encodeURIComponent(id)}/${action}`, { method: 'POST', body: JSON.stringify(body) })
export const getChecklistHistory = (id: string, pageNumber: number, pageSize: number, signal?: AbortSignal) =>
  request<PagedResult<ChecklistHistory>>(`/api/checklists/${encodeURIComponent(id)}/history?${queryString({ pageNumber, pageSize })}`, { signal })
export const getChecklistSubmissions = (id: string, pageNumber: number, pageSize: number, signal?: AbortSignal) =>
  request<PagedResult<ChecklistSubmissionListItem>>(`/api/checklists/${encodeURIComponent(id)}/submissions?${queryString({ pageNumber, pageSize })}`, { signal })
export const getChecklistSubmission = (id: string, submissionNumber: number, signal?: AbortSignal) =>
  request<ChecklistSubmissionDetail>(`/api/checklists/${encodeURIComponent(id)}/submissions/${submissionNumber}`, { signal })

export type RfiListParameters = {
  pageNumber: number; pageSize: number; search?: string; status?: RfiStatus | ''
  constructionObjectId?: string; assignedToMe?: boolean; overdue?: boolean
  sortBy?: 'createdAt' | 'updatedAt' | 'number' | 'subject' | 'status' | 'dueAtUtc'
  sortDirection?: 'asc' | 'desc'
}
export const getRfis = (parameters: RfiListParameters, signal?: AbortSignal) =>
  request<PagedResult<RfiListItem>>(`/api/rfis?${queryString({ ...parameters })}`, { signal })
export const getRfi = (id: string, signal?: AbortSignal) =>
  request<RfiDetail>(`/api/rfis/${encodeURIComponent(id)}`, { signal })
export const getRfiResponderOptions = (pageNumber: number, pageSize: number, search = '', signal?: AbortSignal) =>
  request<PagedResult<RfiResponderOption>>(`/api/rfis/responder-options?${queryString({ pageNumber, pageSize, search })}`, { signal })
export const createRfi = (input: CreateRfiInput) =>
  request<RfiWorkflowResult>('/api/rfis', { method: 'POST', body: JSON.stringify(input) })
export const saveRfiDraft = (id: string, subject: string, question: string, rfiRowVersion: string, processRowVersion: string) =>
  rfiMutation(id, 'draft', { subject, question, rfiRowVersion, processRowVersion }, 'PUT')
export const submitRfi = (id: string, responderUserId: string, rfiRowVersion: string, processRowVersion: string) =>
  rfiMutation(id, 'submit', { responderUserId, rfiRowVersion, processRowVersion })
export const reassignRfi = (id: string, responderUserId: string, reason: string, rfiRowVersion: string, processRowVersion: string) =>
  rfiMutation(id, 'assign', { responderUserId, reason, rfiRowVersion, processRowVersion })
export const respondRfi = (id: string, responseText: string, rfiRowVersion: string, processRowVersion: string) =>
  rfiMutation(id, 'responses', { responseText, rfiRowVersion, processRowVersion })
export const requestRfiClarification = (id: string, content: string, rfiRowVersion: string, processRowVersion: string) =>
  rfiMutation(id, 'request-clarification', { content, rfiRowVersion, processRowVersion })
export const closeRfi = (id: string, comment: string | null, rfiRowVersion: string, processRowVersion: string) =>
  rfiMutation(id, 'close', { comment, rfiRowVersion, processRowVersion })
export const cancelRfi = (id: string, reason: string, rfiRowVersion: string, processRowVersion: string) =>
  rfiMutation(id, 'cancel', { reason, rfiRowVersion, processRowVersion })
export const getRfiHistory = (id: string, pageNumber: number, pageSize: number, signal?: AbortSignal) =>
  request<PagedResult<RfiHistory>>(`/api/rfis/${encodeURIComponent(id)}/history?${queryString({ pageNumber, pageSize })}`, { signal })
const rfiMutation = (id: string, action: string, body: Record<string, unknown>, method = 'POST') =>
  request<RfiWorkflowResult>(`/api/rfis/${encodeURIComponent(id)}/${action}`, { method, body: JSON.stringify(body) })

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
