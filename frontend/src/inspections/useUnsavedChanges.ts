import { useEffect } from 'react'

const message = 'Bạn có thay đổi Draft chưa lưu. Rời trang sẽ làm mất các thay đổi này.'

export function useUnsavedChanges(dirty: boolean) {
  useEffect(() => {
    if (!dirty) return
    const beforeUnload = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = '' }
    const click = (event: MouseEvent) => {
      const link = (event.target as Element | null)?.closest('a[href]') as HTMLAnchorElement | null
      if (!link || link.target === '_blank' || link.href === window.location.href) return
      if (!window.confirm(message)) { event.preventDefault(); event.stopPropagation() }
    }
    const popState = () => { if (!window.confirm(message)) window.history.go(1) }
    window.addEventListener('beforeunload', beforeUnload)
    document.addEventListener('click', click, true)
    window.addEventListener('popstate', popState)
    return () => {
      window.removeEventListener('beforeunload', beforeUnload)
      document.removeEventListener('click', click, true)
      window.removeEventListener('popstate', popState)
    }
  }, [dirty])
}
