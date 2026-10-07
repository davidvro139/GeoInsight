import { appendFilterParams, type RecordFilters } from './recordFilters'
import { apiFetch } from './client'

export type ChoroplethMetric =
  | 'Population'
  | 'MedianHouseholdIncome'
  | 'PovertyRate'
  | 'MinorityPopulationPercent'
  | 'RecordCount'
  | 'TotalAmount'
  | 'AverageAmount'
  | 'RecordsPer1000'
  | 'IncomeLevel'

export const CHOROPLETH_METRIC_LABELS: Record<ChoroplethMetric, string> = {
  Population: 'Population',
  MedianHouseholdIncome: 'Median household income',
  PovertyRate: 'Poverty rate',
  MinorityPopulationPercent: 'Minority population %',
  RecordCount: 'Number of records',
  TotalAmount: 'Total amount',
  AverageAmount: 'Average amount',
  RecordsPer1000: 'Records per 1,000 residents',
  IncomeLevel: 'LMI classification (Low/Mod/Mid/Upper)',
}

// Matches the backend's TractIncomeTier enum, in FFIEC's official order
// (Low -> Upper is a real ordinal magnitude, not an arbitrary category list).
export type TractIncomeTier = 'Unknown' | 'Low' | 'Moderate' | 'Middle' | 'Upper'

export interface TractIncomeLevel {
  tier: TractIncomeTier
  tractMedianFamilyIncome: number | null
  areaMedianFamilyIncome: number | null
  incomePercentageOfAreaMedian: number | null
  msaMdName: string | null
}

export interface TractMetric {
  geoid: string
  value: number | null
}

export interface CountyRef {
  stateFips: string
  countyFips: string
}

export interface TractImportStatus {
  neededCounties: CountyRef[]
  importedCounties: CountyRef[]
  allImported: boolean
  hasUnassignedCoordinates: boolean
  hasEstimatedDemographics: boolean
}

export interface TractDemographics {
  population: number | null
  medianHouseholdIncome: number | null
  povertyRate: number | null
  medianAge: number | null
  ownerOccupiedPercent: number | null
  renterOccupiedPercent: number | null
  minorityPopulationPercent: number | null
  surveyYear: number
  isEstimated: boolean
}

export interface TractBoundary {
  geoid: string
  tractName: string | null
  countyFips: string
  boundary: GeoJSON.Geometry
  demographics: TractDemographics | null
  incomeLevel: TractIncomeLevel | null
}

export function getTractStatus(organizationId: number, datasetId: number): Promise<TractImportStatus> {
  return apiFetch<TractImportStatus>(`/api/organizations/${organizationId}/datasets/${datasetId}/tracts/status`)
}

export function requestTractImport(organizationId: number, datasetId: number): Promise<boolean> {
  return apiFetch<boolean>(`/api/organizations/${organizationId}/datasets/${datasetId}/tracts/import`, {
    method: 'POST',
  })
}

export function requestDemographicsRefresh(organizationId: number, datasetId: number): Promise<boolean> {
  return apiFetch<boolean>(`/api/organizations/${organizationId}/datasets/${datasetId}/tracts/refresh-demographics`, {
    method: 'POST',
  })
}

export function getTractBoundaries(organizationId: number, datasetId: number): Promise<TractBoundary[]> {
  return apiFetch<TractBoundary[]>(`/api/organizations/${organizationId}/datasets/${datasetId}/tracts`)
}

export function getTractChoropleth(
  organizationId: number,
  datasetId: number,
  metric: ChoroplethMetric,
  filters: RecordFilters = {},
): Promise<TractMetric[]> {
  const query = new URLSearchParams({ metric })
  appendFilterParams(query, filters)
  return apiFetch<TractMetric[]>(
    `/api/organizations/${organizationId}/datasets/${datasetId}/tracts/choropleth?${query.toString()}`,
  )
}
