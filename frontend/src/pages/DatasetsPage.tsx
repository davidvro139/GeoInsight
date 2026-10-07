import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, useOutletContext } from 'react-router-dom'
import { z } from 'zod'
import { ApiError } from '@/api/client'
import { createDataset, getDatasets } from '@/api/datasets'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { EmptyState, ErrorState } from '@/components/StateViews'
import { canManageDatasets } from '@/organizations/permissions'
import type { OrgOutletContext } from './OrganizationLayout'

const DATASET_TYPE_SUGGESTIONS = ['generic', 'loan', 'branch', 'atm', 'complaint', 'investment', 'service']

const createDatasetSchema = z.object({
  name: z.string().min(1, 'Enter a dataset name.'),
  description: z.string().optional(),
  datasetType: z.string().optional(),
})

type CreateDatasetFormValues = z.infer<typeof createDatasetSchema>

export function DatasetsPage() {
  const { organization } = useOutletContext<OrgOutletContext>()
  const canManage = canManageDatasets(organization.myRole)
  const [showArchived, setShowArchived] = useState(false)
  const [showCreateForm, setShowCreateForm] = useState(false)
  const queryClient = useQueryClient()

  const datasetsQuery = useQuery({
    queryKey: ['organizations', organization.id, 'datasets', { showArchived }],
    queryFn: () => getDatasets(organization.id, showArchived),
  })

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<CreateDatasetFormValues>({ resolver: zodResolver(createDatasetSchema) })

  const createMutation = useMutation({
    mutationFn: (values: CreateDatasetFormValues) => createDataset(organization.id, values),
    onSuccess: () => {
      reset()
      setShowCreateForm(false)
      queryClient.invalidateQueries({ queryKey: ['organizations', organization.id, 'datasets'] })
    },
  })

  return (
    <div className="mx-auto flex w-full max-w-5xl flex-col gap-6">
      <Card>
        <CardHeader className="flex flex-row items-start justify-between gap-4">
          <div>
            <CardTitle className="text-xl font-semibold">Datasets</CardTitle>
            <CardDescription>Datasets belonging to {organization.name}.</CardDescription>
          </div>
          {canManage && (
            <Button size="sm" onClick={() => setShowCreateForm((v) => !v)}>
              {showCreateForm ? 'Cancel' : '+ New Dataset'}
            </Button>
          )}
        </CardHeader>

        {showCreateForm && (
          <CardContent className="border-b pb-6">
            <form
              className="flex flex-col gap-4"
              onSubmit={handleSubmit((values) => createMutation.mutate(values))}
            >
              {createMutation.isError && (
                <ErrorState
                  message={
                    createMutation.error instanceof ApiError
                      ? createMutation.error.message
                      : 'Unable to create the dataset.'
                  }
                />
              )}

              <div className="flex flex-col gap-2">
                <Label htmlFor="name">Name</Label>
                <Input id="name" placeholder="2026 Mortgage Loans" {...register('name')} />
                {errors.name && <p className="text-sm text-destructive">{errors.name.message}</p>}
              </div>

              <div className="flex flex-col gap-2">
                <Label htmlFor="description">Description (optional)</Label>
                <Input id="description" {...register('description')} />
              </div>

              <div className="flex flex-col gap-2">
                <Label htmlFor="datasetType">Type</Label>
                <Input id="datasetType" list="dataset-type-suggestions" placeholder="generic" {...register('datasetType')} />
                <datalist id="dataset-type-suggestions">
                  {DATASET_TYPE_SUGGESTIONS.map((type) => (
                    <option key={type} value={type} />
                  ))}
                </datalist>
              </div>

              <Button type="submit" disabled={createMutation.isPending} className="self-start">
                {createMutation.isPending ? 'Creating…' : 'Create dataset'}
              </Button>
            </form>
          </CardContent>
        )}

        <CardContent className="flex flex-col gap-4">
          <label className="flex items-center gap-2 text-sm text-muted-foreground">
            <Checkbox
              checked={showArchived}
              onCheckedChange={(checked) => setShowArchived(checked === true)}
            />
            Show archived
          </label>

          {datasetsQuery.isPending && (
            <div className="flex flex-col gap-3">
              <Skeleton className="h-12 w-full" />
              <Skeleton className="h-12 w-full" />
              <Skeleton className="h-12 w-full" />
            </div>
          )}

          {datasetsQuery.isError && <ErrorState message="Unable to load datasets." />}

          {datasetsQuery.data?.length === 0 && (
            <EmptyState
              message="No datasets yet."
              action={
                canManage && (
                  <Button size="sm" onClick={() => setShowCreateForm(true)}>
                    Create your first dataset
                  </Button>
                )
              }
            />
          )}

          <div className="flex flex-col divide-y">
            {datasetsQuery.data?.map((dataset) => (
              <Link
                key={dataset.id}
                to={`/organizations/${organization.id}/datasets/${dataset.id}`}
                className="flex items-center justify-between gap-4 py-3 hover:bg-muted/50"
              >
                <div>
                  <p className="text-sm font-medium">{dataset.name}</p>
                  <p className="text-sm text-muted-foreground">
                    {dataset.datasetType} · Updated {new Date(dataset.updatedAt).toLocaleDateString()} ·{' '}
                    {dataset.recordCount.toLocaleString()} {dataset.recordCount === 1 ? 'record' : 'records'}
                    {dataset.recordCount > 0 &&
                      ` · ${Math.round((dataset.geocodedCount / dataset.recordCount) * 100)}% geocoded`}
                  </p>
                </div>
                {dataset.isArchived && <Badge variant="secondary">Archived</Badge>}
              </Link>
            ))}
          </div>
        </CardContent>
      </Card>
    </div>
  )
}
