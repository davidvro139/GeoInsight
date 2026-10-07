import { apiFetch } from './client'
import type { TractBoundary } from './tracts'

export interface ImportedCounty {
  stateFips: string
  countyFips: string
  tractCount: number
}

export function requestCountyImport(organizationId: number, stateFips: string, countyFips: string): Promise<boolean> {
  return apiFetch<boolean>(`/api/organizations/${organizationId}/census-data/import`, {
    method: 'POST',
    body: JSON.stringify({ stateFips, countyFips }),
  })
}

export function getImportedCounties(organizationId: number): Promise<ImportedCounty[]> {
  return apiFetch<ImportedCounty[]>(`/api/organizations/${organizationId}/census-data/imported-counties`)
}

export function getCountyBoundaries(organizationId: number, stateFips: string, countyFips: string): Promise<TractBoundary[]> {
  return apiFetch<TractBoundary[]>(`/api/organizations/${organizationId}/census-data/counties/${stateFips}/${countyFips}/boundaries`)
}
