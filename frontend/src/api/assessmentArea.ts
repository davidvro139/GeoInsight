import { apiFetch } from './client'

export interface AssessmentAreaCounty {
  stateFips: string
  countyFips: string
}

export function getAssessmentAreaCounties(organizationId: number): Promise<AssessmentAreaCounty[]> {
  return apiFetch<AssessmentAreaCounty[]>(`/api/organizations/${organizationId}/assessment-area/counties`)
}

export function addAssessmentAreaCounty(organizationId: number, stateFips: string, countyFips: string): Promise<boolean> {
  return apiFetch<boolean>(`/api/organizations/${organizationId}/assessment-area/counties`, {
    method: 'POST',
    body: JSON.stringify({ stateFips, countyFips }),
  })
}

export function removeAssessmentAreaCounty(organizationId: number, stateFips: string, countyFips: string): Promise<boolean> {
  return apiFetch<boolean>(`/api/organizations/${organizationId}/assessment-area/counties/${stateFips}/${countyFips}`, {
    method: 'DELETE',
  })
}
