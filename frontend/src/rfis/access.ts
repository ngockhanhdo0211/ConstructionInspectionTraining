import type { User } from '../api/types'

export const canViewRfis = (user: User | null) =>
  Boolean(user?.roles.some(role => ['INSPECTOR', 'REVIEWER', 'MANAGER', 'ADMINISTRATOR'].includes(role)))

export const canCreateRfi = (user: User | null) => user?.roles.includes('INSPECTOR') ?? false
export const isRfiOwner = (user: User | null, ownerId: string) => canCreateRfi(user) && user?.id === ownerId
export const isAssignedRfiReviewer = (user: User | null, assigneeId?: string | null) =>
  Boolean(user?.roles.includes('REVIEWER') && assigneeId && user.id === assigneeId)
export const canReassignRfi = (user: User | null) => user?.roles.includes('MANAGER') ?? false
