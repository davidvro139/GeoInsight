import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { z } from 'zod'
import { ApiError } from '@/api/client'
import { demoLogin, login, type AuthResponse } from '@/api/auth'
import { useAuth } from '@/auth/auth-context'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ErrorState } from '@/components/StateViews'

const loginSchema = z.object({
  email: z.string().email('Enter a valid email address.'),
  password: z.string().min(1, 'Password is required.'),
})

type LoginFormValues = z.infer<typeof loginSchema>

export function LoginPage() {
  const { signIn } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<LoginFormValues>({ resolver: zodResolver(loginSchema) })

  const handleAuthSuccess = (response: AuthResponse) => {
    signIn(response)
    const redirectTo = (location.state as { from?: Location })?.from?.pathname ?? '/'
    navigate(redirectTo, { replace: true })
  }

  const mutation = useMutation({
    mutationFn: login,
    onSuccess: handleAuthSuccess,
  })

  const demoMutation = useMutation({
    mutationFn: demoLogin,
    onSuccess: handleAuthSuccess,
  })

  return (
    <div className="flex min-h-svh items-center justify-center bg-muted/40 p-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle className="text-xl">Sign in to GeoInsight</CardTitle>
          <CardDescription>Enter your credentials to access your organization.</CardDescription>
        </CardHeader>
        <CardContent>
          <form
            className="flex flex-col gap-4"
            onSubmit={handleSubmit((values) => mutation.mutate(values))}
          >
            {mutation.isError && (
              <ErrorState
                message={
                  mutation.error instanceof ApiError ? mutation.error.message : 'Unable to sign in. Please try again.'
                }
              />
            )}

            <div className="flex flex-col gap-2">
              <Label htmlFor="email">Email</Label>
              <Input id="email" type="email" autoComplete="email" {...register('email')} />
              {errors.email && <p className="text-sm text-destructive">{errors.email.message}</p>}
            </div>

            <div className="flex flex-col gap-2">
              <div className="flex items-center justify-between">
                <Label htmlFor="password">Password</Label>
                <Link to="/forgot-password" className="text-sm text-muted-foreground underline underline-offset-4">
                  Forgot password?
                </Link>
              </div>
              <Input
                id="password"
                type="password"
                autoComplete="current-password"
                {...register('password')}
              />
              {errors.password && (
                <p className="text-sm text-destructive">{errors.password.message}</p>
              )}
            </div>

            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? 'Signing in…' : 'Sign in'}
            </Button>

            <div className="relative text-center text-sm">
              <span className="bg-card px-2 text-muted-foreground">or</span>
              <div className="absolute inset-x-0 top-1/2 -z-10 border-t" />
            </div>

            {demoMutation.isError && (
              <ErrorState
                message={
                  demoMutation.error instanceof ApiError
                    ? demoMutation.error.message
                    : 'Unable to start the demo. Please try again.'
                }
              />
            )}

            <Button
              type="button"
              variant="outline"
              disabled={demoMutation.isPending}
              onClick={() => demoMutation.mutate()}
            >
              {demoMutation.isPending ? 'Starting demo…' : 'Try the demo'}
            </Button>

            <p className="text-center text-sm text-muted-foreground">
              Don&apos;t have an account?{' '}
              <Link to="/register" className="underline underline-offset-4">
                Create one
              </Link>
            </p>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
