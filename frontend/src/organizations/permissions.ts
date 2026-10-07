import type { OrganizationRole } from '@/api/organizations'

// Mirrors the backend's OrganizationRoleExtensions.MeetsMinimum rank —
// deliberately independent of any enum/array declaration order.
const RANK: Record<OrganizationRole, number> = {
  Viewer: 1,
  Analyst: 2,
  Administrator: 3,
}

export function meetsMinimumRole(role: OrganizationRole, minimum: OrganizationRole): boolean {
  return RANK[role] >= RANK[minimum]
}

export function canManageDatasets(role: OrganizationRole): boolean {
  return meetsMinimumRole(role, 'Analyst')
}
