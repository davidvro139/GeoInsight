import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { z } from 'zod'
import { ApiError } from '@/api/client'
import { resetPassword, type AuthResponse } from '@/api/auth'
import { useAuth } from '@/auth/auth-context'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ErrorState } from '@/components/StateViews'

const resetPasswordSchema = z.object({
  email: z.string().email('Enter a valid email address.'),
  token: z.string().min(1, 'A reset token is required.'),
  newPassword: z.string().min(8, 'Password must be at least 8 characters.'),
})

type ResetPasswordFormValues = z.infer<typeof resetPasswordSchema>

export function ResetPasswordPage() {
  const { signIn } = useAuth()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ResetPasswordFormValues>({
    resolver: zodResolver(resetPasswordSchema),
    defaultValues: {
      email: searchParams.get('email') ?? '',
      token: searchParams.get('token') ?? '',
    },
  })

  const mutation = useMutation({
    mutationFn: resetPassword,
    onSuccess: (response: AuthResponse) => {
      signIn(response)
      navigate('/', { replace: true })
    },
  })

  return (
    <div className="flex min-h-svh items-center justify-center bg-muted/40 p-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle className="text-xl">Choose a new password</CardTitle>
          <CardDescription>You'll be signed in right away once this succeeds.</CardDescription>
        </CardHeader>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={handleSubmit((values) => mutation.mutate(values))}>
            {mutation.isError && (
              <ErrorState
                message={
                  mutation.error instanceof ApiError
                    ? mutation.error.message
                    : 'Unable to reset the password. The link may have expired — request a new one.'
                }
              />
            )}

            <div className="flex flex-col gap-2">
              <Label htmlFor="email">Email</Label>
              <Input id="email" type="email" autoComplete="email" {...register('email')} />
              {errors.email && <p className="text-sm text-destructive">{errors.email.message}</p>}
            </div>

            <div className="flex flex-col gap-2">
              <Label htmlFor="token">Reset token</Label>
              <Input id="token" {...register('token')} />
              {errors.token && <p className="text-sm text-destructive">{errors.token.message}</p>}
            </div>

            <div className="flex flex-col gap-2">
              <Label htmlFor="newPassword">New password</Label>
              <Input id="newPassword" type="password" autoComplete="new-password" {...register('newPassword')} />
              {errors.newPassword && <p className="text-sm text-destructive">{errors.newPassword.message}</p>}
            </div>

            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? 'Resetting…' : 'Reset password'}
            </Button>

            <p className="text-center text-sm text-muted-foreground">
              <Link to="/login" className="underline underline-offset-4">
                Back to sign in
              </Link>
            </p>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
