import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { getConstructionObjectPage } from '../api/client'
import type { ConstructionObjectBreadcrumbItem } from '../api/types'
import { constructionObjectErrorMessage } from '../constructionObjects/errors'
import { objectTypes, parentType, type ConstructionObjectFormValue } from '../constructionObjects/form'

type FieldErrors = Partial<Record<keyof ConstructionObjectFormValue, string>>
type Props = {
  value: ConstructionObjectFormValue; onChange(value: ConstructionObjectFormValue): void
  errors: FieldErrors; objectTypeLocked?: boolean; currentId?: string
  currentParent?: ConstructionObjectBreadcrumbItem | null; disabled?: boolean
}

const pageSize = 20

export function ConstructionObjectForm({ value, onChange, errors, objectTypeLocked = false, currentId, currentParent, disabled = false }: Props) {
  const [parentSearchInput, setParentSearchInput] = useState('')
  const [parentSearch, setParentSearch] = useState('')
  const [parentPage, setParentPage] = useState(1)
  const expectedParentType = parentType[value.objectType]
  const parents = useQuery({
    queryKey: ['construction-object-parent-options', value.objectType, parentPage, parentSearch],
    queryFn: ({ signal }) => getConstructionObjectPage({ pageNumber: parentPage, pageSize, search: parentSearch, objectType: expectedParentType ?? '', isActive: 'true', sortBy: 'name', sortDirection: 'asc' }, signal),
    enabled: Boolean(expectedParentType) && !disabled,
    placeholderData: keepPreviousData,
  })
  const options = parents.data?.items.filter(item => item.id !== currentId) ?? []
  const selectedParentMissing = value.parentId && !options.some(item => item.id === value.parentId)
  const set = <K extends keyof ConstructionObjectFormValue>(key: K, next: ConstructionObjectFormValue[K]) => onChange({ ...value, [key]: next })
  return <div className="construction-object-fields">
    <label htmlFor="object-type">Object Type *<select id="object-type" value={value.objectType} disabled={disabled || objectTypeLocked} onChange={event => { const objectType = event.target.value as ConstructionObjectFormValue['objectType']; onChange({ ...value, objectType, parentId: '' }) }}>{objectTypes.map(type => <option key={type} value={type}>{type}</option>)}</select></label>
    {expectedParentType && <section className="parent-picker" aria-labelledby="parent-heading">
      <h3 id="parent-heading">Parent {expectedParentType} *</h3>
      <label htmlFor="parent-search">Tìm parent theo code, name hoặc location<div className="filter-line"><input id="parent-search" maxLength={200} value={parentSearchInput} disabled={disabled} onChange={event => setParentSearchInput(event.target.value)} /><button className="button" type="button" disabled={disabled || parents.isFetching} onClick={() => { setParentPage(1); setParentSearch(parentSearchInput.trim()) }}>Tìm</button></div></label>
      {parents.isLoading && <p role="status">Đang tải parent…</p>}
      {parents.isError && <div className="notice error" role="alert">{constructionObjectErrorMessage(parents.error, 'Không thể tải parent.')} <button className="button" type="button" onClick={() => parents.refetch()}>Thử lại</button></div>}
      <label htmlFor="parent-id">Parent<select id="parent-id" value={value.parentId} disabled={disabled} aria-invalid={Boolean(errors.parentId)} aria-describedby={errors.parentId ? 'parent-id-error' : undefined} onChange={event => set('parentId', event.target.value)}>
        <option value="">Chọn parent {expectedParentType} Active</option>
        {selectedParentMissing && currentParent && <option value={currentParent.id}>{currentParent.code} — {currentParent.name}</option>}
        {options.map(item => <option key={item.id} value={item.id}>{item.code} — {item.name}</option>)}
      </select>{errors.parentId && <span className="field-error" id="parent-id-error">{errors.parentId}</span>}</label>
      {(parents.data?.totalPages ?? 0) > 1 && <div className="pagination compact"><button className="button" type="button" disabled={parentPage <= 1 || parents.isFetching} onClick={() => setParentPage(page => page - 1)}>Parent trước</button><span>{parentPage} / {parents.data?.totalPages}</span><button className="button" type="button" disabled={parentPage >= (parents.data?.totalPages ?? 0) || parents.isFetching} onClick={() => setParentPage(page => page + 1)}>Parent sau</button></div>}
    </section>}
    {!expectedParentType && <p className="read-only-note">Project là root và không có parent.</p>}
    <label htmlFor="object-code">Code *<input id="object-code" required maxLength={100} value={value.code} disabled={disabled} aria-invalid={Boolean(errors.code)} aria-describedby={errors.code ? 'object-code-error' : undefined} onChange={event => set('code', event.target.value)} />{errors.code && <span className="field-error" id="object-code-error">{errors.code}</span>}</label>
    <label htmlFor="object-name">Name *<input id="object-name" required maxLength={250} value={value.name} disabled={disabled} aria-invalid={Boolean(errors.name)} aria-describedby={errors.name ? 'object-name-error' : undefined} onChange={event => set('name', event.target.value)} />{errors.name && <span className="field-error" id="object-name-error">{errors.name}</span>}</label>
    <label htmlFor="object-location">Location<input id="object-location" maxLength={500} value={value.location} disabled={disabled} aria-invalid={Boolean(errors.location)} aria-describedby={errors.location ? 'object-location-error' : undefined} onChange={event => set('location', event.target.value)} />{errors.location && <span className="field-error" id="object-location-error">{errors.location}</span>}</label>
    <label htmlFor="object-description">Description<textarea id="object-description" rows={5} maxLength={2000} value={value.description} disabled={disabled} aria-invalid={Boolean(errors.description)} aria-describedby={errors.description ? 'object-description-error' : undefined} onChange={event => set('description', event.target.value)} />{errors.description && <span className="field-error" id="object-description-error">{errors.description}</span>}</label>
  </div>
}
