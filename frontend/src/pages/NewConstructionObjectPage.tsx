import { useMutation } from '@tanstack/react-query'
import { useRef, useState } from 'react'
import { Link, Navigate, useNavigate } from 'react-router-dom'
import { createConstructionObject } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { ConstructionObjectForm } from '../components/ConstructionObjectForm'
import { canManageConstructionObjects } from '../constructionObjects/access'
import { constructionObjectErrorMessage } from '../constructionObjects/errors'
import { emptyConstructionObjectForm, toConstructionObjectInput, validateConstructionObject, type ConstructionObjectFormValue } from '../constructionObjects/form'
import { useUnsavedConstructionObject } from '../constructionObjects/useUnsavedConstructionObject'

export function NewConstructionObjectPage() {
  const { user } = useAuth(); const navigate = useNavigate(); const summaryRef = useRef<HTMLDivElement>(null)
  const [value, setValue] = useState<ConstructionObjectFormValue>(emptyConstructionObjectForm)
  const [errors, setErrors] = useState<ReturnType<typeof validateConstructionObject>>({}); const [saved, setSaved] = useState(false)
  const dirty = !saved && JSON.stringify(value) !== JSON.stringify(emptyConstructionObjectForm)
  useUnsavedConstructionObject(dirty)
  const create = useMutation({ mutationFn: () => createConstructionObject(toConstructionObjectInput(value)), onSuccess: detail => { setSaved(true); navigate(`/construction-objects/${detail.id}`, { replace: true }) }, onError: () => window.setTimeout(() => summaryRef.current?.focus(), 0) })
  if (!canManageConstructionObjects(user)) return <Navigate to="/construction-objects" replace />
  const submit = (event: React.FormEvent) => {
    event.preventDefault(); const nextErrors = validateConstructionObject(value); setErrors(nextErrors)
    if (Object.keys(nextErrors).length) { window.setTimeout(() => summaryRef.current?.focus(), 0); return }
    if (!create.isPending) create.mutate()
  }
  return <><Link className="back-link" to="/construction-objects">← Danh sách Construction Object</Link><header className="page-head"><div><p className="kicker">New hierarchy node</p><h1>Tạo Construction Object</h1><p>Code được server normalize; hierarchy và parent được backend kiểm tra trước khi lưu.</p></div></header>
    <form className="construction-object-form" onSubmit={submit} noValidate>
      {(Object.keys(errors).length > 0 || create.error) && <div className="notice error validation-summary" role="alert" tabIndex={-1} ref={summaryRef}>{create.error ? constructionObjectErrorMessage(create.error, 'Không thể tạo Construction Object.') : 'Hãy kiểm tra các trường được đánh dấu bên dưới.'}</div>}
      <ConstructionObjectForm value={value} onChange={setValue} errors={errors} disabled={create.isPending} />
      <div className="form-actions"><Link className="button action-link" to="/construction-objects">Hủy</Link><button className="button primary" type="submit" disabled={create.isPending}>{create.isPending ? 'Đang tạo…' : 'Tạo Construction Object'}</button></div>
    </form></>
}
