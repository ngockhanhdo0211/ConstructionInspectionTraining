import type { User } from '../api/types'

export function canCreateInspection(user: User | null) {
  return user?.roles.includes('INSPECTOR') ?? false
}

export function canEditInspection(user: User | null, createdByUserId: string, status: string) {
  return canCreateInspection(user) && user?.id === createdByUserId && status === 'Draft'
}
