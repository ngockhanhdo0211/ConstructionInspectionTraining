import type { User } from '../api/types'

export function canCreateInspection(user: User | null) {
  return user?.roles.includes('INSPECTOR') ?? false
}

export function canEditInspection(user: User | null, createdByUserId: string, status: string) {
  return canCreateInspection(user) && user?.id === createdByUserId && (status === 'Draft' || status === 'Recheck')
}

export const isAssignedReviewer = (user: User | null, reviewerUserId?: string | null) =>
  Boolean(user?.roles.includes('REVIEWER') && reviewerUserId && user.id === reviewerUserId)

export const canReassignInspection = (user: User | null, status: string) =>
  Boolean(user?.roles.some(role => role === 'MANAGER' || role === 'ADMINISTRATOR') && (status === 'Submitted' || status === 'UnderReview'))
