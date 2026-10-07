import { useEffect, useMemo, useState } from 'react'
import type { RecordFilters } from '@/api/recordFilters'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

const GEOCODE_STATUSES = ['pending', 'processing', 'matched', 'approximate', 'ambiguous', 'failed', 'manually_resolved']

interface RecordFilterSidebarProps {
  description?: string
  onChange: (filters: RecordFilters) => void
  /**
   * When `key` changes, overwrites every field below from `filters` — an
   * escape hatch for external code (e.g. loading a Saved View) to push new
   * values into this otherwise-uncontrolled form. `key` (not just `filters`
   * by reference) drives the effect so re-loading the same saved view twice
   * in a row still re-applies it.
   */
  resetSignal?: { filters: RecordFilters; key: number }
}

export function RecordFilterSidebar({ description, onChange, resetSignal }: RecordFilterSidebarProps) {
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [category, setCategory] = useState('')
  const [minAmount, setMinAmount] = useState('')
  const [maxAmount, setMaxAmount] = useState('')
  const [city, setCity] = useState('')
  const [filterState, setFilterState] = useState('')
  const [countyFips, setCountyFips] = useState('')
  const [tract, setTract] = useState('')
  const [geocodeStatus, setGeocodeStatus] = useState('')
  const [minMedianIncome, setMinMedianIncome] = useState('')
  const [maxMedianIncome, setMaxMedianIncome] = useState('')

  useEffect(() => {
    if (!resetSignal) return
    const f = resetSignal.filters
    setDateFrom(f.dateFrom ?? '')
    setDateTo(f.dateTo ?? '')
    setCategory(f.category ?? '')
    setMinAmount(f.minAmount != null ? String(f.minAmount) : '')
    setMaxAmount(f.maxAmount != null ? String(f.maxAmount) : '')
    setCity(f.city ?? '')
    setFilterState(f.state ?? '')
    setCountyFips(f.countyFips ?? '')
    setTract(f.tract ?? '')
    setGeocodeStatus(f.geocodeStatus ?? '')
    setMinMedianIncome(f.minMedianIncome != null ? String(f.minMedianIncome) : '')
    setMaxMedianIncome(f.maxMedianIncome != null ? String(f.maxMedianIncome) : '')
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [resetSignal?.key])

  const filters: RecordFilters = useMemo(
    () => ({
      dateFrom: dateFrom || undefined,
      dateTo: dateTo || undefined,
      category: category || undefined,
      minAmount: minAmount ? Number(minAmount) : undefined,
      maxAmount: maxAmount ? Number(maxAmount) : undefined,
      city: city || undefined,
      state: filterState || undefined,
      countyFips: countyFips || undefined,
      tract: tract || undefined,
      geocodeStatus: geocodeStatus || undefined,
      minMedianIncome: minMedianIncome ? Number(minMedianIncome) : undefined,
      maxMedianIncome: maxMedianIncome ? Number(maxMedianIncome) : undefined,
    }),
    [dateFrom, dateTo, category, minAmount, maxAmount, city, filterState, countyFips, tract, geocodeStatus, minMedianIncome, maxMedianIncome],
  )

  useEffect(() => {
    onChange(filters)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filters])

  const resetFilters = () => {
    setDateFrom(''); setDateTo(''); setCategory(''); setMinAmount(''); setMaxAmount('')
    setCity(''); setFilterState(''); setCountyFips(''); setTract(''); setGeocodeStatus('')
    setMinMedianIncome(''); setMaxMedianIncome('')
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Filters</CardTitle>
        <CardDescription>{description ?? 'Narrows the results together.'}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        <div className="grid grid-cols-2 gap-2">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="filter-date-from">From</Label>
            <Input id="filter-date-from" type="date" value={dateFrom} onChange={(e) => setDateFrom(e.target.value)} />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="filter-date-to">To</Label>
            <Input id="filter-date-to" type="date" value={dateTo} onChange={(e) => setDateTo(e.target.value)} />
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="filter-category">Category</Label>
          <Input id="filter-category" value={category} onChange={(e) => setCategory(e.target.value)} placeholder="Contains…" />
        </div>

        <div className="grid grid-cols-2 gap-2">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="filter-min-amount">Min amount</Label>
            <Input id="filter-min-amount" type="number" value={minAmount} onChange={(e) => setMinAmount(e.target.value)} />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="filter-max-amount">Max amount</Label>
            <Input id="filter-max-amount" type="number" value={maxAmount} onChange={(e) => setMaxAmount(e.target.value)} />
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="filter-city">City</Label>
          <Input id="filter-city" value={city} onChange={(e) => setCity(e.target.value)} placeholder="Contains…" />
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="filter-state">State</Label>
          <Input id="filter-state" value={filterState} onChange={(e) => setFilterState(e.target.value)} placeholder="Contains…" />
        </div>

        <div className="grid grid-cols-2 gap-2">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="filter-county">County FIPS</Label>
            <Input id="filter-county" value={countyFips} onChange={(e) => setCountyFips(e.target.value)} />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="filter-tract">Tract</Label>
            <Input id="filter-tract" value={tract} onChange={(e) => setTract(e.target.value)} />
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <Label>Geocode status</Label>
          <Select value={geocodeStatus || '__any__'} onValueChange={(v) => setGeocodeStatus(v === '__any__' ? '' : v)}>
            <SelectTrigger size="sm">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="__any__">Any</SelectItem>
              {GEOCODE_STATUSES.map((status) => (
                <SelectItem key={status} value={status}>
                  {status}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="grid grid-cols-2 gap-2">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="filter-min-income">Min tract income</Label>
            <Input id="filter-min-income" type="number" value={minMedianIncome} onChange={(e) => setMinMedianIncome(e.target.value)} />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="filter-max-income">Max tract income</Label>
            <Input id="filter-max-income" type="number" value={maxMedianIncome} onChange={(e) => setMaxMedianIncome(e.target.value)} />
          </div>
        </div>

        <Button variant="outline" size="sm" onClick={resetFilters}>
          Reset filters
        </Button>
      </CardContent>
    </Card>
  )
}
