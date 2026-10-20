import { beforeEach, describe, expect, it, vi } from 'vitest'
import { configureApi, createConstructionObject, deactivateConstructionObject, getConstructionObject, getConstructionObjectPage, updateConstructionObject } from './client'

const input = { parentId: null, code: 'DEMO-CO-01', name: 'Demo', objectType: 'Project' as const, location: null, description: null }

beforeEach(() => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({}) }))
  configureApi(() => 'opaque-test-token', vi.fn())
})

describe('Construction Object API client', () => {
  it('uses the two GET contracts, forwards cancellation and never places JWT in a URL', async () => {
    const controller = new AbortController()
    await getConstructionObjectPage({ pageNumber: 2, pageSize: 20, search: 'room', isActive: 'false', objectType: 'Room', sortBy: 'name', sortDirection: 'asc' }, controller.signal)
    await getConstructionObject('object/id', controller.signal)
    const calls = vi.mocked(fetch).mock.calls
    expect(String(calls[0][0])).toContain('/api/construction-objects?pageNumber=2&pageSize=20&search=room&isActive=false&objectType=Room&sortBy=name&sortDirection=asc')
    expect(String(calls[1][0])).toContain('/api/construction-objects/object%2Fid')
    expect(calls[0][1]?.signal).toBe(controller.signal)
    expect(calls.every(call => !String(call[0]).includes('opaque-test-token'))).toBe(true)
    expect(calls.every(call => !String(call[0]).toLowerCase().includes('minio'))).toBe(true)
  })

  it('uses the three mutation contracts and sends only contract fields', async () => {
    await createConstructionObject(input)
    await updateConstructionObject('id', { ...input, rowVersion: 'row-version' })
    await deactivateConstructionObject('id', 'row-version-2')
    const calls = vi.mocked(fetch).mock.calls.map(call => ({ url: String(call[0]).replace(/^.*\/api/, '/api'), method: call[1]?.method, body: JSON.parse(String(call[1]?.body)) }))
    expect(calls).toEqual([
      { url: '/api/construction-objects', method: 'POST', body: input },
      { url: '/api/construction-objects/id', method: 'PUT', body: { ...input, rowVersion: 'row-version' } },
      { url: '/api/construction-objects/id/deactivate', method: 'POST', body: { rowVersion: 'row-version-2' } },
    ])
  })
})
