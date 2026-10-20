import type { ConstructionObjectInput, ConstructionObjectType } from '../api/types'

export const objectTypes: ConstructionObjectType[] = ['Project', 'Building', 'Floor', 'Room', 'WorkItem']
export const parentType: Record<ConstructionObjectType, ConstructionObjectType | null> = {
  Project: null, Building: 'Project', Floor: 'Building', Room: 'Floor', WorkItem: 'Room',
}

export type ConstructionObjectFormValue = {
  parentId: string; code: string; name: string; objectType: ConstructionObjectType
  location: string; description: string
}

export const emptyConstructionObjectForm: ConstructionObjectFormValue = {
  parentId: '', code: '', name: '', objectType: 'Project', location: '', description: '',
}

export function validateConstructionObject(value: ConstructionObjectFormValue) {
  const errors: Partial<Record<keyof ConstructionObjectFormValue, string>> = {}
  const code = value.code.trim()
  if (!code) errors.code = 'Code là bắt buộc.'
  else if (!/^[A-Za-z0-9][A-Za-z0-9._-]{1,99}$/.test(code)) errors.code = "Code gồm 2–100 ký tự Latin, số, '.', '_' hoặc '-'."
  if (!value.name.trim()) errors.name = 'Name là bắt buộc.'
  else if (value.name.trim().length > 250) errors.name = 'Name tối đa 250 ký tự.'
  if (value.location.trim().length > 500) errors.location = 'Location tối đa 500 ký tự.'
  if (value.description.trim().length > 2000) errors.description = 'Description tối đa 2.000 ký tự.'
  if (parentType[value.objectType] && !value.parentId) errors.parentId = `Hãy chọn parent ${parentType[value.objectType]}.`
  if (!parentType[value.objectType] && value.parentId) errors.parentId = 'Project không có parent.'
  return errors
}

export function toConstructionObjectInput(value: ConstructionObjectFormValue): ConstructionObjectInput {
  return {
    parentId: parentType[value.objectType] ? value.parentId || null : null,
    code: value.code.trim(), name: value.name.trim(), objectType: value.objectType,
    location: value.location.trim() || null, description: value.description.trim() || null,
  }
}
