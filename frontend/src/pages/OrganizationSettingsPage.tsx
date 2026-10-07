import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as maplibregl from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useOutletContext } from 'react-router-dom'
import { z } from 'zod'
import { addAssessmentAreaCounty, getAssessmentAreaCounties, removeAssessmentAreaCounty } from '@/api/assessmentArea'
import { getAuditEvents } from '@/api/auditEvents'
import { getCountyBoundaries, getImportedCounties, requestCountyImport } from '@/api/censusData'
import {
  addOrganizationMember,
  changeOrganizationMemberRole,
  getOrganizationMembers,
  removeOrganizationMember,
  setCensusApiKey,
  type OrganizationMember,
  type OrganizationRole,
} from '@/api/organizations'
import type { TractBoundary, TractIncomeTier } from '@/api/tracts'
import { ApiError } from '@/api/client'
import { US_COUNTIES } from '@/data/usCounties'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateViews'
import { SEQUENTIAL_BLUE } from '@/lib/chartColors'
import { MAP_STYLE } from '@/lib/mapStyle'
import type { OrgOutletContext } from './OrganizationLayout'

const US_STATES = Array.from(new Map(US_COUNTIES.map((c) => [c.stateFips, c])).values())
  .map((c) => ({ stateFips: c.stateFips, stateName: c.stateName }))
  .sort((a, b) => a.stateName.localeCompare(b.stateName))

type CountyMetric = 'population' | 'medianHouseholdIncome' | 'povertyRate' | 'minorityPopulationPercent' | 'incomeLevel'

const COUNTY_METRIC_LABELS: Record<CountyMetric, string> = {
  population: 'Population',
  medianHouseholdIncome: 'Median household income',
  povertyRate: 'Poverty rate',
  minorityPopulationPercent: 'Minority population %',
  incomeLevel: 'LMI classification (Low/Mod/Mid/Upper)',
}

// Ordinal 1-4, matching the backend's TractIncomeTier enum order — this map
// has no dataset-choropleth endpoint to source that ordinal from (unlike
// DatasetMapPage), so it's computed client-side from the tier string here.
const INCOME_TIER_ORDINAL: Record<Exclude<TractIncomeTier, 'Unknown'>, number> = {
  Low: 1, Moderate: 2, Middle: 3, Upper: 4,
}
const INCOME_TIER_COLOR: Record<Exclude<TractIncomeTier, 'Unknown'>, string> = {
  Low: SEQUENTIAL_BLUE[100], Moderate: SEQUENTIAL_BLUE[300], Middle: SEQUENTIAL_BLUE[500], Upper: SEQUENTIAL_BLUE[700],
}
const INCOME_LEVEL_ORDINAL_LABELS = ['', 'Low', 'Moderate', 'Middle', 'Upper']

function getCountyMetricValue(t: TractBoundary, metric: CountyMetric): number | null {
  if (metric === 'incomeLevel') {
    const tier = t.incomeLevel?.tier
    return tier && tier !== 'Unknown' ? INCOME_TIER_ORDINAL[tier] : null
  }
  return t.demographics?.[metric] ?? null
}

function formatCountyMetricValue(metric: CountyMetric, value: number): string {
  if (metric === 'medianHouseholdIncome') return `$${Math.round(value).toLocaleString()}`
  if (metric === 'povertyRate' || metric === 'minorityPopulationPercent') return `${value.toFixed(1)}%`
  if (metric === 'incomeLevel') return INCOME_LEVEL_ORDINAL_LABELS[Math.round(value)] ?? value.toLocaleString()
  return value.toLocaleString()
}

function escapeHtml(value: string): string {
  const div = document.createElement('div')
  div.textContent = value
  return div.innerHTML
}

const ROLES: OrganizationRole[] = ['Administrator', 'Analyst', 'Viewer']

const addMemberSchema = z.object({
  email: z.string().email('Enter a valid email address.'),
  role: z.enum(['Administrator', 'Analyst', 'Viewer']),
})

type AddMemberFormValues = z.infer<typeof addMemberSchema>

function errorMessage(error: unknown, fallback: string) {
  return error instanceof ApiError ? error.message : fallback
}

/**
 * A county-scoped, dataset-independent map — no record points/clustering,
 * since there are no records here, just real tract boundaries + demographics.
 * Mounted fresh (via a `key` on the parent) whenever the selected county
 * changes, so the map-init effect's "run once" assumption stays true per
 * selection rather than needing to detect a county change mid-lifetime.
 */
function CountyBoundaryMap({
  organizationId, stateFips, countyFips, countyName,
}: {
  organizationId: number
  stateFips: string
  countyFips: string
  countyName: string
}) {
  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<maplibregl.Map | null>(null)
  const popupRef = useRef<maplibregl.Popup | null>(null)
  const [mapLoaded, setMapLoaded] = useState(false)
  const [metric, setMetric] = useState<CountyMetric>('population')

  const boundariesQuery = useQuery({
    queryKey: ['organizations', organizationId, 'census-data', 'county-boundaries', stateFips, countyFips],
    queryFn: () => getCountyBoundaries(organizationId, stateFips, countyFips),
  })

  useEffect(() => {
    if (!containerRef.current || mapRef.current) return

    const map = new maplibregl.Map({ container: containerRef.current, style: MAP_STYLE, center: [-98.5, 39.8], zoom: 3 })
    map.addControl(new maplibregl.NavigationControl(), 'top-right')

    map.on('load', () => {
      map.addSource('county-tracts', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } })
      map.addLayer({
        id: 'county-fill', type: 'fill', source: 'county-tracts',
        paint: { 'fill-color': SEQUENTIAL_BLUE[400], 'fill-opacity': 0.6 },
      })
      map.addLayer({
        id: 'county-line', type: 'line', source: 'county-tracts',
        paint: { 'line-color': SEQUENTIAL_BLUE[700], 'line-width': 1 },
      })

      map.on('click', 'county-fill', (e) => {
        const feature = e.features?.[0]
        if (!feature) return
        const props = feature.properties as Record<string, number | string | boolean | null>

        const rows: string[] = []
        if (props.population != null) rows.push(`Population: ${Number(props.population).toLocaleString()}`)
        if (props.medianHouseholdIncome != null) rows.push(`Median household income: $${Math.round(Number(props.medianHouseholdIncome)).toLocaleString()}`)
        if (props.povertyRate != null) rows.push(`Poverty rate: ${Number(props.povertyRate).toFixed(1)}%`)
        if (props.medianAge != null) rows.push(`Median age: ${Number(props.medianAge).toFixed(1)}`)
        if (props.ownerOccupiedPercent != null) rows.push(`Owner-occupied: ${Number(props.ownerOccupiedPercent).toFixed(1)}%`)
        if (props.renterOccupiedPercent != null) rows.push(`Renter-occupied: ${Number(props.renterOccupiedPercent).toFixed(1)}%`)
        if (props.minorityPopulationPercent != null) rows.push(`Minority population: ${Number(props.minorityPopulationPercent).toFixed(1)}%`)
        if (props.incomeTier != null && props.incomeTier !== 'Unknown') {
          const tier = props.incomeTier as Exclude<TractIncomeTier, 'Unknown'>
          const color = INCOME_TIER_COLOR[tier]
          const pct = props.incomePercentageOfAreaMedian != null ? `${Number(props.incomePercentageOfAreaMedian).toFixed(0)}% of` : 'vs.'
          const areaMfi = props.areaMedianFamilyIncome != null ? `$${Math.round(Number(props.areaMedianFamilyIncome)).toLocaleString()}` : 'area'
          const area = props.msaMdName && props.msaMdName !== 'N/A' ? ` (${escapeHtml(String(props.msaMdName))})` : ' (non-metro area)'
          rows.push(
            `LMI classification: <span style="display:inline-block;width:8px;height:8px;border-radius:50%;background:${color};margin-right:4px"></span>` +
              `${tier} income — ${pct} ${areaMfi} median family income${area}`,
          )
        }

        const body = rows.length > 0
          ? `${rows.map((r) => `<div>${r}</div>`).join('')}<div style="margin-top:4px;color:#9ca3af;font-size:11px">${
              props.isEstimated ? 'Estimated (sample data)' : 'Real Census ACS data'
            }</div>`
          : `<div style="color:#6b7280">No demographic data for this tract.</div>`

        popupRef.current?.remove()
        popupRef.current = new maplibregl.Popup({ closeButton: true })
          .setLngLat(e.lngLat)
          .setHTML(
            `<div style="font-size:13px"><strong>${escapeHtml(String(props.name ?? 'Census tract'))}</strong><br/>` +
              `<span style="color:#6b7280">GEOID ${escapeHtml(String(props.geoid))}</span>` +
              `<div style="margin-top:6px">${body}</div></div>`,
          )
          .addTo(map)
      })
      map.on('mouseenter', 'county-fill', () => (map.getCanvas().style.cursor = 'pointer'))
      map.on('mouseleave', 'county-fill', () => (map.getCanvas().style.cursor = ''))

      setMapLoaded(true)
    })

    mapRef.current = map
    return () => {
      map.remove()
      mapRef.current = null
    }
  }, [])

  useEffect(() => {
    const map = mapRef.current
    if (!map || !mapLoaded || !boundariesQuery.data) return

    const values = boundariesQuery.data.map((t) => getCountyMetricValue(t, metric)).filter((v): v is number => v != null)
    const min = values.length > 0 ? Math.min(...values) : 0
    const max = values.length > 0 ? Math.max(...values) : 0

    const featureCollection: GeoJSON.FeatureCollection = {
      type: 'FeatureCollection',
      features: boundariesQuery.data.map((t: TractBoundary) => ({
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
          isEstimated: t.demographics?.isEstimated ?? true,
          metricValue: getCountyMetricValue(t, metric),
          incomeTier: t.incomeLevel?.tier ?? null,
          tractMedianFamilyIncome: t.incomeLevel?.tractMedianFamilyIncome ?? null,
          areaMedianFamilyIncome: t.incomeLevel?.areaMedianFamilyIncome ?? null,
          incomePercentageOfAreaMedian: t.incomeLevel?.incomePercentageOfAreaMedian ?? null,
          msaMdName: t.incomeLevel?.msaMdName ?? null,
        },
      })),
    }
    const source = map.getSource('county-tracts') as maplibregl.GeoJSONSource | undefined
    source?.setData(featureCollection)

    // IncomeLevel is a fixed 4-tier scale (Low..Upper), not a continuous
    // measurement — its color must stay absolute rather than stretched to
    // whatever tiers happen to be present in this one county, or a county
    // with only Middle/Upper tracts would misleadingly render Middle as if
    // it were the "lightest/best" end of the scale.
    const domainMin = metric === 'incomeLevel' ? 1 : min
    const domainMax = metric === 'incomeLevel' ? 4 : (max > min ? max : min + 1)
    map.setPaintProperty('county-fill', 'fill-color', [
      'case',
      ['==', ['typeof', ['get', 'metricValue']], 'number'],
      ['interpolate', ['linear'], ['get', 'metricValue'], domainMin, SEQUENTIAL_BLUE[100], domainMax, SEQUENTIAL_BLUE[700]],
      '#d1d5db',
    ])

    const bounds = new maplibregl.LngLatBounds()
    boundariesQuery.data.forEach((t) => {
      const coords = t.boundary.type === 'MultiPolygon' ? t.boundary.coordinates.flat(2) : []
      coords.forEach((c) => bounds.extend(c as [number, number]))
    })
    if (!bounds.isEmpty()) map.fitBounds(bounds, { padding: 30 })
  }, [mapLoaded, boundariesQuery.data, metric])

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between gap-2">
        <p className="text-sm font-medium">{countyName}</p>
        <div className="flex items-center gap-2">
          <Label className="text-sm text-muted-foreground">Color by</Label>
          <Select value={metric} onValueChange={(v) => setMetric(v as CountyMetric)}>
            <SelectTrigger size="sm" className="w-56">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {(Object.keys(COUNTY_METRIC_LABELS) as CountyMetric[]).map((m) => (
                <SelectItem key={m} value={m}>
                  {COUNTY_METRIC_LABELS[m]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>

      {boundariesQuery.isPending && <LoadingState />}
      {boundariesQuery.isError && <ErrorState message="Unable to load this county's boundaries." />}

      <div ref={containerRef} className="h-[360px] w-full overflow-hidden rounded-lg border" />

      {boundariesQuery.data && boundariesQuery.data.length > 0 && (() => {
        const values = boundariesQuery.data.map((t) => getCountyMetricValue(t, metric)).filter((v): v is number => v != null)
        if (values.length === 0) return null
        const min = Math.min(...values)
        const max = Math.max(...values)
        return (
          <div className="flex items-center gap-3 text-xs text-muted-foreground">
            <span>{COUNTY_METRIC_LABELS[metric]}:</span>
            <div className="h-3 w-32 rounded" style={{ background: `linear-gradient(to right, ${SEQUENTIAL_BLUE[100]}, ${SEQUENTIAL_BLUE[700]})` }} />
            <span>
              {metric === 'incomeLevel' ? 'Low – Upper' : `${formatCountyMetricValue(metric, min)} – ${formatCountyMetricValue(metric, max)}`}
            </span>
          </div>
        )
      })()}
    </div>
  )
}

export function OrganizationSettingsPage() {
  const { organization } = useOutletContext<OrgOutletContext>()
  const isAdmin = organization.myRole === 'Administrator'
  const queryClient = useQueryClient()
  const membersKey = ['organizations', organization.id, 'members']

  const membersQuery = useQuery({
    queryKey: membersKey,
    queryFn: () => getOrganizationMembers(organization.id),
  })

  const invalidateMembers = () => queryClient.invalidateQueries({ queryKey: membersKey })

  const AUDIT_PAGE_SIZE = 25
  const [auditPage, setAuditPage] = useState(1)
  const auditEventsQuery = useQuery({
    queryKey: ['organizations', organization.id, 'audit-events', auditPage],
    queryFn: () => getAuditEvents(organization.id, auditPage, AUDIT_PAGE_SIZE),
    enabled: isAdmin,
  })
  const auditTotalPages = auditEventsQuery.data ? Math.max(1, Math.ceil(auditEventsQuery.data.totalCount / AUDIT_PAGE_SIZE)) : 1

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<AddMemberFormValues>({
    resolver: zodResolver(addMemberSchema),
    defaultValues: { role: 'Viewer' },
  })

  const addMemberMutation = useMutation({
    mutationFn: (values: AddMemberFormValues) => addOrganizationMember(organization.id, values),
    onSuccess: () => {
      reset()
      invalidateMembers()
    },
  })

  const changeRoleMutation = useMutation({
    mutationFn: ({ userId, role }: { userId: number; role: OrganizationRole }) =>
      changeOrganizationMemberRole(organization.id, userId, role),
    onSuccess: invalidateMembers,
  })

  const removeMemberMutation = useMutation({
    mutationFn: (userId: number) => removeOrganizationMember(organization.id, userId),
    onSuccess: invalidateMembers,
  })

  const [censusApiKeyInput, setCensusApiKeyInput] = useState('')
  const censusApiKeyMutation = useMutation({
    mutationFn: (apiKey: string | null) => setCensusApiKey(organization.id, apiKey),
    onSuccess: () => {
      setCensusApiKeyInput('')
      queryClient.invalidateQueries({ queryKey: ['organizations', organization.id] })
    },
  })

  const [countyImportState, setCountyImportState] = useState('')
  const [countyImportCounty, setCountyImportCounty] = useState('')
  const [viewingCounty, setViewingCounty] = useState<{ stateFips: string; countyFips: string } | null>(null)
  const [pendingCountyImport, setPendingCountyImport] = useState<{ stateFips: string; countyFips: string } | null>(null)

  const importedCountiesKey = ['organizations', organization.id, 'census-data', 'imported-counties']
  const countyImportMutation = useMutation({
    mutationFn: (vars: { stateFips: string; countyFips: string }) =>
      requestCountyImport(organization.id, vars.stateFips, vars.countyFips),
    onMutate: (vars) => setPendingCountyImport(vars),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: importedCountiesKey }),
    onError: () => setPendingCountyImport(null),
  })

  const importedCountiesQuery = useQuery({
    queryKey: importedCountiesKey,
    queryFn: () => getImportedCounties(organization.id),
    enabled: isAdmin,
    // While waiting on a just-requested import, keep polling until it shows up
    // — otherwise the list only ever refreshes once, at request-acceptance
    // time, well before the background job has actually finished.
    refetchInterval: (query) => {
      if (!pendingCountyImport) return false
      const { stateFips, countyFips } = pendingCountyImport
      const alreadyThere = (query.state.data ?? []).some((c) => c.stateFips === stateFips && c.countyFips === countyFips)
      return alreadyThere ? false : 2000
    },
  })
  const importedCountyNames = useMemo(() => {
    const byFips = new Map(US_COUNTIES.map((c) => [`${c.stateFips}${c.countyFips}`, c]))
    return (importedCountiesQuery.data ?? [])
      .map((c) => byFips.get(`${c.stateFips}${c.countyFips}`))
      .filter((c) => c !== undefined)
  }, [importedCountiesQuery.data])

  const countiesForState = useMemo(
    () =>
      US_COUNTIES.filter((c) => c.stateFips === countyImportState).sort((a, b) => a.countyName.localeCompare(b.countyName)),
    [countyImportState],
  )

  const [assessmentAreaState, setAssessmentAreaState] = useState('')
  const [assessmentAreaCounty, setAssessmentAreaCounty] = useState('')
  const countiesForAssessmentAreaState = useMemo(
    () =>
      US_COUNTIES.filter((c) => c.stateFips === assessmentAreaState).sort((a, b) => a.countyName.localeCompare(b.countyName)),
    [assessmentAreaState],
  )

  const assessmentAreaKey = ['organizations', organization.id, 'assessment-area', 'counties']
  const assessmentAreaQuery = useQuery({
    queryKey: assessmentAreaKey,
    queryFn: () => getAssessmentAreaCounties(organization.id),
    enabled: isAdmin,
  })
  const assessmentAreaCountyNames = useMemo(() => {
    const byFips = new Map(US_COUNTIES.map((c) => [`${c.stateFips}${c.countyFips}`, c]))
    return (assessmentAreaQuery.data ?? [])
      .map((c) => byFips.get(`${c.stateFips}${c.countyFips}`))
      .filter((c) => c !== undefined)
  }, [assessmentAreaQuery.data])

  const invalidateAssessmentArea = () => queryClient.invalidateQueries({ queryKey: assessmentAreaKey })
  const addAssessmentAreaCountyMutation = useMutation({
    mutationFn: (vars: { stateFips: string; countyFips: string }) =>
      addAssessmentAreaCounty(organization.id, vars.stateFips, vars.countyFips),
    onSuccess: () => {
      setAssessmentAreaState('')
      setAssessmentAreaCounty('')
      invalidateAssessmentArea()
    },
  })
  const removeAssessmentAreaCountyMutation = useMutation({
    mutationFn: (vars: { stateFips: string; countyFips: string }) =>
      removeAssessmentAreaCounty(organization.id, vars.stateFips, vars.countyFips),
    onSuccess: invalidateAssessmentArea,
  })

  return (
    <div className="mx-auto flex w-full max-w-5xl flex-col gap-6">
      <Card>
        <CardHeader>
          <CardTitle className="text-xl font-semibold">{organization.name}</CardTitle>
          <CardDescription>
            Created {new Date(organization.createdAt).toLocaleDateString()}
          </CardDescription>
        </CardHeader>
      </Card>

      {isAdmin && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Census API Key</CardTitle>
            <CardDescription>
              Optional. Lets tract imports fetch real Census ACS demographic data instead of synthetic sample
              values. Get a free key at{' '}
              <a
                href="https://api.census.gov/data/key_signup.html"
                target="_blank"
                rel="noreferrer"
                className="underline underline-offset-4"
              >
                api.census.gov
              </a>
              .
            </CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-3">
            <p className="text-sm text-muted-foreground">
              {organization.hasCensusApiKey
                ? 'Configured — real demographic data is used where available.'
                : 'Not configured — demographic data shown across this organization is synthetic sample data.'}
            </p>
            <div className="flex flex-col gap-2 sm:flex-row sm:items-end">
              <div className="flex flex-1 flex-col gap-2">
                <Label htmlFor="census-api-key">API key</Label>
                <Input
                  id="census-api-key"
                  type="password"
                  autoComplete="off"
                  placeholder={organization.hasCensusApiKey ? '••••••••••••' : 'Enter your Census API key'}
                  value={censusApiKeyInput}
                  onChange={(e) => setCensusApiKeyInput(e.target.value)}
                />
              </div>
              <Button
                disabled={!censusApiKeyInput.trim() || censusApiKeyMutation.isPending}
                onClick={() => censusApiKeyMutation.mutate(censusApiKeyInput.trim())}
              >
                {censusApiKeyMutation.isPending ? 'Saving…' : 'Save'}
              </Button>
              {organization.hasCensusApiKey && (
                <Button
                  variant="outline"
                  disabled={censusApiKeyMutation.isPending}
                  onClick={() => censusApiKeyMutation.mutate(null)}
                >
                  Clear
                </Button>
              )}
            </div>
            {censusApiKeyMutation.isError && (
              <ErrorState message={errorMessage(censusApiKeyMutation.error, 'Unable to save the API key.')} />
            )}
          </CardContent>
        </Card>
      )}

      {isAdmin && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Census Reference Data</CardTitle>
            <CardDescription>
              Prepares real Census tract boundaries + demographics for a county ahead of time — no dataset needs to
              touch it first. Once a dataset's records later land in that county, its Map tab shows this data
              immediately with no re-import needed.
            </CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-3">
            <div className="flex flex-col gap-2 sm:flex-row sm:items-end">
              <div className="flex flex-1 flex-col gap-2">
                <Label htmlFor="county-import-state">State</Label>
                <Select
                  value={countyImportState}
                  onValueChange={(value) => {
                    setCountyImportState(value)
                    setCountyImportCounty('')
                  }}
                >
                  <SelectTrigger id="county-import-state">
                    <SelectValue placeholder="Choose a state…" />
                  </SelectTrigger>
                  <SelectContent>
                    {US_STATES.map((s) => (
                      <SelectItem key={s.stateFips} value={s.stateFips}>
                        {s.stateName}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>

              <div className="flex flex-1 flex-col gap-2">
                <Label htmlFor="county-import-county">County</Label>
                <Select value={countyImportCounty} onValueChange={setCountyImportCounty} disabled={!countyImportState}>
                  <SelectTrigger id="county-import-county">
                    <SelectValue placeholder="Choose a county…" />
                  </SelectTrigger>
                  <SelectContent>
                    {countiesForState.map((c) => (
                      <SelectItem key={c.countyFips} value={c.countyFips}>
                        {c.countyName}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>

              <Button
                disabled={!countyImportState || !countyImportCounty || countyImportMutation.isPending}
                onClick={() => countyImportMutation.mutate({ stateFips: countyImportState, countyFips: countyImportCounty })}
              >
                {countyImportMutation.isPending ? 'Importing…' : 'Import'}
              </Button>
            </div>

            {countyImportMutation.isError && (
              <ErrorState message={errorMessage(countyImportMutation.error, 'Unable to start the import.')} />
            )}
            {countyImportMutation.isSuccess && (
              <p className="text-sm text-muted-foreground">
                Importing — this runs in the background and usually finishes within a minute or two.
              </p>
            )}

            {importedCountiesQuery.isPending && <LoadingState />}
            {importedCountiesQuery.isError && (
              <ErrorState message={errorMessage(importedCountiesQuery.error, 'Unable to load imported counties.')} />
            )}
            {importedCountiesQuery.data && (
              <div className="flex flex-col gap-1">
                <Label>Already imported ({importedCountyNames.length}) — click one to view it</Label>
                {importedCountyNames.length === 0 ? (
                  <p className="text-sm text-muted-foreground">No counties imported yet.</p>
                ) : (
                  <div className="flex flex-wrap gap-1.5">
                    {importedCountyNames.map((c) => (
                      <button key={`${c!.stateFips}${c!.countyFips}`} type="button" className="cursor-pointer" onClick={() => setViewingCounty({ stateFips: c!.stateFips, countyFips: c!.countyFips })}>
                        <Badge variant={viewingCounty?.stateFips === c!.stateFips && viewingCounty?.countyFips === c!.countyFips ? 'default' : 'secondary'}>
                          {c!.countyName}, {c!.stateAbbr}
                        </Badge>
                      </button>
                    ))}
                  </div>
                )}
              </div>
            )}

            {viewingCounty && (() => {
              const county = US_COUNTIES.find(
                (c) => c.stateFips === viewingCounty.stateFips && c.countyFips === viewingCounty.countyFips,
              )
              return (
                <CountyBoundaryMap
                  key={`${viewingCounty.stateFips}-${viewingCounty.countyFips}`}
                  organizationId={organization.id}
                  stateFips={viewingCounty.stateFips}
                  countyFips={viewingCounty.countyFips}
                  countyName={county ? `${county.countyName}, ${county.stateAbbr}` : 'Selected county'}
                />
              )
            })()}
          </CardContent>
        </Card>
      )}

      {isAdmin && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Assessment Area</CardTitle>
            <CardDescription>
              The set of counties this organization has declared as its CRA service area. Gap Analysis compares each
              dataset's activity against it — independent of whether a county's Census data has been imported yet.
            </CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-3">
            <div className="flex flex-col gap-2 sm:flex-row sm:items-end">
              <div className="flex flex-1 flex-col gap-2">
                <Label htmlFor="assessment-area-state">State</Label>
                <Select
                  value={assessmentAreaState}
                  onValueChange={(value) => {
                    setAssessmentAreaState(value)
                    setAssessmentAreaCounty('')
                  }}
                >
                  <SelectTrigger id="assessment-area-state">
                    <SelectValue placeholder="Choose a state…" />
                  </SelectTrigger>
                  <SelectContent>
                    {US_STATES.map((s) => (
                      <SelectItem key={s.stateFips} value={s.stateFips}>
                        {s.stateName}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>

              <div className="flex flex-1 flex-col gap-2">
                <Label htmlFor="assessment-area-county">County</Label>
                <Select value={assessmentAreaCounty} onValueChange={setAssessmentAreaCounty} disabled={!assessmentAreaState}>
                  <SelectTrigger id="assessment-area-county">
                    <SelectValue placeholder="Choose a county…" />
                  </SelectTrigger>
                  <SelectContent>
                    {countiesForAssessmentAreaState.map((c) => (
                      <SelectItem key={c.countyFips} value={c.countyFips}>
                        {c.countyName}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>

              <Button
                disabled={!assessmentAreaState || !assessmentAreaCounty || addAssessmentAreaCountyMutation.isPending}
                onClick={() =>
                  addAssessmentAreaCountyMutation.mutate({ stateFips: assessmentAreaState, countyFips: assessmentAreaCounty })
                }
              >
                {addAssessmentAreaCountyMutation.isPending ? 'Adding…' : 'Add'}
              </Button>
            </div>

            {addAssessmentAreaCountyMutation.isError && (
              <ErrorState message={errorMessage(addAssessmentAreaCountyMutation.error, 'Unable to add this county.')} />
            )}
            {removeAssessmentAreaCountyMutation.isError && (
              <ErrorState message={errorMessage(removeAssessmentAreaCountyMutation.error, 'Unable to remove this county.')} />
            )}

            {assessmentAreaQuery.isPending && <LoadingState />}
            {assessmentAreaQuery.isError && (
              <ErrorState message={errorMessage(assessmentAreaQuery.error, 'Unable to load the assessment area.')} />
            )}
            {assessmentAreaQuery.data && (
              <div className="flex flex-col gap-1">
                <Label>Declared counties ({assessmentAreaCountyNames.length})</Label>
                {assessmentAreaCountyNames.length === 0 ? (
                  <EmptyState message="No counties declared yet — Gap Analysis has no assessment-area comparison until at least one is added." />
                ) : (
                  <div className="flex flex-col divide-y">
                    {assessmentAreaCountyNames.map((c) => (
                      <div key={`${c!.stateFips}${c!.countyFips}`} className="flex items-center justify-between gap-4 py-2">
                        <span className="text-sm">
                          {c!.countyName}, {c!.stateAbbr}
                        </span>
                        <Button
                          variant="outline"
                          size="sm"
                          disabled={removeAssessmentAreaCountyMutation.isPending}
                          onClick={() => removeAssessmentAreaCountyMutation.mutate({ stateFips: c!.stateFips, countyFips: c!.countyFips })}
                        >
                          Remove
                        </Button>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            )}
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Members</CardTitle>
          <CardDescription>People with access to this organization.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          {membersQuery.isPending && <LoadingState />}

          {membersQuery.isError && (
            <ErrorState message={errorMessage(membersQuery.error, 'Unable to load members.')} />
          )}
          {(changeRoleMutation.isError || removeMemberMutation.isError) && (
            <ErrorState
              message={errorMessage(
                changeRoleMutation.error ?? removeMemberMutation.error,
                'That action could not be completed.',
              )}
            />
          )}

          <div className="flex flex-col divide-y">
            {membersQuery.data?.map((member: OrganizationMember) => (
              <div key={member.userId} className="flex items-center justify-between gap-4 py-3">
                <div>
                  <p className="text-sm font-medium">{member.displayName}</p>
                  <p className="text-sm text-muted-foreground">{member.email}</p>
                </div>

                {isAdmin ? (
                  <div className="flex items-center gap-2">
                    <Select
                      value={member.role}
                      onValueChange={(role) =>
                        changeRoleMutation.mutate({ userId: member.userId, role: role as OrganizationRole })
                      }
                    >
                      <SelectTrigger size="sm" className="w-36">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {ROLES.map((role) => (
                          <SelectItem key={role} value={role}>
                            {role}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => removeMemberMutation.mutate(member.userId)}
                    >
                      Remove
                    </Button>
                  </div>
                ) : (
                  <Badge variant="secondary">{member.role}</Badge>
                )}
              </div>
            ))}
          </div>
        </CardContent>
      </Card>

      {isAdmin && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Add a member</CardTitle>
            <CardDescription>
              They need an existing GeoInsight account — invite-by-email isn't built yet.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <form
              className="flex flex-col gap-4 sm:flex-row sm:items-end"
              onSubmit={handleSubmit((values) => addMemberMutation.mutate(values))}
            >
              <div className="flex flex-1 flex-col gap-2">
                <Label htmlFor="email">Email</Label>
                <Input id="email" type="email" {...register('email')} />
                {errors.email && <p className="text-sm text-destructive">{errors.email.message}</p>}
              </div>

              <div className="flex flex-col gap-2">
                <Label htmlFor="role">Role</Label>
                <select
                  id="role"
                  className="h-9 rounded-md border bg-transparent px-3 text-sm"
                  {...register('role')}
                >
                  {ROLES.map((role) => (
                    <option key={role} value={role}>
                      {role}
                    </option>
                  ))}
                </select>
              </div>

              <Button type="submit" disabled={addMemberMutation.isPending}>
                {addMemberMutation.isPending ? 'Adding…' : 'Add member'}
              </Button>
            </form>

            {addMemberMutation.isError && (
              <div className="mt-4">
                <ErrorState message={errorMessage(addMemberMutation.error, 'Unable to add that member.')} />
              </div>
            )}
          </CardContent>
        </Card>
      )}

      {isAdmin && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Audit History</CardTitle>
            <CardDescription>Recent actions taken in this organization.</CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            {auditEventsQuery.isPending && <LoadingState />}

            {auditEventsQuery.isError && (
              <ErrorState message={errorMessage(auditEventsQuery.error, 'Unable to load audit history.')} />
            )}

            {auditEventsQuery.data?.items.length === 0 && (
              <EmptyState message="No audited actions yet." />
            )}

            <div className="overflow-x-auto rounded-md border">
              <table className="w-full text-left text-sm">
                <thead className="bg-muted/50">
                  <tr>
                    <th className="whitespace-nowrap px-3 py-2 font-medium">When</th>
                    <th className="whitespace-nowrap px-3 py-2 font-medium">Action</th>
                    <th className="whitespace-nowrap px-3 py-2 font-medium">Entity</th>
                    <th className="whitespace-nowrap px-3 py-2 font-medium">User</th>
                  </tr>
                </thead>
                <tbody>
                  {auditEventsQuery.data?.items.map((event) => (
                    <tr key={event.id} className="border-t">
                      <td className="px-3 py-2 text-muted-foreground">{new Date(event.createdAt).toLocaleString()}</td>
                      <td className="px-3 py-2">{event.action}</td>
                      <td className="px-3 py-2 text-muted-foreground">
                        {event.entityType ? `${event.entityType} #${event.entityId}` : '—'}
                      </td>
                      <td className="px-3 py-2">{event.userDisplayName ?? '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {auditEventsQuery.data && auditEventsQuery.data.totalCount > 0 && (
              <div className="flex items-center justify-between text-sm text-muted-foreground">
                <span>
                  Page {auditPage} of {auditTotalPages}
                </span>
                <div className="flex gap-2">
                  <Button variant="outline" size="sm" disabled={auditPage <= 1} onClick={() => setAuditPage((p) => p - 1)}>
                    Previous
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={auditPage >= auditTotalPages}
                    onClick={() => setAuditPage((p) => p + 1)}
                  >
                    Next
                  </Button>
                </div>
              </div>
            )}
          </CardContent>
        </Card>
      )}
    </div>
  )
}
