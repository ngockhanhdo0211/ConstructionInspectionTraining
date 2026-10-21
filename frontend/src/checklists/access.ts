import type { User } from '../api/types'

export const canViewChecklists = (user: User | null) =>
  Boolean(user?.roles.some(role => ['INSPECTOR', 'REVIEWER', 'MANAGER', 'ADMINISTRATOR'].includes(role)))

export const canCreateChecklist = (user: User | null) => user?.roles.includes('INSPECTOR') ?? false

export const isChecklistOwner = (user: User | null, createdByUserId: string) =>
  Boolean(canCreateChecklist(user) && user?.id === createdByUserId)

export const isChecklistReviewer = (user: User | null, reviewerUserId?: string | null) =>
  Boolean(user?.roles.includes('REVIEWER') && reviewerUserId && user.id === reviewerUserId)
