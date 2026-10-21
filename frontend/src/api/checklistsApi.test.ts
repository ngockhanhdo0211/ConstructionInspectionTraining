import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { approveChecklist, configureApi, createChecklist, getChecklist, getChecklistHistory, getChecklistSubmission, getChecklistSubmissions, getChecklists, rejectChecklist, replaceChecklistInspections, requestChecklistRecheck, startChecklist, startChecklistReview, submitChecklist, updateChecklistItems } from './client'

describe('Checklist API client', () => {
  beforeEach(() => {
    configureApi(() => 'opaque-test-token', vi.fn())
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: () => Promise.resolve({}) }))
  })
  afterEach(() => vi.unstubAllGlobals())

  it('uses the five read contracts, forwards cancellation and never places JWT in the URL', async () => {
    const controller = new AbortController()
    await getChecklists({ pageNumber: 2, pageSize: 20, search: 'CHK 1', status: 'InProgress', constructionObjectId: 'object-id', reviewerUserId: 'reviewer-id', sortBy: 'createdAt', sortDirection: 'desc' }, controller.signal)
    await getChecklist('a/b', controller.signal); await getChecklistHistory('a/b', 2, 10, controller.signal); await getChecklistSubmissions('a/b', 3, 10, controller.signal); await getChecklistSubmission('a/b', 4, controller.signal)
    const calls = vi.mocked(fetch).mock.calls
    expect(calls.map(call => String(call[0]).replace(/^.*\/api/, '/api'))).toEqual([
      '/api/checklists?pageNumber=2&pageSize=20&search=CHK+1&status=InProgress&constructionObjectId=object-id&reviewerUserId=reviewer-id&sortBy=createdAt&sortDirection=desc',
      '/api/checklists/a%2Fb', '/api/checklists/a%2Fb/history?pageNumber=2&pageSize=10', '/api/checklists/a%2Fb/submissions?pageNumber=3&pageSize=10', '/api/checklists/a%2Fb/submissions/4',
    ])
    expect(calls.every(call => call[1]?.signal === controller.signal)).toBe(true)
    expect(calls.every(call => (call[1]?.headers as Record<string, string> | undefined)?.Authorization === 'Bearer opaque-test-token')).toBe(true)
    expect(calls.every(call => !String(call[0]).includes('opaque-test-token'))).toBe(true)
  })

  it('uses all nine mutation contracts and exact concurrency payloads', async () => {
    await createChecklist({ constructionObjectId: 'o1', title: 'Safety', description: null, items: [{ criterion: 'Door', isRequired: true }] })
    await startChecklist('c1', 'c-rv', 'p-rv')
    await updateChecklistItems('c1', { checklistRowVersion: 'c-rv', processRowVersion: 'p-rv', items: [{ id: 'item-1', rowVersion: 'i-rv', criterion: 'Door', isRequired: true, result: 'Pass', note: null }] })
    await replaceChecklistInspections('c1', ['i1'], 'c-rv', 'p-rv')
    await submitChecklist('c1', 'r1', 'c-rv', 'p-rv')
    await startChecklistReview('c1', 'p-rv'); await approveChecklist('c1', 'ok', 'p-rv'); await rejectChecklist('c1', 'bad', 'p-rv'); await requestChecklistRecheck('c1', 'fix', 'p-rv')
    const calls = vi.mocked(fetch).mock.calls.map(call => ({ path: String(call[0]).replace(/^.*\/api/, '/api'), method: call[1]?.method, body: JSON.parse(String(call[1]?.body)) }))
    expect(calls).toEqual([
      { path: '/api/checklists', method: 'POST', body: { constructionObjectId: 'o1', title: 'Safety', description: null, items: [{ criterion: 'Door', isRequired: true }] } },
      { path: '/api/checklists/c1/start', method: 'POST', body: { checklistRowVersion: 'c-rv', processRowVersion: 'p-rv' } },
      { path: '/api/checklists/c1/items', method: 'PUT', body: { checklistRowVersion: 'c-rv', processRowVersion: 'p-rv', items: [{ id: 'item-1', rowVersion: 'i-rv', criterion: 'Door', isRequired: true, result: 'Pass', note: null }] } },
      { path: '/api/checklists/c1/inspections', method: 'PUT', body: { inspectionIds: ['i1'], checklistRowVersion: 'c-rv', processRowVersion: 'p-rv' } },
      { path: '/api/checklists/c1/submit', method: 'POST', body: { reviewerUserId: 'r1', checklistRowVersion: 'c-rv', processRowVersion: 'p-rv' } },
      { path: '/api/checklists/c1/start-review', method: 'POST', body: { processRowVersion: 'p-rv' } },
      { path: '/api/checklists/c1/approve', method: 'POST', body: { comment: 'ok', processRowVersion: 'p-rv' } },
      { path: '/api/checklists/c1/reject', method: 'POST', body: { reason: 'bad', processRowVersion: 'p-rv' } },
      { path: '/api/checklists/c1/recheck', method: 'POST', body: { reason: 'fix', processRowVersion: 'p-rv' } },
    ])
  })
})
