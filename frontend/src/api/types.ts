export type User = { id: string; email: string; fullName: string; roles: string[] }
export type LoginResponse = { accessToken: string; tokenType: string; expiresAt: string; user: User }
export type Drawing = { id: string; originalFileName: string; contentType?: string; fileSize: number; sha256?: string; pageCount: number; status: 'Uploading' | 'Ready' | 'Failed'; createdAt: string; createdByUserId: string; rowVersion?: string }
export type PagedResult<T> = { items: T[]; pageNumber: number; pageSize: number; totalCount: number; totalPages: number }
export type UploadResponse = { drawing: Drawing; duplicateWarning: boolean }
export type Problem = { title?: string; detail?: string; status?: number; code?: string; traceId?: string; errors?: Record<string, string[]> }
export type ConstructionObjectType = 'Project' | 'Building' | 'Floor' | 'Room' | 'WorkItem'
export type ConstructionObjectListItem = { id: string; parentId: string | null; code: string; name: string; objectType: ConstructionObjectType; location?: string | null; isActive: boolean; rowVersion: string }
export type DrawingRectangle = { left: number; bottom: number; right: number; top: number }
export type MappingConstructionObject = { id: string; code: string; name: string; objectType: ConstructionObjectType; isActive: boolean }
export type DrawingObjectMapping = {
  id: string; drawingDocumentId: string; pageNumber: number; constructionObject: MappingConstructionObject
  shapeType: 'Rectangle'; geometry: DrawingRectangle; status: 'Active' | 'Inactive'
  createdAt: string; createdByUserId: string; updatedAt?: string | null; updatedByUserId?: string | null
  deactivatedAt?: string | null; deactivatedByUserId?: string | null; rowVersion: string
}
export type DrawingPageMappings = { drawingId: string; pageNumber: number; items: DrawingObjectMapping[] }
export type CreateConstructionObjectInput = {
  parentId: string | null; code: string; name: string; objectType: ConstructionObjectType
  location?: string | null; description?: string | null
}

export type FormTemplateStatus = 'Active' | 'Inactive'
export type FormTemplateVersionStatus = 'Draft' | 'Published'
export type FormTemplateListItem = {
  id: string; code: string; name: string; description?: string | null; status: FormTemplateStatus
  latestPublishedVersionNumber?: number | null; draftVersionNumber?: number | null; rowVersion: string
}
export type FormTemplateHeader = FormTemplateListItem & { nextVersionNumber: number }
export type FormTemplateVersionMetadata = {
  id: string; versionNumber: number; status: FormTemplateVersionStatus; createdAt: string; createdByUserId: string
  updatedAt?: string | null; updatedByUserId?: string | null; publishedAt?: string | null; publishedByUserId?: string | null
  isReferenced: boolean; rowVersion: string
}
export type FormTemplateVersions = { template: FormTemplateHeader; versions: PagedResult<FormTemplateVersionMetadata> }
export type FormTemplateVersionDetail = Omit<FormTemplateVersionMetadata, 'isReferenced'> & {
  template: FormTemplateHeader; schemaJson: string; schemaUtf8ByteCount: number
}
export type FormTemplateCreated = { template: FormTemplateHeader; version: FormTemplateVersionDetail }
export type FormTemplateDeactivated = {
  id: string; status: 'Inactive'; updatedAt?: string | null; updatedByUserId?: string | null
  deactivatedAt: string; deactivatedByUserId: string; rowVersion: string
}

export type InspectionStatus = 'Draft' | 'Submitted' | 'UnderReview' | 'Approved' | 'Recheck' | 'Rejected'
export type InspectionListItem = {
  id: string; number: string; title: string; constructionObjectId: string
  formTemplateVersionId: string; formTemplateVersionNumber: number
  status: InspectionStatus; createdByUserId: string; reviewerUserId?: string | null
  createdAt: string; updatedAt?: string | null; rowVersion: string
}
export type InspectionSubmission = {
  id: string; revisionNumber: number; dataJson: string; createdAt: string
  updatedAt?: string | null; submittedAt?: string | null; rowVersion: string
}
export type InspectionTemplate = {
  id: string; code: string; name: string; versionId: string; versionNumber: number; schemaJson: string
}
export type InspectionDetail = {
  id: string; number: string; constructionObjectId: string; constructionObjectCode: string
  constructionObjectName: string; title: string; description?: string | null
  template: InspectionTemplate; status: InspectionStatus; createdByUserId: string
  reviewerUserId?: string | null; createdAt: string; updatedAt?: string | null
  currentRevision: InspectionSubmission; rowVersion: string; processRowVersion: string
}
export type FormTemplateOption = {
  formTemplateId: string; code: string; name: string; latestPublishedVersionNumber: number
}
export type CreateInspectionInput = {
  constructionObjectId: string; formTemplateId: string; title: string
  description?: string | null; data: Record<string, unknown>
}
export type SaveInspectionDraftInput = {
  data: Record<string, unknown>; inspectionRowVersion: string
  submissionRowVersion: string; processRowVersion: string
}
export type InspectionDraft = {
  id: string; revisionNumber: number; dataJson: string; updatedAt: string
  inspectionRowVersion: string; submissionRowVersion: string; processRowVersion: string
}
