import type { PagedResult } from './records'
import { apiFetch } from './client'

export interface AuditEvent {
  id: number
  action: string
  entityType: string | null
  entityId: string | null
  metadata: string | null
  userDisplayName: string | null
  createdAt: string
}

export function getAuditEvents(organizationId: number, page: number, pageSize: number): Promise<PagedResult<AuditEvent>> {
  return apiFetch<PagedResult<AuditEvent>>(
    `/api/organizations/${organizationId}/audit-events?page=${page}&pageSize=${pageSize}`,
  )
}
