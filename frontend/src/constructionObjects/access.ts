import type { User } from '../api/types'

export function canManageConstructionObjects(user: User | null | undefined) {
  return user?.roles.includes('ADMINISTRATOR') ?? false
}

export function canViewConstructionObjectManagement(user: User | null | undefined) {
  return user?.roles.some(role => role === 'ADMINISTRATOR' || role === 'MANAGER') ?? false
}
