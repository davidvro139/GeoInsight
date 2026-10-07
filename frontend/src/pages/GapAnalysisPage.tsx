import { useMutation, useQuery } from '@tanstack/react-query'
import { jsPDF } from 'jspdf'
import * as maplibregl from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import { useEffect, useRef, useState } from 'react'
import { useOutletContext } from 'react-router-dom'
import { getAnalytics, type Analytics, type TractStatistic } from '@/api/analytics'
import { ApiError } from '@/api/client'
import { base64FromDataUrl, createFileExport } from '@/api/exports'
import { getHmdaBenchmark, type HmdaCountyBenchmark } from '@/api/hmdaBenchmark'
import type { RecordFilters } from '@/api/recordFilters'
import { getTractBoundaries, type TractIncomeTier } from '@/api/tracts'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { RecordFilterSidebar } from '@/components/RecordFilterSidebar'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateViews'
import { DIVERGING_NEGATIVE, DIVERGING_NEUTRAL, DIVERGING_POSITIVE, SEQUENTIAL_BLUE } from '@/lib/chartColors'
import { MAP_STYLE } from '@/lib/mapStyle'
import { addPageHeader, PDF_MARGIN } from '@/lib/pdfReport'
import { US_COUNTIES } from '@/data/usCounties'
import type { DatasetOutletContext } from './DatasetLayout'

// Empirically confirmed live against the real CFPB HMDA Data Browser API:
// 2018-2025 all return real data (the API's own error message claims
// "2018-2023", but that's stale — 2024/2025 both work); 2026 and earlier than
// 2018 are rejected with a 400.
const HMDA_YEARS = [2025, 2024, 2023, 2022, 2021, 2020, 2019, 2018]

// A simple, explainable cutoff — not a statistical z-score, which would
// overstate precision for demographic figures already labeled "estimated".
const DEVIATION_THRESHOLD = 25

type SortKey =
  | 'geoid' | 'countyFips' | 'population' | 'recordCount' | 'recordsPer1000'
  | 'countyAverageRecordsPer1000' | 'deviationFromCountyAveragePercent' | 'averageAmount' | 'incomeTier'
  | 'inAssessmentArea'
type SortDir = 'asc' | 'desc'

const COLUMNS: { key: SortKey; label: string }[] = [
  { key: 'geoid', label: 'Tract' },
  { key: 'countyFips', label: 'County' },
  { key: 'population', label: 'Population' },
  { key: 'incomeTier', label: 'Income Tier' },
  { key: 'inAssessmentArea', label: 'Assessment Area' },
  { key: 'recordCount', label: 'Records' },
  { key: 'recordsPer1000', label: 'Records/1K' },
  { key: 'countyAverageRecordsPer1000', label: 'County Avg' },
  { key: 'deviationFromCountyAveragePercent', label: 'Deviation' },
  { key: 'averageAmount', label: 'Avg Amount' },
]

function deviationBadge(deviation: number | null): { label: string; variant: 'default' | 'destructive' | 'secondary' } {
  if (deviation == null) return { label: 'No data', variant: 'secondary' }
  if (deviation <= -DEVIATION_THRESHOLD) return { label: 'Below average', variant: 'destructive' }
  if (deviation >= DEVIATION_THRESHOLD) return { label: 'Above average', variant: 'default' }
  return { label: 'Typical', variant: 'secondary' }
}

// Real FFIEC classification (see the Census Reference Data card), not a
// derived statistic — rank used only for this table's sort, low income first.
const INCOME_TIER_RANK: Record<TractIncomeTier, number> = { Low: 0, Moderate: 1, Middle: 2, Upper: 3, Unknown: 4 }
const INCOME_TIER_DOT_COLOR: Record<TractIncomeTier, string> = {
  Low: SEQUENTIAL_BLUE[100], Moderate: SEQUENTIAL_BLUE[300], Middle: SEQUENTIAL_BLUE[500], Upper: SEQUENTIAL_BLUE[700], Unknown: '#d1d5db',
}
const LMI_TIERS = new Set<TractIncomeTier>(['Low', 'Moderate'])

function countyName(stateFips: string, countyFips: string): string {
  const county = US_COUNTIES.find((c) => c.stateFips === stateFips && c.countyFips === countyFips)
  return county ? `${county.countyName}, ${county.stateAbbr}` : `County ${stateFips}${countyFips}`
}

/**
 * The CRA-focused counterpart to AnalyticsPage's buildReportPdf — pulls in
 * the three features this page's generic sibling doesn't have (LMI tier,
 * Assessment Area, HMDA peer benchmark) so a compliance officer has one
 * self-contained document to hand an examiner instead of a link to a live
 * dashboard. Assessment Area and HMDA sections are each omitted (not shown
 * as empty/zero) when that data isn't configured/loaded, mirroring how the
 * on-screen page itself only shows them when there's something real to show.
 */
function buildComplianceReportPdf(params: {
  organizationName: string
  datasetName: string
  filters: RecordFilters
  data: Analytics
  sortedStats: TractStatistic[]
  belowCount: number
  aboveCount: number
  regionAverage: number | null
  tiersKnownCount: number
  lmiCount: number
  hasAssessmentArea: boolean
  assessmentAreaPercent: number
  recordsInAssessmentArea: number
  totalRecordsInTracts: number
  lmiRecordsInAssessmentArea: number
  benchmark: { year: number; rows: HmdaCountyBenchmark[] } | null
  mapImage: string | null
}): jsPDF {
  const {
    organizationName, datasetName, filters, data, sortedStats, belowCount, aboveCount, regionAverage,
    tiersKnownCount, lmiCount, hasAssessmentArea, assessmentAreaPercent, recordsInAssessmentArea,
    totalRecordsInTracts, lmiRecordsInAssessmentArea, benchmark, mapImage,
  } = params

  const doc = new jsPDF({ unit: 'pt', format: 'letter' })
  const pageWidth = doc.internal.pageSize.getWidth()
  const pageHeight = doc.internal.pageSize.getHeight()
  const margin = PDF_MARGIN
  let y = addPageHeader(doc, { title: 'GeoInsight CRA Compliance Report', organizationName, datasetName, filters })

  function ensureSpace(needed: number) {
    if (y + needed > pageHeight - margin) {
      doc.addPage()
      y = margin
    }
  }

  doc.setFontSize(9)
  doc.setTextColor(153, 27, 27)
  doc.text(
    'Not a legal or regulatory determination. This report identifies statistical patterns for informational purposes ' +
      'only. It does not constitute a legal or regulatory determination of discriminatory lending or any other violation.',
    margin, y, { maxWidth: pageWidth - margin * 2 },
  )
  doc.setTextColor(0, 0, 0)
  y += 34

  doc.setFontSize(13)
  doc.text('Gap Analysis Summary', margin, y); y += 18
  doc.setFontSize(10)
  doc.text(
    `Region-wide average: ${regionAverage?.toFixed(2) ?? '—'} records per 1,000 residents. ` +
      `${belowCount} of ${data.tractStatistics.length} tracts fall more than 25% below their county average; ` +
      `${aboveCount} fall more than 25% above.`,
    margin, y, { maxWidth: pageWidth - margin * 2 },
  ); y += 28

  doc.setFontSize(13)
  doc.text('LMI Classification', margin, y); y += 18
  doc.setFontSize(10)
  doc.text(
    tiersKnownCount > 0
      ? `${lmiCount} of ${tiersKnownCount} tracts with a known FFIEC classification are Low or Moderate income (LMI).`
      : 'No tracts have a known FFIEC income classification yet.',
    margin, y, { maxWidth: pageWidth - margin * 2 },
  ); y += 28

  if (hasAssessmentArea) {
    doc.setFontSize(13)
    doc.text('Assessment Area', margin, y); y += 18
    doc.setFontSize(10)
    doc.text(
      `${assessmentAreaPercent}% of records (${recordsInAssessmentArea} of ${totalRecordsInTracts}) fall within the ` +
        `organization's declared assessment area; ${lmiRecordsInAssessmentArea} are in Low/Moderate income tracts inside it.`,
      margin, y, { maxWidth: pageWidth - margin * 2 },
    ); y += 28
  }

  if (benchmark && benchmark.rows.length > 0) {
    ensureSpace(40 + benchmark.rows.length * 13)
    doc.setFontSize(13)
    doc.text(`HMDA Peer Benchmark (${benchmark.year})`, margin, y); y += 16
    doc.setFontSize(8)
    const hmdaColumns = [
      { header: 'County', x: margin, width: 110 },
      { header: 'Your Records', x: margin + 115, width: 65 },
      { header: 'Your Total', x: margin + 185, width: 70 },
      { header: 'Market Loans', x: margin + 260, width: 70 },
      { header: 'Market Total', x: margin + 335, width: 80 },
      { header: 'Your Share', x: margin + 420, width: 60 },
    ]
    hmdaColumns.forEach((c) => doc.text(c.header, c.x, y))
    y += 12
    for (const row of benchmark.rows) {
      ensureSpace(13)
      doc.text(countyName(row.stateFips, row.countyFips), hmdaColumns[0].x, y, { maxWidth: hmdaColumns[0].width })
      doc.text(row.datasetRecordCount.toLocaleString(), hmdaColumns[1].x, y)
      doc.text(`$${Math.round(row.datasetTotalAmount).toLocaleString()}`, hmdaColumns[2].x, y)
      doc.text(row.marketOriginatedLoanCount?.toLocaleString() ?? 'N/A', hmdaColumns[3].x, y)
      doc.text(row.marketTotalAmount != null ? `$${Math.round(row.marketTotalAmount).toLocaleString()}` : 'N/A', hmdaColumns[4].x, y)
      doc.text(row.datasetSharePercent != null ? `${row.datasetSharePercent}%` : 'N/A', hmdaColumns[5].x, y)
      y += 13
    }
    y += 16
  }

  if (mapImage) {
    ensureSpace(230)
    doc.setFontSize(13)
    doc.text('Highlighted Map', margin, y); y += 16
    const mapWidth = pageWidth - margin * 2
    doc.addImage(mapImage, 'PNG', margin, y, mapWidth, mapWidth * 0.55)
    y += mapWidth * 0.55 + 20
  }

  if (sortedStats.length > 0) {
    doc.addPage()
    y = margin
    doc.setFontSize(13)
    doc.text('Tract Comparison', margin, y); y += 20
    doc.setFontSize(7)
    const columns = [
      { header: 'Tract', x: margin, width: 85 },
      { header: 'County', x: margin + 88, width: 32 },
      { header: 'Population', x: margin + 123, width: 50 },
      { header: 'Income Tier', x: margin + 176, width: 55 },
      { header: 'Assess. Area', x: margin + 234, width: 45 },
      { header: 'Records', x: margin + 282, width: 38 },
      { header: 'Rec/1K', x: margin + 323, width: 35 },
      { header: 'Cnty Avg', x: margin + 361, width: 38 },
      { header: 'Deviation', x: margin + 402, width: 55 },
      { header: 'Avg Amt', x: margin + 460, width: 65 },
    ]
    columns.forEach((c) => doc.text(c.header, c.x, y))
    y += 12
    for (const t of sortedStats) {
      if (y > pageHeight - margin) { doc.addPage(); y = margin }
      doc.text(t.tractName ?? t.geoid, columns[0].x, y, { maxWidth: columns[0].width })
      doc.text(t.countyFips, columns[1].x, y)
      doc.text(t.population?.toLocaleString() ?? '—', columns[2].x, y)
      doc.text(t.incomeTier ?? '—', columns[3].x, y)
      doc.text(t.inAssessmentArea == null ? '—' : t.inAssessmentArea ? 'Yes' : 'No', columns[4].x, y)
      doc.text(t.recordCount.toLocaleString(), columns[5].x, y)
      doc.text(t.recordsPer1000?.toString() ?? '—', columns[6].x, y)
      doc.text(t.countyAverageRecordsPer1000?.toFixed(2) ?? '—', columns[7].x, y)
      doc.text(t.deviationFromCountyAveragePercent != null ? `${t.deviationFromCountyAveragePercent}%` : '—', columns[8].x, y)
      doc.text(`$${Math.round(t.averageAmount).toLocaleString()}`, columns[9].x, y)
      y += 12
    }
  }

  return doc
}

export function GapAnalysisPage() {
  const { organization, dataset } = useOutletContext<DatasetOutletContext>()
  const [filters, setFilters] = useState<RecordFilters>({})
  const [sortKey, setSortKey] = useState<SortKey>('recordCount')
  const [sortDir, setSortDir] = useState<SortDir>('desc')

  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<maplibregl.Map | null>(null)
  const [mapLoaded, setMapLoaded] = useState(false)

  const analyticsQuery = useQuery({
    queryKey: ['organizations', organization.id, 'datasets', dataset.id, 'analytics', filters],
    queryFn: () => getAnalytics(organization.id, dataset.id, filters),
  })

  const boundariesQuery = useQuery({
    queryKey: ['organizations', organization.id, 'datasets', dataset.id, 'tracts', 'boundaries'],
    queryFn: () => getTractBoundaries(organization.id, dataset.id),
  })

  // Initialize the map once. Simpler than the Map tab's — tract outlines
  // only, colored by a diverging scale centered on 0% deviation, since
  // "highlighting gaps" is this page's whole point, not general exploration.
  useEffect(() => {
    if (!containerRef.current || mapRef.current) return

    const map = new maplibregl.Map({
      container: containerRef.current,
      style: MAP_STYLE,
      center: [-111.9, 41.3],
      zoom: 6,
      // The compliance report captures this map's canvas via getCanvas().
      // toDataURL() — WebGL clears its drawing buffer after compositing by
      // default, so without this flag that capture comes back blank even
      // though the map is visibly rendered on screen.
      canvasContextAttributes: { preserveDrawingBuffer: true },
    })
    map.addControl(new maplibregl.NavigationControl(), 'top-right')

    map.on('load', () => {
      map.addSource('gap-tracts', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } })
      map.addLayer({
        id: 'gap-fill', type: 'fill', source: 'gap-tracts',
        paint: { 'fill-color': '#d1d5db', 'fill-opacity': 0.7 },
      })
      map.addLayer({
        id: 'gap-line', type: 'line', source: 'gap-tracts',
        paint: { 'line-color': '#374151', 'line-width': 1 },
      })
      // A dashed violet outline for tracts inside the org's declared
      // Assessment Area — deliberately a border, not a fill, so it layers on
      // top of the deviation coloring instead of competing with it; the dash
      // pattern (not just a color) keeps the boundary legible for colorblind
      // viewers and in black-and-white print, matching this project's own
      // dataviz-skill discipline of never encoding identity in color alone.
      map.addLayer({
        id: 'assessment-area-outline', type: 'line', source: 'gap-tracts',
        filter: ['==', ['get', 'inAssessmentArea'], true],
        paint: { 'line-color': '#4a3aa7', 'line-width': 2.5, 'line-dasharray': [2, 1.5] },
      })
      setMapLoaded(true)
    })

    mapRef.current = map
    return () => {
      map.remove()
      mapRef.current = null
    }
  }, [])

  // Keep the map's tract fill in sync with the loaded deviation figures.
  useEffect(() => {
    const map = mapRef.current
    if (!map || !mapLoaded || !analyticsQuery.data || !boundariesQuery.data) return

    const statsByGeoid = new Map(analyticsQuery.data.tractStatistics.map((t) => [t.geoid, t]))
    const features: GeoJSON.Feature[] = boundariesQuery.data
      .filter((t) => statsByGeoid.has(t.geoid))
      .map((t) => {
        const stats = statsByGeoid.get(t.geoid)!
        return {
          type: 'Feature',
          geometry: t.boundary,
          properties: {
            geoid: t.geoid,
            deviation: stats.deviationFromCountyAveragePercent,
            inAssessmentArea: stats.inAssessmentArea === true,
          },
        }
      })
    const featureCollection: GeoJSON.FeatureCollection = { type: 'FeatureCollection', features }

    const source = map.getSource('gap-tracts') as maplibregl.GeoJSONSource | undefined
    source?.setData(featureCollection)

    map.setPaintProperty('gap-fill', 'fill-color', [
      'case',
      ['==', ['typeof', ['get', 'deviation']], 'number'],
      ['interpolate', ['linear'], ['get', 'deviation'],
        -50, DIVERGING_NEGATIVE, 0, DIVERGING_NEUTRAL, 50, DIVERGING_POSITIVE],
      '#d1d5db',
    ])

    if (features.length > 0) {
      const bounds = new maplibregl.LngLatBounds()
      features.forEach((f) => {
        const coords = f.geometry.type === 'MultiPolygon' ? f.geometry.coordinates.flat(2) : []
        coords.forEach((c) => bounds.extend(c as [number, number]))
      })
      if (!bounds.isEmpty()) map.fitBounds(bounds, { padding: 30, maxZoom: 12 })
    }
  }, [mapLoaded, analyticsQuery.data, boundariesQuery.data])

  const toggleSort = (key: SortKey) => {
    if (sortKey === key) {
      setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'))
    } else {
      setSortKey(key)
      setSortDir('desc')
    }
  }

  const data = analyticsQuery.data
  const sortedStats = [...(data?.tractStatistics ?? [])].sort((a: TractStatistic, b: TractStatistic) => {
    if (sortKey === 'incomeTier') {
      const an = INCOME_TIER_RANK[a.incomeTier ?? 'Unknown']
      const bn = INCOME_TIER_RANK[b.incomeTier ?? 'Unknown']
      return sortDir === 'asc' ? an - bn : bn - an
    }
    if (sortKey === 'inAssessmentArea') {
      const rank = (v: boolean | null) => (v == null ? -1 : v ? 1 : 0)
      const an = rank(a.inAssessmentArea)
      const bn = rank(b.inAssessmentArea)
      return sortDir === 'asc' ? an - bn : bn - an
    }
    const av = a[sortKey]
    const bv = b[sortKey]
    if (typeof av === 'string' || typeof bv === 'string') {
      const cmp = String(av ?? '').localeCompare(String(bv ?? ''))
      return sortDir === 'asc' ? cmp : -cmp
    }
    const an = av ?? -Infinity
    const bn = bv ?? -Infinity
    return sortDir === 'asc' ? an - bn : bn - an
  })

  const belowCount = data?.tractStatistics.filter((t) => (t.deviationFromCountyAveragePercent ?? 0) <= -DEVIATION_THRESHOLD).length ?? 0
  const aboveCount = data?.tractStatistics.filter((t) => (t.deviationFromCountyAveragePercent ?? 0) >= DEVIATION_THRESHOLD).length ?? 0
  const regionAverage = data?.tractStatistics[0]?.regionAverageRecordsPer1000 ?? null

  // LMI = Low or Moderate income relative to the tract's real FFIEC area
  // median family income — tracts with no FFIEC row (t.incomeTier == null)
  // are excluded from both the numerator and denominator rather than
  // silently counted as "not LMI".
  const tiersKnown = data?.tractStatistics.filter((t) => t.incomeTier != null) ?? []
  const lmiCount = tiersKnown.filter((t) => LMI_TIERS.has(t.incomeTier!)).length

  // Records (not tracts) inside vs. outside the org's declared Assessment
  // Area, and specifically how many of the records inside it sit in an LMI
  // tract — the two figures the TODO backlog item calls for. Computed from
  // each tract's own recordCount, same "tract -> records" rollup already
  // used for belowCount/aboveCount above.
  const hasAssessmentArea = data?.summary.hasAssessmentArea ?? false
  const totalRecordsInTracts = data?.tractStatistics.reduce((sum, t) => sum + t.recordCount, 0) ?? 0
  const recordsInAssessmentArea = data?.tractStatistics
    .filter((t) => t.inAssessmentArea === true)
    .reduce((sum, t) => sum + t.recordCount, 0) ?? 0
  const lmiRecordsInAssessmentArea = data?.tractStatistics
    .filter((t) => t.inAssessmentArea === true && t.incomeTier != null && LMI_TIERS.has(t.incomeTier))
    .reduce((sum, t) => sum + t.recordCount, 0) ?? 0
  const assessmentAreaPercent = totalRecordsInTracts > 0 ? Math.round((recordsInAssessmentArea / totalRecordsInTracts) * 1000) / 10 : 0

  const [benchmarkYear, setBenchmarkYear] = useState(HMDA_YEARS[0])
  const benchmarkMutation = useMutation({
    mutationFn: () => getHmdaBenchmark(organization.id, dataset.id, benchmarkYear, filters),
  })

  const [isGeneratingReport, setIsGeneratingReport] = useState(false)
  const [reportError, setReportError] = useState<string | null>(null)

  const handleGenerateReport = async () => {
    if (!data) return
    setReportError(null)
    setIsGeneratingReport(true)
    try {
      const mapImage = mapRef.current?.getCanvas().toDataURL('image/png') ?? null
      const doc = buildComplianceReportPdf({
        organizationName: organization.name,
        datasetName: dataset.name,
        filters,
        data,
        sortedStats,
        belowCount,
        aboveCount,
        regionAverage,
        tiersKnownCount: tiersKnown.length,
        lmiCount,
        hasAssessmentArea,
        assessmentAreaPercent,
        recordsInAssessmentArea,
        totalRecordsInTracts,
        lmiRecordsInAssessmentArea,
        benchmark: benchmarkMutation.data ? { year: benchmarkYear, rows: benchmarkMutation.data } : null,
        mapImage,
      })

      doc.save(`${dataset.name}-cra-compliance-report.pdf`)

      const base64 = base64FromDataUrl(doc.output('datauristring'))
      await createFileExport(organization.id, dataset.id, 'Pdf', base64)
    } catch (err) {
      setReportError(err instanceof ApiError ? err.message : 'Unable to generate the compliance report.')
    } finally {
      setIsGeneratingReport(false)
    }
  }

  return (
    <div className="grid grid-cols-1 items-start gap-4 md:grid-cols-[260px_1fr]">
      <RecordFilterSidebar description="Narrows the gap analysis below." onChange={setFilters} />

      <div className="flex min-w-0 flex-col gap-4">
        <Alert variant="destructive">
          <AlertTitle>Not a legal or regulatory determination</AlertTitle>
          <AlertDescription>
            This page identifies statistical patterns for informational purposes only. It does not constitute a legal or
            regulatory determination of discriminatory lending or any other violation.
          </AlertDescription>
        </Alert>

        {data && data.tractStatistics.length > 0 && (
          <div>
            <Button size="sm" variant="outline" disabled={isGeneratingReport} onClick={handleGenerateReport}>
              {isGeneratingReport ? 'Generating…' : 'Generate Compliance Report'}
            </Button>
            {reportError && <ErrorState message={reportError} />}
          </div>
        )}

        {analyticsQuery.isPending && <LoadingState />}
        {analyticsQuery.isError && <ErrorState message="Unable to load gap analysis." />}
        {boundariesQuery.isError && <ErrorState message="Unable to load tract boundaries for the map." />}

        {data && data.tractStatistics.length === 0 && (
          <EmptyState message="No tracts to analyze yet — import tract boundaries from the Map tab." />
        )}

        {data && data.tractStatistics.length > 0 && (
          <Card>
            <CardHeader>
              <CardTitle className="text-base">Summary</CardTitle>
            </CardHeader>
            <CardContent>
              <p className="text-sm text-muted-foreground">
                The region-wide average is <strong className="text-foreground">{regionAverage?.toFixed(2) ?? '—'}</strong>{' '}
                records per 1,000 residents. <strong className="text-foreground">{belowCount}</strong> of{' '}
                {data.tractStatistics.length} tracts fall more than {DEVIATION_THRESHOLD}% below their county average;{' '}
                <strong className="text-foreground">{aboveCount}</strong> fall more than {DEVIATION_THRESHOLD}% above.
              </p>
              {tiersKnown.length > 0 && (
                <p className="mt-2 text-sm text-muted-foreground">
                  <strong className="text-foreground">{lmiCount}</strong> of {tiersKnown.length} tracts with a known FFIEC
                  classification are Low or Moderate income (LMI).
                </p>
              )}
              {hasAssessmentArea && (
                <p className="mt-2 text-sm text-muted-foreground">
                  <strong className="text-foreground">{assessmentAreaPercent}%</strong> of records (
                  <strong className="text-foreground">{recordsInAssessmentArea}</strong> of {totalRecordsInTracts}) fall
                  within the organization's declared assessment area;{' '}
                  <strong className="text-foreground">{lmiRecordsInAssessmentArea}</strong> are in Low/Moderate income
                  tracts inside it.
                </p>
              )}
            </CardContent>
          </Card>
        )}

        {/* Always rendered (not gated on `data`) so the map container exists in
            the DOM the moment the mount effect below runs — it only ever runs
            once, so if this div appeared later the map would never be created. */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Highlighted map</CardTitle>
            <CardDescription>Colored by each tract's deviation from its county's average records per 1,000 population.</CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-3">
            <div ref={containerRef} className="h-[360px] w-full overflow-hidden rounded-lg border" />
            <div className="flex items-center gap-3 text-xs text-muted-foreground">
              <span>Below county avg</span>
              <div
                className="h-3 w-32 rounded"
                style={{ background: `linear-gradient(to right, ${DIVERGING_NEGATIVE}, ${DIVERGING_NEUTRAL}, ${DIVERGING_POSITIVE})` }}
              />
              <span>Above county avg</span>
            </div>
            {hasAssessmentArea && (
              <div className="flex items-center gap-2 text-xs text-muted-foreground">
                <svg width="24" height="10" aria-hidden="true">
                  <line x1="0" y1="5" x2="24" y2="5" stroke="#4a3aa7" strokeWidth="2.5" strokeDasharray="4,3" />
                </svg>
                <span>Dashed outline = inside the declared assessment area</span>
              </div>
            )}
          </CardContent>
        </Card>

        {data && (
          <>
            <Card>
              <CardHeader>
                <CardTitle className="text-base">Tract comparison</CardTitle>
                <CardDescription>
                  {data.summary.demographicsAreEstimated
                    ? 'Population figures are estimated (sample data).'
                    : 'Population figures are real Census ACS data.'}
                </CardDescription>
              </CardHeader>
              <CardContent>
                {sortedStats.length === 0 ? (
                  <p className="text-sm text-muted-foreground">No tracts yet.</p>
                ) : (
                  <div className="overflow-x-auto rounded-md border">
                    <table className="w-full text-left text-sm">
                      <thead className="bg-muted/50">
                        <tr>
                          {COLUMNS.map((col) => (
                            <th key={col.key} className="whitespace-nowrap px-3 py-2 font-medium">
                              <button
                                type="button"
                                className="flex items-center gap-1 hover:underline"
                                onClick={() => toggleSort(col.key)}
                              >
                                {col.label}
                                {sortKey === col.key && (sortDir === 'asc' ? '▲' : '▼')}
                              </button>
                            </th>
                          ))}
                        </tr>
                      </thead>
                      <tbody>
                        {sortedStats.map((t) => {
                          const { label, variant } = deviationBadge(t.deviationFromCountyAveragePercent)
                          return (
                            <tr key={t.geoid} className="border-t">
                              <td className="px-3 py-2">{t.tractName ?? t.geoid}</td>
                              <td className="px-3 py-2 text-muted-foreground">{t.countyFips}</td>
                              <td className="px-3 py-2">{t.population?.toLocaleString() ?? '—'}</td>
                              <td className="px-3 py-2">
                                {t.incomeTier != null ? (
                                  <span className="flex items-center gap-1.5">
                                    <span
                                      className="inline-block h-2 w-2 rounded-full"
                                      style={{ background: INCOME_TIER_DOT_COLOR[t.incomeTier] }}
                                    />
                                    {t.incomeTier}
                                  </span>
                                ) : (
                                  '—'
                                )}
                              </td>
                              <td className="px-3 py-2">
                                {t.inAssessmentArea == null ? (
                                  '—'
                                ) : (
                                  <Badge variant={t.inAssessmentArea ? 'default' : 'secondary'}>
                                    {t.inAssessmentArea ? 'Yes' : 'No'}
                                  </Badge>
                                )}
                              </td>
                              <td className="px-3 py-2">{t.recordCount.toLocaleString()}</td>
                              <td className="px-3 py-2">{t.recordsPer1000 ?? '—'}</td>
                              <td className="px-3 py-2">{t.countyAverageRecordsPer1000?.toFixed(2) ?? '—'}</td>
                              <td className="px-3 py-2">
                                <div className="flex items-center gap-2">
                                  <span>
                                    {t.deviationFromCountyAveragePercent != null
                                      ? `${t.deviationFromCountyAveragePercent > 0 ? '+' : ''}${t.deviationFromCountyAveragePercent}%`
                                      : '—'}
                                  </span>
                                  <Badge variant={variant}>{label}</Badge>
                                </div>
                              </td>
                              <td className="px-3 py-2">${Math.round(t.averageAmount).toLocaleString()}</td>
                            </tr>
                          )
                        })}
                      </tbody>
                    </table>
                  </div>
                )}
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle className="text-base">HMDA Peer Benchmark</CardTitle>
                <CardDescription>
                  Compares this dataset against real, publicly reported CFPB HMDA mortgage-lending data for the same
                  county and year — most meaningful for mortgage/loan datasets. County-level only; HMDA's public
                  aggregation API does not support tract-level queries.
                </CardDescription>
              </CardHeader>
              <CardContent className="flex flex-col gap-3">
                <div className="flex items-center gap-2">
                  <Select value={String(benchmarkYear)} onValueChange={(v) => setBenchmarkYear(Number(v))}>
                    <SelectTrigger size="sm" className="w-28">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {HMDA_YEARS.map((y) => (
                        <SelectItem key={y} value={String(y)}>
                          {y}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <Button disabled={benchmarkMutation.isPending} onClick={() => benchmarkMutation.mutate()}>
                    {benchmarkMutation.isPending ? 'Loading…' : 'Load Peer Benchmark'}
                  </Button>
                </div>

                {benchmarkMutation.isError && (
                  <ErrorState
                    message={
                      benchmarkMutation.error instanceof ApiError
                        ? benchmarkMutation.error.message
                        : 'Unable to load the HMDA benchmark.'
                    }
                  />
                )}

                {benchmarkMutation.data && (
                  benchmarkMutation.data.length === 0 ? (
                    <p className="text-sm text-muted-foreground">No geocoded records to benchmark for the current filters.</p>
                  ) : (
                    <div className="overflow-x-auto rounded-md border">
                      <table className="w-full text-left text-sm">
                        <thead className="bg-muted/50">
                          <tr>
                            <th className="whitespace-nowrap px-3 py-2 font-medium">County</th>
                            <th className="whitespace-nowrap px-3 py-2 font-medium">Your records</th>
                            <th className="whitespace-nowrap px-3 py-2 font-medium">Your total</th>
                            <th className="whitespace-nowrap px-3 py-2 font-medium">Public market loans</th>
                            <th className="whitespace-nowrap px-3 py-2 font-medium">Public market total</th>
                            <th className="whitespace-nowrap px-3 py-2 font-medium">Your share of market</th>
                          </tr>
                        </thead>
                        <tbody>
                          {benchmarkMutation.data.map((row) => (
                            <tr key={`${row.stateFips}${row.countyFips}`} className="border-t">
                              <td className="px-3 py-2">{countyName(row.stateFips, row.countyFips)}</td>
                              <td className="px-3 py-2">{row.datasetRecordCount.toLocaleString()}</td>
                              <td className="px-3 py-2">${Math.round(row.datasetTotalAmount).toLocaleString()}</td>
                              {row.marketOriginatedLoanCount == null ? (
                                <td className="px-3 py-2 text-muted-foreground" colSpan={3}>
                                  Data unavailable for this county/year.
                                </td>
                              ) : (
                                <>
                                  <td className="px-3 py-2">{row.marketOriginatedLoanCount.toLocaleString()}</td>
                                  <td className="px-3 py-2">${Math.round(row.marketTotalAmount!).toLocaleString()}</td>
                                  <td className="px-3 py-2">{row.datasetSharePercent}%</td>
                                </>
                              )}
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )
                )}
              </CardContent>
            </Card>
          </>
        )}
      </div>
    </div>
  )
}
