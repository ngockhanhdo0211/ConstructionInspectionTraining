import { render, waitFor } from '@testing-library/react'
import { useEffect } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { getDrawings } from '../api/client'
import { AuthProvider } from './AuthContext'

const storageKey = 'construction-inspection.auth'

describe('AuthProvider API configuration', () => {
  afterEach(() => {
    sessionStorage.clear()
    vi.unstubAllGlobals()
  })

  it('makes the restored token available before child effects request data', async () => {
    sessionStorage.setItem(storageKey, JSON.stringify({
      token: 'test-token',
      user: { id: '1', email: 'manager@local.test', fullName: 'Manager', roles: ['MANAGER'] },
    }))
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      items: [], pageNumber: 1, pageSize: 50, totalCount: 0, totalPages: 0,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    function ChildRequest() {
      useEffect(() => { void getDrawings() }, [])
      return null
    }

    render(<AuthProvider><ChildRequest /></AuthProvider>)

    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    const init = fetchMock.mock.calls[0][1] as RequestInit
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer test-token')
  })
})
