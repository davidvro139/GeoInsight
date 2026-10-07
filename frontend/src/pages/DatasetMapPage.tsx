import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as maplibregl from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import { useEffect, useRef, useState } from 'react'
import { Link, useOutletContext, useSearchParams } from 'react-router-dom'
import { ApiError } from '@/api/client'
import { getRecordPoints, getRecords, type DatasetRecordPoint } from '@/api/records'
import type { RecordFilters } from '@/api/recordFilters'
import { base64FromDataUrl, createDataExport, createFileExport, downloadExport, saveDataUrl } from '@/api/exports'
import { createSavedView, getSavedViews } from '@/api/savedViews'
import {
  CHOROPLETH_METRIC_LABELS,
  getTractBoundaries,
  getTractChoropleth,
  getTractStatus,
  requestDemographicsRefresh,
  requestTractImport,
  type ChoroplethMetric,
  type TractIncomeTier,
} from '@/api/tracts'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Checkbox } from '@/components/ui/checkbox'
import { RecordFilterSidebar } from '@/components/RecordFilterSidebar'
import { ErrorState, LoadingState } from '@/components/StateViews'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { SEQUENTIAL_BLUE } from '@/lib/chartColors'
import { MAP_STYLE } from '@/lib/mapStyle'
import type { DatasetOutletContext } from './DatasetLayout'

const PAGE_SIZE = 25

const DEFAULT_MAP_CENTER: [number, number] = [-111.9, 41.3]
const DEFAULT_MAP_ZOOM = 7

const CHOROPLETH_METRICS = Object.keys(CHOROPLETH_METRIC_LABELS) as ChoroplethMetric[]

type SortDir = 'asc' | 'desc'

interface TractFeatureProperties {
  geoid: string
  name: string | null
  population: number | null
  medianHouseholdIncome: number | null
  povertyRate: number | null
  medianAge: number | null
  ownerOccupiedPercent: number | null
  renterOccupiedPercent: number | null
  minorityPopulationPercent: number | null
  surveyYear: number | null
  isEstimated: boolean
  metricValue: number | null
  incomeTier: TractIncomeTier | null
  tractMedianFamilyIncome: number | null
  areaMedianFamilyIncome: number | null
  incomePercentageOfAreaMedian: number | null
  msaMdName: string | null
}

// The backend's IncomeLevel choropleth metric already returns this same 1-4
// ordinal as metricValue (Low..Upper, matching TractIncomeTier's enum order),
// so the existing generic interpolate-based choropleth colors it light->dark
// blue with no special-casing — this map is only for the popup's tier badge.
const INCOME_TIER_COLOR: Record<Exclude<TractIncomeTier, 'Unknown'>, string> = {
  Low: SEQUENTIAL_BLUE[100], Moderate: SEQUENTIAL_BLUE[300], Middle: SEQUENTIAL_BLUE[500], Upper: SEQUENTIAL_BLUE[700],
}

const columns: { key: string; label: string }[] = [
  { key: 'externalId', label: 'External ID' },
  { key: 'city', label: 'City' },
  { key: 'amount', label: 'Amount' },
  { key: 'recordDate', label: 'Date' },
  { key: 'geocodeStatus', label: 'Geocode' },
]

function pointsToGeoJson(points: DatasetRecordPoint[]): GeoJSON.FeatureCollection<GeoJSON.Point> {
  return {
    type: 'FeatureCollection',
    features: points.map((p) => ({
      type: 'Feature',
      geometry: { type: 'Point', coordinates: [p.longitude, p.latitude] },
      properties: {
        id: p.id, externalId: p.externalId, category: p.category,
        censusTract: p.censusTract, censusGeoid: p.censusGeoid,
      },
    })),
  }
}

// Shared by the initial points-load effect and the "Reset view" button so
// both ways of (re-)framing the map agree on exactly the same behavior.
function fitMapToPoints(map: maplibregl.Map, points: DatasetRecordPoint[]) {
  if (points.length === 1) {
    const [p] = points
    map.jumpTo({ center: [p.longitude, p.latitude], zoom: 12 })
  } else if (points.length > 1) {
    const bounds = new maplibregl.LngLatBounds()
    points.forEach((p) => bounds.extend([p.longitude, p.latitude]))
    map.fitBounds(bounds, { padding: 40, maxZoom: 14 })
  } else {
    map.jumpTo({ center: DEFAULT_MAP_CENTER, zoom: DEFAULT_MAP_ZOOM })
  }
}

const INCOME_LEVEL_ORDINAL_LABELS = ['', 'Low', 'Moderate', 'Middle', 'Upper']

function formatMetricValue(metric: ChoroplethMetric, value: number): string {
  if (metric === 'MedianHouseholdIncome' || metric === 'TotalAmount' || metric === 'AverageAmount') {
    return `$${Math.round(value).toLocaleString()}`
  }
  if (metric === 'PovertyRate' || metric === 'MinorityPopulationPercent') {
    return `${value.toFixed(1)}%`
  }
  if (metric === 'IncomeLevel') {
    return INCOME_LEVEL_ORDINAL_LABELS[Math.round(value)] ?? value.toLocaleString()
  }
  return value.toLocaleString()
}

export function DatasetMapPage() {
  const { organization, dataset, canManage } = useOutletContext<DatasetOutletContext>()
  const queryClient = useQueryClient()
  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<maplibregl.Map | null>(null)
  const popupRef = useRef<maplibregl.Popup | null>(null)
  const [mapLoaded, setMapLoaded] = useState(false)
  const [selectedRecordId, setSelectedRecordId] = useState<number | null>(null)
  const [showTracts, setShowTracts] = useState(false)
  const [choroplethMetric, setChoroplethMetric] = useState<ChoroplethMetric | ''>('')

  const [page, setPage] = useState(1)
  const [sortBy, setSortBy] = useState('createdAt')
  const [sortDir, setSortDir] = useState<SortDir>('desc')
  const [filters, setFilters] = useState<RecordFilters>({})
  const [filterResetSignal, setFilterResetSignal] = useState<{ filters: RecordFilters; key: number }>()

  const handleFiltersChange = (next: RecordFilters) => {
    setFilters(next)
    // Any filter change invalidates the current page — otherwise page 3 of an
    // unfiltered list could silently become an out-of-range page of a filtered one.
    setPage(1)
  }

  const [searchParams] = useSearchParams()
  const [isSavingView, setIsSavingView] = useState(false)
  const [newViewName, setNewViewName] = useState('')
  const appliedViewIdRef = useRef<string | null>(null)

  const savedViewsQuery = useQuery({
    queryKey: ['organizations', organization.id, 'datasets', dataset.id, 'saved-views'],
    queryFn: () => getSavedViews(organization.id, dataset.id),
  })

  const createSavedViewMutation = useMutation({
    mutationFn: (name: string) => {
      const center = mapRef.current?.getCenter()
      const zoom = mapRef.current?.getZoom()
      return createSavedView(organization.id, dataset.id, {
        name,
        filters,
        mapConfiguration: {
          showTracts,
          choroplethMetric: choroplethMetric || null,
          centerLng: center?.lng ?? null,
          centerLat: center?.lat ?? null,
          zoom: zoom ?? null,
        },
      })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['organizations', organization.id, 'datasets', dataset.id, 'saved-views'] })
      setIsSavingView(false)
      setNewViewName('')
    },
  })

  // Applies a `?view={id}` link (from the Saved Views tab's "Open" action)
  // once its data has loaded — a ref (not state) tracks which view id has
  // already been applied so this doesn't re-fire on every unrelated render.
  useEffect(() => {
    const viewId = searchParams.get('view')
    if (!viewId || viewId === appliedViewIdRef.current || !savedViewsQuery.data) return

    const view = savedViewsQuery.data.find((v) => String(v.id) === viewId)
    if (!view) return

    appliedViewIdRef.current = viewId
    setFilterResetSignal({ filters: view.filters ?? {}, key: Date.now() })
    setShowTracts(view.mapConfiguration?.showTracts ?? false)
    setChoroplethMetric((view.mapConfiguration?.choroplethMetric as ChoroplethMetric | null) ?? '')

    const { centerLng, centerLat, zoom } = view.mapConfiguration ?? {}
    if (centerLng != null && centerLat != null && zoom != null) {
      mapRef.current?.jumpTo({ center: [centerLng, centerLat], zoom })
    }
  }, [searchParams, savedViewsQuery.data])

  const [isExporting, setIsExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)

  const handleExport = async (type: 'Csv' | 'Xlsx' | 'GeoJson' | 'Png') => {
    setExportError(null)
    setIsExporting(true)
    try {
      if (type === 'Png') {
        const map = mapRef.current
        if (!map) return
        const dataUrl = map.getCanvas().toDataURL('image/png')
        saveDataUrl(dataUrl, `${dataset.name}-map.png`)
        // History record only — the file's already on the user's disk, so this can't block the download.
        createFileExport(organization.id, dataset.id, 'Png', base64FromDataUrl(dataUrl)).catch(() => {})
      } else {
        const extension = type === 'Csv' ? 'csv' : type === 'Xlsx' ? 'xlsx' : 'geojson'
        const created = await createDataExport(organization.id, dataset.id, type, filters)
        await downloadExport(organization.id, dataset.id, created.id, `${dataset.name}.${extension}`)
      }
    } catch (err) {
      setExportError(err instanceof ApiError ? err.message : 'Unable to export.')
    } finally {
      setIsExporting(false)
    }
  }

  const pointsQuery = useQuery({
    queryKey: ['organizations', organization.id, 'datasets', dataset.id, 'records', 'points', filters],
    queryFn: () => getRecordPoints(organization.id, dataset.id, filters),
  })

  const recordsQuery = useQuery({
    queryKey: ['organizations', organization.id, 'datasets', dataset.id, 'records', { page, sortBy, sortDir, filters }],
    queryFn: () => getRecords(organization.id, dataset.id, { page, pageSize: PAGE_SIZE, sortBy, sortDir, filters }),
  })

  const tractStatusKey = ['organizations', organization.id, 'datasets', dataset.id, 'tracts', 'status']
  const tractStatusQuery = useQuery({
    queryKey: tractStatusKey,
    queryFn: () => getTractStatus(organization.id, dataset.id),
    enabled: showTracts,
    refetchInterval: (query) => (query.state.data && !query.state.data.allImported ? 2000 : false),
  })

  // Boundaries/choropleth render whatever's already imported, even while some
  // of the dataset's counties are still pending — the API already returns
  // partial results, and waiting for every county blocked the map from
  // showing anything for datasets where import completes county-by-county.
  // `allImported` rides along in the query key so the set is re-fetched (and
  // any newly-imported county's tracts picked up) once the rest finish.
  const boundariesQuery = useQuery({
    queryKey: ['organizations', organization.id, 'datasets', dataset.id, 'tracts', 'boundaries', tractStatusQuery.data?.allImported],
    queryFn: () => getTractBoundaries(organization.id, dataset.id),
    enabled: showTracts && tractStatusQuery.data !== undefined,
  })

  const choroplethQuery = useQuery({
    queryKey: [
      'organizations', organization.id, 'datasets', dataset.id, 'tracts', 'choropleth',
      choroplethMetric, filters, tractStatusQuery.data?.allImported,
    ],
    queryFn: () => getTractChoropleth(organization.id, dataset.id, choroplethMetric as ChoroplethMetric, filters),
    enabled: showTracts && choroplethMetric !== '' && tractStatusQuery.data !== undefined,
  })

  const importTractsMutation = useMutation({
    mutationFn: () => requestTractImport(organization.id, dataset.id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: tractStatusKey }),
  })

  const refreshDemographicsMutation = useMutation({
    mutationFn: () => requestDemographicsRefresh(organization.id, dataset.id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tractStatusKey })
      queryClient.invalidateQueries({ queryKey: ['organizations', organization.id, 'datasets', dataset.id, 'tracts', 'boundaries'] })
    },
  })

  // Initialize the map once.
  useEffect(() => {
    if (!containerRef.current || mapRef.current) return

    const map = new maplibregl.Map({
      container: containerRef.current,
      style: MAP_STYLE,
      center: DEFAULT_MAP_CENTER,
      zoom: DEFAULT_MAP_ZOOM,
    })
    map.addControl(new maplibregl.NavigationControl(), 'top-right')

    map.on('load', () => {
      map.addSource('tracts', {
        type: 'geojson',
        data: { type: 'FeatureCollection', features: [] },
      })
      map.addLayer({
        id: 'tract-fill',
        type: 'fill',
        source: 'tracts',
        paint: { 'fill-color': SEQUENTIAL_BLUE[400], 'fill-opacity': 0.08 },
        layout: { visibility: 'none' },
      })
      map.addLayer({
        id: 'tract-line',
        type: 'line',
        source: 'tracts',
        paint: { 'line-color': SEQUENTIAL_BLUE[400], 'line-width': 1.5 },
        layout: { visibility: 'none' },
      })

      map.addSource('records', {
        type: 'geojson',
        data: pointsToGeoJson([]),
        cluster: true,
        clusterMaxZoom: 14,
        clusterRadius: 50,
      })

      map.addLayer({
        id: 'clusters',
        type: 'circle',
        source: 'records',
        filter: ['has', 'point_count'],
        paint: {
          'circle-color': '#4b5563',
          'circle-radius': ['step', ['get', 'point_count'], 16, 25, 20, 100, 26],
          'circle-opacity': 0.85,
        },
      })
      map.addLayer({
        id: 'cluster-count',
        type: 'symbol',
        source: 'records',
        filter: ['has', 'point_count'],
        layout: { 'text-field': '{point_count_abbreviated}', 'text-size': 12 },
        paint: { 'text-color': '#ffffff' },
      })
      map.addLayer({
        id: 'unclustered-point',
        type: 'circle',
        source: 'records',
        filter: ['!', ['has', 'point_count']],
        paint: {
          'circle-color': '#111827',
          'circle-radius': 6,
          'circle-stroke-width': 2,
          'circle-stroke-color': '#ffffff',
        },
      })

      map.on('click', 'clusters', async (e) => {
        const feature = e.features?.[0]
        if (!feature) return
        const clusterId = feature.properties?.cluster_id
        const source = map.getSource('records') as maplibregl.GeoJSONSource
        const zoom = await source.getClusterExpansionZoom(clusterId)
        const [lng, lat] = (feature.geometry as GeoJSON.Point).coordinates
        map.easeTo({ center: [lng, lat], zoom })
      })

      map.on('click', 'unclustered-point', (e) => {
        const feature = e.features?.[0]
        if (!feature) return
        const [lng, lat] = (feature.geometry as GeoJSON.Point).coordinates
        const { id, externalId, category, censusTract, censusGeoid } = feature.properties as {
          id: number; externalId: string | null; category: string | null
          censusTract: string | null; censusGeoid: string | null
        }
        showPopup(map, [lng, lat], id, externalId, category, censusTract, censusGeoid)
        setSelectedRecordId(id)
      })

      map.on('click', 'tract-fill', (e) => {
        // A record marker sitting on top of this tract already has its own
        // click handler/popup above — don't fight it with a second popup.
        const pointHit = map.queryRenderedFeatures(e.point, { layers: ['unclustered-point', 'clusters'] })
        if (pointHit.length > 0) return

        const feature = e.features?.[0]
        if (!feature) return
        showTractPopup(map, e.lngLat, feature.properties as TractFeatureProperties)
      })

      map.on('mouseenter', 'unclustered-point', () => (map.getCanvas().style.cursor = 'pointer'))
      map.on('mouseleave', 'unclustered-point', () => (map.getCanvas().style.cursor = ''))
      map.on('mouseenter', 'clusters', () => (map.getCanvas().style.cursor = 'pointer'))
      map.on('mouseleave', 'clusters', () => (map.getCanvas().style.cursor = ''))
      map.on('mouseenter', 'tract-fill', () => (map.getCanvas().style.cursor = 'pointer'))
      map.on('mouseleave', 'tract-fill', () => (map.getCanvas().style.cursor = ''))

      setMapLoaded(true)
    })

    mapRef.current = map
    return () => {
      map.remove()
      mapRef.current = null
    }
    // MapLibre layer and handler registration is a one-time setup for this component.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Keep the map's data in sync with the fetched points.
  useEffect(() => {
    const map = mapRef.current
    if (!map || !mapLoaded || !pointsQuery.data) return

    const source = map.getSource('records') as maplibregl.GeoJSONSource | undefined
    source?.setData(pointsToGeoJson(pointsQuery.data))
    fitMapToPoints(map, pointsQuery.data)
  }, [mapLoaded, pointsQuery.data])

  const choroplethValues = (choroplethQuery.data ?? [])
    .map((d) => d.value)
    .filter((v): v is number => v != null)
  const choroplethMin = choroplethValues.length > 0 ? Math.min(...choroplethValues) : 0
  const choroplethMax = choroplethValues.length > 0 ? Math.max(...choroplethValues) : 0

  // Keep the tract boundary layer's data (including the chosen choropleth
  // metric's value per tract) in sync with the fetched boundaries/metrics.
  useEffect(() => {
    const map = mapRef.current
    if (!map || !mapLoaded) return

    const metricByGeoid = new Map((choroplethQuery.data ?? []).map((d) => [d.geoid, d.value]))
    const source = map.getSource('tracts') as maplibregl.GeoJSONSource | undefined
    const featureCollection: GeoJSON.FeatureCollection = {
      type: 'FeatureCollection',
      features: (boundariesQuery.data ?? []).map((t) => ({
        type: 'Feature',
        geometry: t.boundary,
        properties: {
          geoid: t.geoid,
          name: t.tractName,
          population: t.demographics?.population ?? null,
          medianHouseholdIncome: t.demographics?.medianHouseholdIncome ?? null,
          povertyRate: t.demographics?.povertyRate ?? null,
          medianAge: t.demographics?.medianAge ?? null,
          ownerOccupiedPercent: t.demographics?.ownerOccupiedPercent ?? null,
          renterOccupiedPercent: t.demographics?.renterOccupiedPercent ?? null,
          minorityPopulationPercent: t.demographics?.minorityPopulationPercent ?? null,
          surveyYear: t.demographics?.surveyYear ?? null,
          isEstimated: t.demographics?.isEstimated ?? true,
          metricValue: metricByGeoid.get(t.geoid) ?? null,
          incomeTier: t.incomeLevel?.tier ?? null,
          tractMedianFamilyIncome: t.incomeLevel?.tractMedianFamilyIncome ?? null,
          areaMedianFamilyIncome: t.incomeLevel?.areaMedianFamilyIncome ?? null,
          incomePercentageOfAreaMedian: t.incomeLevel?.incomePercentageOfAreaMedian ?? null,
          msaMdName: t.incomeLevel?.msaMdName ?? null,
        },
      })),
    }
    source?.setData(featureCollection)
  }, [mapLoaded, boundariesQuery.data, choroplethQuery.data])

  // Toggle the tract layer's visibility without re-adding/removing it.
  useEffect(() => {
    const map = mapRef.current
    if (!map || !mapLoaded) return

    const visibility = showTracts ? 'visible' : 'none'
    if (map.getLayer('tract-fill')) map.setLayoutProperty('tract-fill', 'visibility', visibility)
    if (map.getLayer('tract-line')) map.setLayoutProperty('tract-line', 'visibility', visibility)
  }, [showTracts, mapLoaded])

  // Switch the tract fill between its flat outline color and a data-driven
  // choropleth gradient based on the selected metric.
  useEffect(() => {
    const map = mapRef.current
    if (!map || !mapLoaded || !map.getLayer('tract-fill')) return

    if (choroplethMetric && choroplethValues.length > 0) {
      // IncomeLevel is a fixed 4-tier scale (Low..Upper), not a continuous
      // measurement — its color must stay absolute (Low always lightest,
      // Upper always darkest) rather than stretched to whatever tiers happen
      // to be present in the current view, or a county with only Middle and
      // Upper tracts would misleadingly render its Middle tract as if it
      // were the "lightest/best" end of the scale.
      const domainMin = choroplethMetric === 'IncomeLevel' ? 1 : choroplethMin
      const domainMax = choroplethMetric === 'IncomeLevel' ? 4 : (choroplethMax > choroplethMin ? choroplethMax : choroplethMin + 1)
      map.setPaintProperty('tract-fill', 'fill-color', [
        'case',
        ['==', ['typeof', ['get', 'metricValue']], 'number'],
        ['interpolate', ['linear'], ['get', 'metricValue'], domainMin, SEQUENTIAL_BLUE[100], domainMax, SEQUENTIAL_BLUE[700]],
        '#d1d5db',
      ])
      map.setPaintProperty('tract-fill', 'fill-opacity', 0.6)
    } else {
      map.setPaintProperty('tract-fill', 'fill-color', SEQUENTIAL_BLUE[400])
      map.setPaintProperty('tract-fill', 'fill-opacity', 0.08)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mapLoaded, choroplethMetric, choroplethMin, choroplethMax, choroplethValues.length])

  function showPopup(
    map: maplibregl.Map, coordinates: [number, number], id: number, externalId: string | null, category: string | null,
    censusTract: string | null, censusGeoid: string | null,
  ) {
    popupRef.current?.remove()
    popupRef.current = new maplibregl.Popup({ closeButton: true })
      .setLngLat(coordinates)
      .setHTML(
        `<div style="font-size:13px">${externalId ? `<strong>${escapeHtml(externalId)}</strong><br/>` : ''}${
          category ? escapeHtml(category) : `Record #${id}`
        }${censusGeoid ? `<br/><span style="color:#6b7280">Tract ${escapeHtml(censusTract ?? '')} · GEOID ${escapeHtml(censusGeoid)}</span>` : ''}</div>`,
      )
      .addTo(map)
  }

  function showTractPopup(map: maplibregl.Map, coordinates: maplibregl.LngLatLike, tract: TractFeatureProperties) {
    popupRef.current?.remove()

    const rows: string[] = []
    if (tract.population != null) rows.push(`Population: ${tract.population.toLocaleString()}`)
    if (tract.medianHouseholdIncome != null) rows.push(`Median household income: $${Math.round(tract.medianHouseholdIncome).toLocaleString()}`)
    if (tract.povertyRate != null) rows.push(`Poverty rate: ${Number(tract.povertyRate).toFixed(1)}%`)
    if (tract.medianAge != null) rows.push(`Median age: ${Number(tract.medianAge).toFixed(1)}`)
    if (tract.ownerOccupiedPercent != null) rows.push(`Owner-occupied: ${Number(tract.ownerOccupiedPercent).toFixed(1)}%`)
    if (tract.renterOccupiedPercent != null) rows.push(`Renter-occupied: ${Number(tract.renterOccupiedPercent).toFixed(1)}%`)
    if (tract.minorityPopulationPercent != null) rows.push(`Minority population: ${Number(tract.minorityPopulationPercent).toFixed(1)}%`)
    if (tract.incomeTier != null && tract.incomeTier !== 'Unknown') {
      const color = INCOME_TIER_COLOR[tract.incomeTier]
      const pct = tract.incomePercentageOfAreaMedian != null ? `${Number(tract.incomePercentageOfAreaMedian).toFixed(0)}% of` : 'vs.'
      const areaMfi = tract.areaMedianFamilyIncome != null ? `$${Math.round(tract.areaMedianFamilyIncome).toLocaleString()}` : 'area'
      const area = tract.msaMdName && tract.msaMdName !== 'N/A' ? ` (${escapeHtml(tract.msaMdName)})` : ' (non-metro area)'
      rows.push(
        `LMI classification: <span style="display:inline-block;width:8px;height:8px;border-radius:50%;background:${color};margin-right:4px"></span>` +
          `${tract.incomeTier} income — ${pct} ${areaMfi} median family income${area}`,
      )
    }
    if (tract.metricValue != null && choroplethMetric && choroplethMetric !== 'IncomeLevel') {
      rows.push(`${CHOROPLETH_METRIC_LABELS[choroplethMetric]}: ${formatMetricValue(choroplethMetric, tract.metricValue)}`)
    }

    const body = rows.length > 0
      ? `${rows.map((r) => `<div>${r}</div>`).join('')}${
          tract.isEstimated
            ? '<div style="margin-top:4px;color:#9ca3af;font-size:11px">Estimated (sample data)</div>'
            : '<div style="margin-top:4px;color:#9ca3af;font-size:11px">Real Census ACS data</div>'
        }`
      : `<div style="color:#6b7280">No demographic data yet — import tract boundaries to generate estimates.</div>`

    popupRef.current = new maplibregl.Popup({ closeButton: true })
      .setLngLat(coordinates)
      .setHTML(
        `<div style="font-size:13px"><strong>${escapeHtml(tract.name ?? 'Census tract')}</strong><br/>` +
          `<span style="color:#6b7280">GEOID ${escapeHtml(tract.geoid)}</span>` +
          `<div style="margin-top:6px">${body}</div></div>`,
      )
      .addTo(map)
  }

  const flyToRecord = (recordId: number) => {
    const point = pointsQuery.data?.find((p) => p.id === recordId)
    const map = mapRef.current
    if (!point || !map) return
    map.flyTo({ center: [point.longitude, point.latitude], zoom: 14 })
    showPopup(map, [point.longitude, point.latitude], point.id, point.externalId, point.category, point.censusTract, point.censusGeoid)
    setSelectedRecordId(recordId)
  }

  const toggleSort = (key: string) => {
    if (sortBy === key) {
      setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'))
    } else {
      setSortBy(key)
      setSortDir('asc')
    }
    setPage(1)
  }

  const totalPages = recordsQuery.data ? Math.max(1, Math.ceil(recordsQuery.data.totalCount / PAGE_SIZE)) : 1
  const geocodedCount = pointsQuery.data?.length ?? 0
  const totalCount = recordsQuery.data?.totalCount ?? 0

  return (
    <div className="grid grid-cols-1 items-start gap-4 md:grid-cols-[260px_1fr]">
      <RecordFilterSidebar
        description="Narrows the map, table, and choropleth together."
        onChange={handleFiltersChange}
        resetSignal={filterResetSignal}
      />

      <Card className="min-w-0">
        <CardHeader>
          <CardTitle>Map</CardTitle>
          <CardDescription>
            {totalCount === 0 ? (
              <>
                No records yet —{' '}
                <Link
                  to={`/organizations/${organization.id}/datasets/${dataset.id}/imports/new`}
                  className="underline underline-offset-4"
                >
                  import a CSV
                </Link>{' '}
                to add some.
              </>
            ) : (
              `${geocodedCount} of ${totalCount} filtered records have coordinates.`
            )}
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          {(pointsQuery.isPending || recordsQuery.isPending) && <LoadingState />}

          {(pointsQuery.isError || recordsQuery.isError) && (
            <ErrorState message="Unable to load records for this dataset." />
          )}

          {showTracts && (boundariesQuery.isError || choroplethQuery.isError || tractStatusQuery.isError) && (
            <ErrorState message="Unable to load tract boundaries." />
          )}

          <div className="flex flex-wrap items-center gap-4">
            <label className="flex items-center gap-2 text-sm text-muted-foreground">
              <Checkbox checked={showTracts} onCheckedChange={(checked) => setShowTracts(checked === true)} />
              Show tract boundaries
            </label>

            {showTracts && (
              <div className="flex items-center gap-2">
                <Label className="text-sm text-muted-foreground">Color by</Label>
                <Select
                  value={choroplethMetric || '__none__'}
                  onValueChange={(v) => setChoroplethMetric(v === '__none__' ? '' : (v as ChoroplethMetric))}
                >
                  <SelectTrigger size="sm" className="w-56">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="__none__">None (outline only)</SelectItem>
                    {CHOROPLETH_METRICS.map((metric) => (
                      <SelectItem key={metric} value={metric}>
                        {CHOROPLETH_METRIC_LABELS[metric]}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            )}

            <div className="ml-auto flex flex-wrap items-center gap-2">
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  const map = mapRef.current
                  if (map) fitMapToPoints(map, pointsQuery.data ?? [])
                }}
              >
                Reset view
              </Button>

              {canManage && (
                <Select value="" onValueChange={(v) => handleExport(v as 'Csv' | 'Xlsx' | 'GeoJson' | 'Png')} disabled={isExporting}>
                  <SelectTrigger size="sm" className="w-32">
                    <SelectValue placeholder={isExporting ? 'Exporting…' : 'Export'} />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Csv">CSV</SelectItem>
                    <SelectItem value="Xlsx">Excel (XLSX)</SelectItem>
                    <SelectItem value="GeoJson">GeoJSON</SelectItem>
                    <SelectItem value="Png">PNG (map image)</SelectItem>
                  </SelectContent>
                </Select>
              )}

              {isSavingView ? (
                <>
                  <Input
                    autoFocus
                    placeholder="View name…"
                    value={newViewName}
                    onChange={(e) => setNewViewName(e.target.value)}
                    className="h-8 w-40"
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' && newViewName.trim()) createSavedViewMutation.mutate(newViewName.trim())
                      if (e.key === 'Escape') setIsSavingView(false)
                    }}
                  />
                  <Button
                    size="sm"
                    disabled={!newViewName.trim() || createSavedViewMutation.isPending}
                    onClick={() => createSavedViewMutation.mutate(newViewName.trim())}
                  >
                    {createSavedViewMutation.isPending ? 'Saving…' : 'Save'}
                  </Button>
                  <Button variant="outline" size="sm" onClick={() => setIsSavingView(false)}>
                    Cancel
                  </Button>
                </>
              ) : (
                <Button variant="outline" size="sm" onClick={() => setIsSavingView(true)}>
                  Save current view
                </Button>
              )}
            </div>
          </div>

          {createSavedViewMutation.isError && (
            <ErrorState
              message={
                createSavedViewMutation.error instanceof ApiError
                  ? createSavedViewMutation.error.message
                  : 'Unable to save this view.'
              }
            />
          )}

          {exportError && <ErrorState message={exportError} />}

          {showTracts && tractStatusQuery.data && !tractStatusQuery.data.allImported && (
            <div className="flex flex-col gap-2 rounded-lg border p-3 text-sm">
              {importTractsMutation.isSuccess ? (
                <p className="text-muted-foreground">
                  {tractStatusQuery.data.neededCounties.length > 0
                    ? `Importing tract boundaries for ${tractStatusQuery.data.neededCounties.length} ${tractStatusQuery.data.neededCounties.length === 1 ? 'county' : 'counties'}…`
                    : 'Matching records to Census tracts…'}
                </p>
              ) : (
                <>
                  <p className="text-muted-foreground">
                    {tractStatusQuery.data.neededCounties.length > 0 ? (
                      <>
                        {tractStatusQuery.data.importedCounties.length > 0
                          ? 'Showing tract boundaries for the counties already imported below. '
                          : ''}
                        Still missing this dataset's{' '}
                        {tractStatusQuery.data.neededCounties.length === 1 ? 'county' : 'counties'} (
                        {tractStatusQuery.data.neededCounties.map((c) => `${c.stateFips}-${c.countyFips}`).join(', ')}).
                      </>
                    ) : (
                      "Some records have coordinates but haven't been matched to a Census tract yet."
                    )}
                  </p>
                  {canManage && (
                    <Button
                      size="sm"
                      className="w-fit"
                      disabled={importTractsMutation.isPending}
                      onClick={() => importTractsMutation.mutate()}
                    >
                      {importTractsMutation.isPending
                        ? 'Requesting…'
                        : tractStatusQuery.data.neededCounties.length > 0
                          ? 'Import Tract Boundaries'
                          : 'Match Records to Tracts'}
                    </Button>
                  )}
                  {importTractsMutation.isError && (
                    <ErrorState
                      message={
                        importTractsMutation.error instanceof ApiError
                          ? importTractsMutation.error.message
                          : 'Unable to request the import.'
                      }
                    />
                  )}
                </>
              )}
            </div>
          )}

          {showTracts && tractStatusQuery.data?.hasEstimatedDemographics && (
            <div className="flex flex-col gap-2 rounded-lg border p-3 text-sm">
              <p className="text-muted-foreground">
                Some tract demographics shown here are synthetic sample data, not real Census figures.
              </p>
              {canManage && (
                <Button
                  size="sm"
                  variant="outline"
                  className="w-fit"
                  disabled={refreshDemographicsMutation.isPending}
                  onClick={() => refreshDemographicsMutation.mutate()}
                >
                  {refreshDemographicsMutation.isPending ? 'Refreshing…' : 'Refresh Demographics'}
                </Button>
              )}
              {refreshDemographicsMutation.isError && (
                <ErrorState
                  message={
                    refreshDemographicsMutation.error instanceof ApiError
                      ? refreshDemographicsMutation.error.message
                      : 'Unable to refresh demographics.'
                  }
                />
              )}
            </div>
          )}

          <div ref={containerRef} className="h-[420px] w-full overflow-hidden rounded-lg border" />

          {showTracts && choroplethMetric && choroplethValues.length > 0 && (
            <div className="flex items-center gap-3 text-xs text-muted-foreground">
              <span>{CHOROPLETH_METRIC_LABELS[choroplethMetric]}:</span>
              <div
                className="h-3 w-32 rounded"
                style={{ background: `linear-gradient(to right, ${SEQUENTIAL_BLUE[100]}, ${SEQUENTIAL_BLUE[700]})` }}
              />
              <span>
                {choroplethMetric === 'IncomeLevel'
                  ? 'Low – Upper'
                  : `${formatMetricValue(choroplethMetric, choroplethMin)} – ${formatMetricValue(choroplethMetric, choroplethMax)}`}
              </span>
              <span className="text-muted-foreground/70">(estimated where demographic-based)</span>
            </div>
          )}

          {totalCount > 0 && geocodedCount === 0 && (
            <p className="text-sm text-muted-foreground">
              No records have coordinates yet.{' '}
              <Link
                to={`/organizations/${organization.id}/datasets/${dataset.id}/geocoding`}
                className="underline underline-offset-4"
              >
                Start geocoding
              </Link>{' '}
              to look them up, or include
              <code className="mx-1 rounded bg-muted px-1">latitude</code>/<code className="mx-1 rounded bg-muted px-1">longitude</code>
              columns next time you import a CSV.
            </p>
          )}

          <div className="overflow-x-auto rounded-md border">
            <table className="w-full text-left text-sm">
              <thead className="bg-muted/50">
                <tr>
                  {columns.map((col) => (
                    <th key={col.key} className="whitespace-nowrap px-3 py-2 font-medium">
                      <button
                        type="button"
                        className="flex items-center gap-1 hover:underline"
                        onClick={() => toggleSort(col.key)}
                      >
                        {col.label}
                        {sortBy === col.key && (sortDir === 'asc' ? '▲' : '▼')}
                      </button>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {recordsQuery.data?.items.length === 0 && (
                  <tr>
                    <td colSpan={columns.length} className="px-3 py-6 text-center text-sm text-muted-foreground">
                      No records match the current filters.
                    </td>
                  </tr>
                )}
                {recordsQuery.data?.items.map((record) => (
                  <tr
                    key={record.id}
                    onClick={() => flyToRecord(record.id)}
                    className={`cursor-pointer border-t hover:bg-muted/50 ${
                      selectedRecordId === record.id ? 'bg-muted' : ''
                    }`}
                  >
                    <td className="px-3 py-2">{record.externalId ?? '—'}</td>
                    <td className="px-3 py-2">
                      {record.city}, {record.state}
                    </td>
                    <td className="px-3 py-2">{record.amount != null ? `$${record.amount.toLocaleString()}` : '—'}</td>
                    <td className="px-3 py-2">{record.recordDate ?? '—'}</td>
                    <td className="px-3 py-2 text-muted-foreground">{record.geocodeStatus}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="flex items-center justify-between text-sm text-muted-foreground">
            <span>
              Page {page} of {totalPages}
            </span>
            <div className="flex gap-2">
              <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
                Previous
              </Button>
              <Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>
                Next
              </Button>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}

function escapeHtml(value: string): string {
  const div = document.createElement('div')
  div.textContent = value
  return div.innerHTML
}
