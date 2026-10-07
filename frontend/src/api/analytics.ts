import { appendFilterParams, type RecordFilters } from './recordFilters'
import { apiFetch } from './client'
import type { TractIncomeTier } from './tracts'

export interface AnalyticsSummary {
  recordCount: number
  geocodedCount: number
  geocodedRate: number
  failedCount: number
  totalAmount: number
  tractCount: number
  countyCount: number
  demographicsAreEstimated: boolean
  hasAssessmentArea: boolean
}

export interface CategoryCount {
  category: string | null
  count: number
}

export interface CountyCount {
  countyFips: string
  count: number
}

export interface IncomeRangeCount {
  range: string
  count: number
}

export interface GeocodeStatusCount {
  status: string
  count: number
}

export interface TimeSeriesPoint {
  period: string
  count: number
}

export interface TractStatistic {
  geoid: string
  tractName: string | null
  countyFips: string
  population: number | null
  recordCount: number
  recordsPer1000: number | null
  totalAmount: number
  averageAmount: number
  countyAverageRecordsPer1000: number | null
  regionAverageRecordsPer1000: number | null
  deviationFromCountyAveragePercent: number | null
  incomeTier: TractIncomeTier | null
  inAssessmentArea: boolean | null
}

export interface Analytics {
  summary: AnalyticsSummary
  byCategory: CategoryCount[]
  byCounty: CountyCount[]
  byIncomeRange: IncomeRangeCount[]
  byGeocodeStatus: GeocodeStatusCount[]
  overTime: TimeSeriesPoint[]
  tractStatistics: TractStatistic[]
}

export function getAnalytics(organizationId: number, datasetId: number, filters: RecordFilters = {}): Promise<Analytics> {
  const query = new URLSearchParams()
  appendFilterParams(query, filters)
  const suffix = query.toString() ? `?${query.toString()}` : ''
  return apiFetch<Analytics>(`/api/organizations/${organizationId}/datasets/${datasetId}/analytics${suffix}`)
}
