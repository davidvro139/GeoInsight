import { apiFetch } from './client'

export type OrganizationRole = 'Administrator' | 'Analyst' | 'Viewer'

export interface OrganizationSummary {
  id: number
  name: string
  myRole: OrganizationRole
  memberCount: number
}

export interface OrganizationDetail {
  id: number
  name: string
  createdAt: string
  myRole: OrganizationRole
  hasCensusApiKey: boolean
}

export interface OrganizationMember {
  userId: number
  email: string
  displayName: string
  role: OrganizationRole
  joinedAt: string
}

export function createOrganization(name: string): Promise<OrganizationDetail> {
  return apiFetch<OrganizationDetail>('/api/organizations', {
    method: 'POST',
    body: JSON.stringify({ name }),
  })
}

export function getMyOrganizations(): Promise<OrganizationSummary[]> {
  return apiFetch<OrganizationSummary[]>('/api/organizations')
}

export function getOrganization(organizationId: number): Promise<OrganizationDetail> {
  return apiFetch<OrganizationDetail>(`/api/organizations/${organizationId}`)
}

export function getOrganizationMembers(organizationId: number): Promise<OrganizationMember[]> {
  return apiFetch<OrganizationMember[]>(`/api/organizations/${organizationId}/members`)
}

export function addOrganizationMember(
  organizationId: number,
  payload: { email: string; role: OrganizationRole },
): Promise<OrganizationMember> {
  return apiFetch<OrganizationMember>(`/api/organizations/${organizationId}/members`, {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export function changeOrganizationMemberRole(
  organizationId: number,
  userId: number,
  role: OrganizationRole,
): Promise<OrganizationMember> {
  return apiFetch<OrganizationMember>(`/api/organizations/${organizationId}/members/${userId}`, {
    method: 'PATCH',
    body: JSON.stringify({ role }),
  })
}

export function removeOrganizationMember(organizationId: number, userId: number): Promise<boolean> {
  return apiFetch<boolean>(`/api/organizations/${organizationId}/members/${userId}`, {
    method: 'DELETE',
  })
}

export function setCensusApiKey(organizationId: number, apiKey: string | null): Promise<boolean> {
  return apiFetch<boolean>(`/api/organizations/${organizationId}/census-api-key`, {
    method: 'PUT',
    body: JSON.stringify({ apiKey }),
  })
}
