import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as maplibregl from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import { useEffect, useRef, useState } from 'react'
import { useOutletContext } from 'react-router-dom'
import { ApiError } from '@/api/client'
import {
  acceptCandidate,
  correctAddress,
  dismissRecord,
  getReviewQueue,
  setCoordinates,
  type ReviewRecord,
} from '@/api/review'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateViews'
import { MAP_STYLE } from '@/lib/mapStyle'
import type { DatasetOutletContext } from './DatasetLayout'

interface MapPoint {
  latitude: number
  longitude: number
}

function MiniMap({ point }: { point: MapPoint | null }) {
  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<maplibregl.Map | null>(null)
  const markerRef = useRef<maplibregl.Marker | null>(null)

  useEffect(() => {
    if (!containerRef.current || mapRef.current) return

    const map = new maplibregl.Map({
      container: containerRef.current,
      style: MAP_STYLE,
      center: point ? [point.longitude, point.latitude] : [-98.5, 39.8],
      zoom: point ? 13 : 3,
    })
    map.addControl(new maplibregl.NavigationControl(), 'top-right')
    mapRef.current = map

    return () => {
      map.remove()
      mapRef.current = null
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    const map = mapRef.current
    if (!map) return

    markerRef.current?.remove()
    markerRef.current = null

    if (point) {
      markerRef.current = new maplibregl.Marker().setLngLat([point.longitude, point.latitude]).addTo(map)
      map.jumpTo({ center: [point.longitude, point.latitude], zoom: 13 })
    }
  }, [point])

  return <div ref={containerRef} className="h-48 w-full overflow-hidden rounded-lg border" />
}

function statusBadgeVariant(status: string): 'default' | 'secondary' | 'destructive' {
  if (status === 'manually_resolved') return 'default'
  if (status === 'ambiguous') return 'secondary'
  return 'destructive'
}

interface ReviewDetailPanelProps {
  organizationId: number
  datasetId: number
  record: ReviewRecord
  canManage: boolean
  onResolved: () => void
}

function ReviewDetailPanel({ organizationId, datasetId, record, canManage, onResolved }: ReviewDetailPanelProps) {
  const [previewCandidate, setPreviewCandidate] = useState<number | null>(null)
  const [addressForm, setAddressForm] = useState({
    addressLine1: record.addressLine1,
    addressLine2: record.addressLine2 ?? '',
    city: record.city,
    state: record.state,
    postalCode: record.postalCode ?? '',
  })
  const [coordsForm, setCoordsForm] = useState({
    latitude: record.latitude?.toString() ?? '',
    longitude: record.longitude?.toString() ?? '',
  })

  const acceptMutation = useMutation({
    mutationFn: (index: number) => acceptCandidate(organizationId, datasetId, record.id, index),
    onSuccess: onResolved,
  })
  const correctMutation = useMutation({
    mutationFn: () => correctAddress(organizationId, datasetId, record.id, addressForm),
    onSuccess: onResolved,
  })
  const coordsMutation = useMutation({
    mutationFn: () => setCoordinates(organizationId, datasetId, record.id, Number(coordsForm.latitude), Number(coordsForm.longitude)),
    onSuccess: onResolved,
  })
  const dismissMutation = useMutation({
    mutationFn: () => dismissRecord(organizationId, datasetId, record.id),
    onSuccess: onResolved,
  })

  const previewPoint: MapPoint | null =
    previewCandidate !== null
      ? record.candidates[previewCandidate]
      : record.latitude != null && record.longitude != null
        ? { latitude: record.latitude, longitude: record.longitude }
        : null

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Original address</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-2 text-sm">
          <div>
            <p>
              {record.addressLine1}
              {record.addressLine2 ? `, ${record.addressLine2}` : ''}
            </p>
            <p>
              {record.city}, {record.state} {record.postalCode}
            </p>
          </div>
          {record.matchedAddress && (
            <p className="text-muted-foreground">Last match attempt: {record.matchedAddress}</p>
          )}
          <Badge className="w-fit" variant={statusBadgeVariant(record.geocodeStatus)}>
            {record.geocodeStatus}
          </Badge>
        </CardContent>
      </Card>

      <MiniMap point={previewPoint} />

      {record.candidates.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Possible matches</CardTitle>
            <CardDescription>Hover a candidate to preview it on the map above.</CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-2">
            {record.candidates.map((candidate, index) => (
              <div
                key={index}
                onMouseEnter={() => setPreviewCandidate(index)}
                onMouseLeave={() => setPreviewCandidate(null)}
                className={`flex items-center justify-between gap-3 rounded-md border p-3 text-sm ${
                  previewCandidate === index ? 'border-primary' : ''
                }`}
              >
                <span>{candidate.matchedAddress}</span>
                {canManage && (
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={acceptMutation.isPending}
                    onClick={() => acceptMutation.mutate(index)}
                  >
                    Accept Match
                  </Button>
                )}
              </div>
            ))}
          </CardContent>
        </Card>
      )}

      {canManage && (
        <>
          <Card>
            <CardHeader>
              <CardTitle className="text-base">Corrected address</CardTitle>
              <CardDescription>Re-runs the geocoder against the corrected address.</CardDescription>
            </CardHeader>
            <CardContent className="flex flex-col gap-3">
              <div className="grid grid-cols-2 gap-3">
                <div className="col-span-2 flex flex-col gap-1.5">
                  <Label htmlFor="review-address-line-1">Address line 1</Label>
                  <Input
                    id="review-address-line-1"
                    value={addressForm.addressLine1}
                    onChange={(e) => setAddressForm((f) => ({ ...f, addressLine1: e.target.value }))}
                  />
                </div>
                <div className="col-span-2 flex flex-col gap-1.5">
                  <Label htmlFor="review-address-line-2">Address line 2</Label>
                  <Input
                    id="review-address-line-2"
                    value={addressForm.addressLine2}
                    onChange={(e) => setAddressForm((f) => ({ ...f, addressLine2: e.target.value }))}
                  />
                </div>
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="review-city">City</Label>
                  <Input
                    id="review-city"
                    value={addressForm.city}
                    onChange={(e) => setAddressForm((f) => ({ ...f, city: e.target.value }))}
                  />
                </div>
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="review-state">State</Label>
                  <Input
                    id="review-state"
                    value={addressForm.state}
                    onChange={(e) => setAddressForm((f) => ({ ...f, state: e.target.value }))}
                  />
                </div>
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="review-postal-code">Postal code</Label>
                  <Input
                    id="review-postal-code"
                    value={addressForm.postalCode}
                    onChange={(e) => setAddressForm((f) => ({ ...f, postalCode: e.target.value }))}
                  />
                </div>
              </div>
              {correctMutation.isError && (
                <ErrorState
                  message={
                    correctMutation.error instanceof ApiError ? correctMutation.error.message : 'Unable to save correction.'
                  }
                />
              )}
              {correctMutation.isSuccess && correctMutation.data.geocodeStatus !== 'manually_resolved' && (
                <ErrorState
                  message={`Still couldn't confidently match that address (${correctMutation.data.geocodeStatus}). Try manual coordinates instead, or edit the address again.`}
                />
              )}
              <Button size="sm" disabled={correctMutation.isPending} onClick={() => correctMutation.mutate()}>
                {correctMutation.isPending ? 'Saving…' : 'Save Correction'}
              </Button>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-base">Manual coordinates</CardTitle>
              <CardDescription>Bypasses the geocoder — no Census tract/block will be assigned.</CardDescription>
            </CardHeader>
            <CardContent className="flex flex-col gap-3">
              <div className="grid grid-cols-2 gap-3">
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="review-latitude">Latitude</Label>
                  <Input
                    id="review-latitude"
                    inputMode="decimal"
                    value={coordsForm.latitude}
                    onChange={(e) => setCoordsForm((f) => ({ ...f, latitude: e.target.value }))}
                  />
                </div>
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="review-longitude">Longitude</Label>
                  <Input
                    id="review-longitude"
                    inputMode="decimal"
                    value={coordsForm.longitude}
                    onChange={(e) => setCoordsForm((f) => ({ ...f, longitude: e.target.value }))}
                  />
                </div>
              </div>
              {coordsMutation.isError && (
                <ErrorState
                  message={
                    coordsMutation.error instanceof ApiError ? coordsMutation.error.message : 'Unable to save coordinates.'
                  }
                />
              )}
              <Button
                size="sm"
                variant="outline"
                disabled={coordsMutation.isPending || !coordsForm.latitude || !coordsForm.longitude}
                onClick={() => coordsMutation.mutate()}
              >
                {coordsMutation.isPending ? 'Saving…' : 'Save Coordinates'}
              </Button>
            </CardContent>
          </Card>

          <Button
            variant="ghost"
            className="w-fit text-muted-foreground"
            disabled={dismissMutation.isPending}
            onClick={() => dismissMutation.mutate()}
          >
            Mark Unresolved
          </Button>
        </>
      )}
    </div>
  )
}

export function AddressReviewPage() {
  const { organization, dataset, canManage } = useOutletContext<DatasetOutletContext>()
  const queryClient = useQueryClient()
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const queueKey = ['organizations', organization.id, 'datasets', dataset.id, 'review-queue']

  const queueQuery = useQuery({
    queryKey: queueKey,
    queryFn: () => getReviewQueue(organization.id, dataset.id, { pageSize: 100 }),
  })

  const records = queueQuery.data?.items ?? []
  const selected = records.find((r) => r.id === selectedId) ?? records[0] ?? null

  const invalidate = () => {
    setSelectedId(null)
    queryClient.invalidateQueries({ queryKey: queueKey })
  }

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardHeader>
          <CardTitle className="text-xl font-semibold">Address Review Queue</CardTitle>
          <CardDescription>Resolve addresses the geocoder couldn't confidently match.</CardDescription>
        </CardHeader>
      </Card>

      {queueQuery.isPending ? (
        <LoadingState />
      ) : queueQuery.isError ? (
        <ErrorState message="Unable to load the address review queue." />
      ) : records.length === 0 ? (
        <Card>
          <CardContent className="pt-6">
            <EmptyState message="Nothing needs review right now." />
          </CardContent>
        </Card>
      ) : (
        <div className="grid grid-cols-1 items-start gap-4 md:grid-cols-[280px_1fr]">
          <Card>
            <CardContent className="flex flex-col divide-y p-0">
              {records.map((record) => (
                <button
                  key={record.id}
                  type="button"
                  onClick={() => setSelectedId(record.id)}
                  className={`flex flex-col items-start gap-1 px-4 py-3 text-left text-sm hover:bg-muted/50 ${
                    (selected?.id ?? records[0]?.id) === record.id ? 'bg-muted' : ''
                  }`}
                >
                  <span className="font-medium">{record.addressLine1}</span>
                  <span className="text-muted-foreground">
                    {record.city}, {record.state}
                  </span>
                  <Badge variant={statusBadgeVariant(record.geocodeStatus)}>{record.geocodeStatus}</Badge>
                </button>
              ))}
            </CardContent>
          </Card>

          {selected && (
            <ReviewDetailPanel
              key={selected.id}
              organizationId={organization.id}
              datasetId={dataset.id}
              record={selected}
              canManage={canManage}
              onResolved={invalidate}
            />
          )}
        </div>
      )}
    </div>
  )
}
