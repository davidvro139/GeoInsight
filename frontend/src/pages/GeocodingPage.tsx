import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useOutletContext } from 'react-router-dom'
import { ApiError } from '@/api/client'
import { cancelGeocodeJob, createGeocodeJob, getGeocodeJobs } from '@/api/geocodeJobs'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { ErrorState, LoadingState } from '@/components/StateViews'
import type { DatasetOutletContext } from './DatasetLayout'

export function GeocodingPage() {
  const { organization, dataset, canManage } = useOutletContext<DatasetOutletContext>()
  const queryClient = useQueryClient()
  const jobsKey = ['organizations', organization.id, 'datasets', dataset.id, 'geocode-jobs']

  const jobsQuery = useQuery({
    queryKey: jobsKey,
    queryFn: () => getGeocodeJobs(organization.id, dataset.id),
    refetchInterval: (query) => {
      const latest = query.state.data?.[0]
      return latest && (latest.status === 'Queued' || latest.status === 'Running') ? 2000 : false
    },
  })

  const startMutation = useMutation({
    mutationFn: () => createGeocodeJob(organization.id, dataset.id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: jobsKey }),
  })

  const cancelMutation = useMutation({
    mutationFn: (jobId: number) => cancelGeocodeJob(organization.id, dataset.id, jobId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: jobsKey }),
  })

  const jobs = jobsQuery.data ?? []
  const activeJob = jobs.find((j) => j.status === 'Queued' || j.status === 'Running')
  const latestJob = jobs[0]
  const percent = activeJob && activeJob.totalItems > 0
    ? Math.round((activeJob.completedItems / activeJob.totalItems) * 100)
    : 0

  return (
    <div className="flex flex-col gap-6">
      <Card>
        <CardHeader className="flex flex-row items-start justify-between gap-4">
          <div>
            <CardTitle className="text-xl font-semibold">Geocoding</CardTitle>
            <CardDescription>Turn addresses into mapped points using the U.S. Census Geocoder.</CardDescription>
          </div>
          {canManage && !activeJob && (
            <Button size="sm" disabled={startMutation.isPending} onClick={() => startMutation.mutate()}>
              {startMutation.isPending ? 'Starting…' : 'Start Geocoding'}
            </Button>
          )}
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          {startMutation.isError && (
            <ErrorState
              message={startMutation.error instanceof ApiError ? startMutation.error.message : 'Unable to start geocoding.'}
            />
          )}

          {jobsQuery.isPending && <LoadingState />}

          {jobsQuery.isError && <ErrorState message="Unable to load geocoding status." />}

          {!jobsQuery.isPending && (activeJob ? (
            <div className="flex flex-col gap-3 rounded-lg border p-4">
              <div className="flex items-center justify-between">
                <p className="text-sm font-medium">
                  {activeJob.status === 'Queued' ? 'Queued…' : `Geocoding ${dataset.name}`}
                </p>
                {canManage && (
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={cancelMutation.isPending}
                    onClick={() => cancelMutation.mutate(activeJob.id)}
                  >
                    Cancel
                  </Button>
                )}
              </div>

              <div className="h-2 w-full overflow-hidden rounded-full bg-muted">
                <div className="h-full bg-foreground transition-all" style={{ width: `${percent}%` }} />
              </div>
              <p className="text-sm text-muted-foreground">
                {activeJob.completedItems} / {activeJob.totalItems} ({percent}%)
              </p>
              <div className="flex gap-4 text-sm">
                <span>Matched {activeJob.successfulItems}</span>
                <span>Failed {activeJob.failedItems}</span>
              </div>
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">
              {jobs.length === 0
                ? canManage
                  ? 'Start a geocoding job to turn imported addresses into mapped points.'
                  : 'No geocoding has run yet.'
                : 'No geocoding job currently running.'}
            </p>
          ))}

          {!activeJob && latestJob && latestJob.failedItems > 0 && (
            <Link
              to={`/organizations/${organization.id}/datasets/${dataset.id}/review`}
              className="text-sm underline underline-offset-4"
            >
              Review Failed Addresses ({latestJob.failedItems})
            </Link>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">History</CardTitle>
        </CardHeader>
        <CardContent>
          {jobsQuery.isPending && <LoadingState />}
          {!jobsQuery.isPending && jobs.length === 0 && (
            <p className="text-sm text-muted-foreground">No geocode jobs yet.</p>
          )}
          <div className="flex flex-col divide-y">
            {jobs.map((job) => (
              <div key={job.id} className="flex items-center justify-between py-3">
                <div>
                  <p className="text-sm font-medium">{new Date(job.createdAt).toLocaleString()}</p>
                  <p className="text-sm text-muted-foreground">
                    {job.successfulItems} matched · {job.failedItems} failed / {job.totalItems} total
                  </p>
                </div>
                <Badge variant={job.status === 'Failed' ? 'destructive' : 'secondary'}>{job.status}</Badge>
              </div>
            ))}
          </div>
        </CardContent>
      </Card>
    </div>
  )
}
