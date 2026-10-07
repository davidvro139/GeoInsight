import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useOutletContext } from 'react-router-dom'
import { z } from 'zod'
import { ApiError } from '@/api/client'
import { updateDataset } from '@/api/datasets'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ErrorState } from '@/components/StateViews'
import type { DatasetOutletContext } from './DatasetLayout'

const editDatasetSchema = z.object({
  name: z.string().min(1, 'Enter a dataset name.'),
  description: z.string().optional(),
  datasetType: z.string().optional(),
})

type EditDatasetFormValues = z.infer<typeof editDatasetSchema>

export function DatasetOverviewPage() {
  const { organization, dataset, canManage, invalidateDataset } = useOutletContext<DatasetOutletContext>()
  const [isEditing, setIsEditing] = useState(false)

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<EditDatasetFormValues>({
    resolver: zodResolver(editDatasetSchema),
    values: { name: dataset.name, description: dataset.description ?? '', datasetType: dataset.datasetType },
  })

  const updateMutation = useMutation({
    mutationFn: (values: EditDatasetFormValues) => updateDataset(organization.id, dataset.id, values),
    onSuccess: () => {
      setIsEditing(false)
      invalidateDataset()
    },
  })

  return (
    <Card>
      <CardContent className="flex flex-col gap-4 pt-6">
        {canManage && (
          <Button variant="outline" size="sm" className="self-end" onClick={() => setIsEditing((v) => !v)}>
            {isEditing ? 'Cancel' : 'Edit'}
          </Button>
        )}

        {isEditing ? (
          <form className="flex flex-col gap-4" onSubmit={handleSubmit((values) => updateMutation.mutate(values))}>
            {updateMutation.isError && (
              <ErrorState
                message={
                  updateMutation.error instanceof ApiError
                    ? updateMutation.error.message
                    : 'Unable to save changes.'
                }
              />
            )}

            <div className="flex flex-col gap-2">
              <Label htmlFor="name">Name</Label>
              <Input id="name" {...register('name')} />
              {errors.name && <p className="text-sm text-destructive">{errors.name.message}</p>}
            </div>

            <div className="flex flex-col gap-2">
              <Label htmlFor="description">Description</Label>
              <Input id="description" {...register('description')} />
            </div>

            <div className="flex flex-col gap-2">
              <Label htmlFor="datasetType">Type</Label>
              <Input id="datasetType" {...register('datasetType')} />
            </div>

            <Button type="submit" disabled={updateMutation.isPending} className="self-start">
              {updateMutation.isPending ? 'Saving…' : 'Save changes'}
            </Button>
          </form>
        ) : (
          <p className="text-sm text-muted-foreground">{dataset.description || 'No description.'}</p>
        )}
      </CardContent>
    </Card>
  )
}
