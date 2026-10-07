import type { RecordFilters } from './recordFilters'
import type { ChoroplethMetric } from './tracts'
import { apiFetch } from './client'

export interface MapConfiguration {
  showTracts: boolean
  choroplethMetric: ChoroplethMetric | null
  centerLng?: number | null
  centerLat?: number | null
  zoom?: number | null
}

export interface SavedView {
  id: number
  name: string
  userId: number
  ownerDisplayName: string
  createdAt: string
  updatedAt: string
  filters: RecordFilters | null
  mapConfiguration: MapConfiguration | null
}

export interface CreateSavedViewInput {
  name: string
  filters: RecordFilters | null
  mapConfiguration: MapConfiguration | null
}

export function getSavedViews(organizationId: number, datasetId: number): Promise<SavedView[]> {
  return apiFetch<SavedView[]>(`/api/organizations/${organizationId}/datasets/${datasetId}/saved-views`)
}

export function createSavedView(organizationId: number, datasetId: number, input: CreateSavedViewInput): Promise<SavedView> {
  return apiFetch<SavedView>(`/api/organizations/${organizationId}/datasets/${datasetId}/saved-views`, {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function renameSavedView(organizationId: number, datasetId: number, savedViewId: number, name: string): Promise<SavedView> {
  return apiFetch<SavedView>(`/api/organizations/${organizationId}/datasets/${datasetId}/saved-views/${savedViewId}`, {
    method: 'PUT',
    body: JSON.stringify({ name }),
  })
}

export function deleteSavedView(organizationId: number, datasetId: number, savedViewId: number): Promise<boolean> {
  return apiFetch<boolean>(`/api/organizations/${organizationId}/datasets/${datasetId}/saved-views/${savedViewId}`, {
    method: 'DELETE',
  })
}
