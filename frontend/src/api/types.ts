export type User = { id: string; email: string; fullName: string; roles: string[] }
export type LoginResponse = { accessToken: string; tokenType: string; expiresAt: string; user: User }
export type InspectionDashboardStatusCounts = {
  draft: number; submitted: number; underReview: number; approved: number; recheck: number; rejected: number
}
export type ChecklistDashboardStatusCounts = InspectionDashboardStatusCounts & { inProgress: number }
export type RfiDashboardStatusCounts = { draft: number; open: number; responded: number; closed: number; cancelled: number }
export type DashboardModuleSummary<TStatusCounts> = {
  total: number; openWork: number; awaitingMyAction: number; statusCounts: TStatusCounts
}
export type DashboardRfiSummary = DashboardModuleSummary<RfiDashboardStatusCounts> & {
  awaitingResponse: number; awaitingInspectorAction: number; overdueOpen: number
}
export type DashboardActionItem = {
  module: 'Inspection' | 'Checklist' | 'Rfi'; entityId: string; number: string; displayTitle: string
  status: string; actionKind: string; dueAtUtc?: string | null; updatedAtUtc: string
  constructionObject: { id: string; code: string; name: string }; isOverdue: boolean
}
export type DashboardResponse = {
  generatedAtUtc: string; roles: string[]; scopeMode: 'Full' | 'OwnAndAssigned' | 'Own' | 'Assigned'
  inspectionSummary: DashboardModuleSummary<InspectionDashboardStatusCounts>
  checklistSummary: DashboardModuleSummary<ChecklistDashboardStatusCounts>
  rfiSummary: DashboardRfiSummary
  actionQueue: DashboardActionItem[]
}
export type Drawing = { id: string; originalFileName: string; contentType?: string; fileSize: number; sha256?: string; pageCount: number; status: 'Uploading' | 'Ready' | 'Failed'; createdAt: string; createdByUserId: string; rowVersion?: string }
export type PagedResult<T> = { items: T[]; pageNumber: number; pageSize: number; totalCount: number; totalPages: number }
export type UploadResponse = { drawing: Drawing; duplicateWarning: boolean }
export type Problem = { title?: string; detail?: string; status?: number; code?: string; traceId?: string; errors?: Record<string, string[]> }
export type ConstructionObjectType = 'Project' | 'Building' | 'Floor' | 'Room' | 'WorkItem'
export type ConstructionObjectListItem = { id: string; parentId: string | null; code: string; name: string; objectType: ConstructionObjectType; location?: string | null; isActive: boolean; rowVersion: string }
export type ConstructionObjectBreadcrumbItem = { id: string; code: string; name: string; objectType: ConstructionObjectType }
export type ConstructionObjectDetail = {
  id: string; parentId: string | null; code: string; name: string; objectType: ConstructionObjectType
  location?: string | null; description?: string | null; isActive: boolean
  parent?: ConstructionObjectBreadcrumbItem | null; breadcrumb: ConstructionObjectBreadcrumbItem[]
  hasChildren: boolean; directChildrenCount: number; createdByUserId: string; createdAt: string
  updatedByUserId?: string | null; updatedAt?: string | null; rowVersion: string
}
export type ConstructionObjectInput = {
  parentId: string | null; code: string; name: string; objectType: ConstructionObjectType
  location?: string | null; description?: string | null
}
export type UpdateConstructionObjectInput = ConstructionObjectInput & { rowVersion: string }
export type DeactivatedConstructionObject = {
  id: string; isActive: false; updatedByUserId: string; updatedAt: string; rowVersion: string
}
export type DrawingRectangle = { left: number; bottom: number; right: number; top: number }
export type MappingConstructionObject = { id: string; code: string; name: string; objectType: ConstructionObjectType; isActive: boolean }
export type DrawingObjectMapping = {
  id: string; drawingDocumentId: string; pageNumber: number; constructionObject: MappingConstructionObject
  shapeType: 'Rectangle'; geometry: DrawingRectangle; status: 'Active' | 'Inactive'
  createdAt: string; createdByUserId: string; updatedAt?: string | null; updatedByUserId?: string | null
  deactivatedAt?: string | null; deactivatedByUserId?: string | null; rowVersion: string
}
export type DrawingPageMappings = { drawingId: string; pageNumber: number; items: DrawingObjectMapping[] }
export type CreateConstructionObjectInput = ConstructionObjectInput

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
export type ReviewerOption = { userId: string; displayName: string }
export type InspectionWorkflowResult = {
  inspectionId: string; status: InspectionStatus; reviewerUserId?: string | null
  currentRevisionNumber: number; completedAt?: string | null
  inspectionRowVersion: string; submissionRowVersion: string; processRowVersion: string
}
export type ProcessHistory = {
  id: string; action: string; fromStatus?: InspectionStatus | null; toStatus: InspectionStatus
  actorUserId: string; actorDisplayName: string
  previousReviewerUserId?: string | null; previousReviewerDisplayName?: string | null
  newReviewerUserId?: string | null; newReviewerDisplayName?: string | null
  reason?: string | null; createdAt: string; submissionId?: string | null
}
export type InspectionRevisionListItem = {
  id: string; revisionNumber: number; createdByUserId: string; createdByDisplayName: string
  createdAt: string; updatedAt?: string | null; submittedAt?: string | null
  isImmutable: boolean; isCurrent: boolean; rowVersion: string
}
export type InspectionRevisionDetail = {
  inspectionId: string; id: string; revisionNumber: number; dataJson: string
  createdByUserId: string; createdByDisplayName: string; createdAt: string
  updatedAt?: string | null; submittedAt?: string | null; isImmutable: boolean
  isCurrent: boolean; rowVersion: string; template: InspectionTemplate
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

export type ChecklistStatus = 'Draft' | 'InProgress' | 'Submitted' | 'UnderReview' | 'Approved' | 'Recheck' | 'Rejected'
export type ChecklistItemResult = 'NotChecked' | 'Pass' | 'Fail' | 'NotApplicable'
export type ChecklistListItem = {
  id: string; number: string; title: string; constructionObjectId: string
  constructionObjectCode: string; constructionObjectName: string; status: ChecklistStatus
  createdByUserId: string; reviewerUserId?: string | null; itemCount: number
  passCount: number; failCount: number; notApplicableCount: number; notCheckedCount: number
  createdAt: string; updatedAt?: string | null
}
export type ChecklistItem = {
  id: string; sequence: number; criterion: string; isRequired: boolean
  result: ChecklistItemResult; note?: string | null; rowVersion: string
}
export type ChecklistInspection = { id: string; number: string; title: string }
export type ChecklistDetail = {
  id: string; number: string; constructionObjectId: string; constructionObjectCode: string
  constructionObjectName: string; title: string; description?: string | null; status: ChecklistStatus
  createdByUserId: string; reviewerUserId?: string | null; currentSubmissionId?: string | null
  nextSubmissionNumber: number; createdAt: string; updatedAt?: string | null
  items: ChecklistItem[]; inspections: ChecklistInspection[]
  checklistRowVersion: string; processRowVersion: string
}
export type ChecklistWorkflowResult = {
  checklistId: string; status: ChecklistStatus; reviewerUserId?: string | null
  currentSubmissionId?: string | null; submissionNumber?: number | null; completedAt?: string | null
  checklistRowVersion: string; processRowVersion: string
}
export type ChecklistItemsResult = {
  items: ChecklistItem[]; checklistRowVersion: string; processRowVersion: string
}
export type ChecklistLinksResult = {
  inspections: ChecklistInspection[]; checklistRowVersion: string; processRowVersion: string
}
export type ChecklistHistory = {
  id: string; action: string; fromStatus?: ChecklistStatus | null; toStatus: ChecklistStatus
  actorUserId: string; actorDisplayName: string; reason?: string | null; createdAt: string
  checklistSubmissionId?: string | null; newReviewerUserId?: string | null
}
export type ChecklistSubmissionListItem = {
  id: string; submissionNumber: number; submittedByUserId: string; submittedByDisplayName: string
  submittedAt: string; itemCount: number; inspectionCount: number; isCurrent: boolean
}
export type ChecklistSubmissionItem = {
  sourceChecklistItemId: string; sequence: number; criterion: string; isRequired: boolean
  result: ChecklistItemResult; note?: string | null
}
export type ChecklistSubmissionDetail = {
  id: string; submissionNumber: number; submittedByUserId: string; submittedByDisplayName: string
  submittedAt: string; items: ChecklistSubmissionItem[]; inspections: ChecklistInspection[]; isCurrent: boolean
}
export type CreateChecklistInput = {
  constructionObjectId: string; title: string; description?: string | null
  items: Array<{ criterion: string; isRequired: boolean }>
}
export type UpdateChecklistItemsInput = {
  checklistRowVersion: string; processRowVersion: string
  items: Array<{ id?: string | null; rowVersion?: string | null; criterion: string; isRequired: boolean; result: ChecklistItemResult; note?: string | null }>
}

export type RfiStatus = 'Draft' | 'Open' | 'Responded' | 'Closed' | 'Cancelled'
export type RfiEntryType = 'Response' | 'ClarificationRequest'
export type RfiListItem = {
  id: string; number: string; subject: string; constructionObjectId: string
  constructionObjectCode: string; constructionObjectName: string; status: RfiStatus
  createdByUserId: string; assignedToUserId?: string | null; assignedToDisplayName?: string | null
  dueAtUtc?: string | null; isOverdue: boolean; createdAt: string; updatedAt?: string | null
  rfiRowVersion: string; processRowVersion: string
}
export type RfiEntry = {
  id: string; entryNumber: number; entryType: RfiEntryType; content: string
  createdByUserId: string; createdByDisplayName: string; createdAtUtc: string
}
export type RfiDetail = {
  id: string; number: string; constructionObjectId: string; constructionObjectCode: string
  constructionObjectName: string; subject: string; question: string; status: RfiStatus
  createdByUserId: string; assignedToUserId?: string | null; assignedToDisplayName?: string | null
  dueAtUtc?: string | null; completedAt?: string | null; createdAt: string; updatedAt?: string | null
  entries: RfiEntry[]; rfiRowVersion: string; processRowVersion: string
}
export type RfiWorkflowResult = {
  rfiId: string; status: RfiStatus; assignedToUserId?: string | null; dueAtUtc?: string | null
  completedAt?: string | null; rfiRowVersion: string; processRowVersion: string
  createdEntry?: RfiEntry | null
}
export type RfiHistory = {
  id: string; action: string; fromStatus?: RfiStatus | null; toStatus: RfiStatus
  actorUserId: string; actorDisplayName: string; reason?: string | null; performedAt: string
  rfiEntryId?: string | null; previousAssignedToUserId?: string | null; newAssignedToUserId?: string | null
}
export type RfiResponderOption = { userId: string; displayName: string }
export type CreateRfiInput = { constructionObjectId: string; subject: string; question: string }
