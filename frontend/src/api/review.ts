import { apiFetch } from './client'
import type { PagedResult } from './records'

export interface GeocodeCandidate {
  matchedAddress: string | null
  latitude: number
  longitude: number
  stateFips: string | null
  countyFips: string | null
  tract: string | null
  blockGroup: string | null
  block: string | null
  geoid: string | null
}

export interface ReviewRecord {
  id: number
  externalId: string | null
  addressLine1: string
  addressLine2: string | null
  city: string
  state: string
  postalCode: string | null
  normalizedAddress: string | null
  matchedAddress: string | null
  geocodeStatus: string
  matchQuality: string | null
  geocoderSource: string | null
  latitude: number | null
  longitude: number | null
  candidates: GeocodeCandidate[]
  geocodedAt: string | null
}

export function getReviewQueue(
  organizationId: number,
  datasetId: number,
  params: { page?: number; pageSize?: number } = {},
): Promise<PagedResult<ReviewRecord>> {
  const query = new URLSearchParams()
  if (params.page) query.set('page', String(params.page))
  if (params.pageSize) query.set('pageSize', String(params.pageSize))
  const queryString = query.toString()
  return apiFetch<PagedResult<ReviewRecord>>(
    `/api/organizations/${organizationId}/datasets/${datasetId}/records/review${queryString ? `?${queryString}` : ''}`,
  )
}

export function getReviewRecord(organizationId: number, datasetId: number, recordId: number): Promise<ReviewRecord> {
  return apiFetch<ReviewRecord>(`/api/organizations/${organizationId}/datasets/${datasetId}/records/review/${recordId}`)
}

export function acceptCandidate(
  organizationId: number,
  datasetId: number,
  recordId: number,
  candidateIndex: number,
): Promise<ReviewRecord> {
  return apiFetch<ReviewRecord>(
    `/api/organizations/${organizationId}/datasets/${datasetId}/records/review/${recordId}/accept-candidate`,
    { method: 'POST', body: JSON.stringify({ candidateIndex }) },
  )
}

export interface CorrectAddressInput {
  addressLine1: string
  addressLine2?: string | null
  city: string
  state: string
  postalCode?: string | null
}

export function correctAddress(
  organizationId: number,
  datasetId: number,
  recordId: number,
  input: CorrectAddressInput,
): Promise<ReviewRecord> {
  return apiFetch<ReviewRecord>(
    `/api/organizations/${organizationId}/datasets/${datasetId}/records/review/${recordId}/correct-address`,
    { method: 'POST', body: JSON.stringify(input) },
  )
}

export function setCoordinates(
  organizationId: number,
  datasetId: number,
  recordId: number,
  latitude: number,
  longitude: number,
): Promise<ReviewRecord> {
  return apiFetch<ReviewRecord>(
    `/api/organizations/${organizationId}/datasets/${datasetId}/records/review/${recordId}/coordinates`,
    { method: 'POST', body: JSON.stringify({ latitude, longitude }) },
  )
}

export function dismissRecord(
  organizationId: number,
  datasetId: number,
  recordId: number,
  note?: string,
): Promise<ReviewRecord> {
  return apiFetch<ReviewRecord>(
    `/api/organizations/${organizationId}/datasets/${datasetId}/records/review/${recordId}/dismiss`,
    { method: 'POST', body: JSON.stringify({ note: note ?? null }) },
  )
}
