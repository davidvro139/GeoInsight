import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { z } from 'zod'
import { createOrganization } from '@/api/organizations'
import { ApiError } from '@/api/client'
import { useAuth } from '@/auth/auth-context'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ErrorState } from '@/components/StateViews'
import { setLastOrganizationId } from '@/organizations/lastOrganization'

const createOrgSchema = z.object({
  name: z.string().min(1, 'Enter an organization name.'),
})

type CreateOrgFormValues = z.infer<typeof createOrgSchema>

export function CreateOrganizationPage() {
  const { user, signOut } = useAuth()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<CreateOrgFormValues>({ resolver: zodResolver(createOrgSchema) })

  const mutation = useMutation({
    mutationFn: (values: CreateOrgFormValues) => createOrganization(values.name),
    onSuccess: async (organization) => {
      await queryClient.invalidateQueries({ queryKey: ['organizations'] })
      setLastOrganizationId(organization.id)
      navigate(`/organizations/${organization.id}`, { replace: true })
    },
  })

  return (
    <div className="flex min-h-svh items-center justify-center bg-muted/40 p-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle className="text-xl">Create an organization</CardTitle>
          <CardDescription>
            {user?.displayName}, you're not part of an organization yet. Create one to start
            importing and mapping data.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form
            className="flex flex-col gap-4"
            onSubmit={handleSubmit((values) => mutation.mutate(values))}
          >
            {mutation.isError && (
              <ErrorState
                message={
                  mutation.error instanceof ApiError
                    ? mutation.error.message
                    : 'Unable to create the organization. Please try again.'
                }
              />
            )}

            <div className="flex flex-col gap-2">
              <Label htmlFor="name">Organization name</Label>
              <Input id="name" autoComplete="organization" {...register('name')} />
              {errors.name && <p className="text-sm text-destructive">{errors.name.message}</p>}
            </div>

            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? 'Creating…' : 'Create organization'}
            </Button>

            <Button type="button" variant="ghost" onClick={signOut}>
              Log out
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
