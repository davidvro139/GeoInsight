import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { Link } from 'react-router-dom'
import { z } from 'zod'
import { ApiError } from '@/api/client'
import { forgotPassword } from '@/api/auth'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ErrorState } from '@/components/StateViews'

const forgotPasswordSchema = z.object({
  email: z.string().email('Enter a valid email address.'),
})

type ForgotPasswordFormValues = z.infer<typeof forgotPasswordSchema>

export function ForgotPasswordPage() {
  const {
    register,
    handleSubmit,
    getValues,
    formState: { errors },
  } = useForm<ForgotPasswordFormValues>({ resolver: zodResolver(forgotPasswordSchema) })

  const mutation = useMutation({
    mutationFn: (values: ForgotPasswordFormValues) => forgotPassword(values.email),
  })

  return (
    <div className="flex min-h-svh items-center justify-center bg-muted/40 p-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle className="text-xl">Reset your password</CardTitle>
          <CardDescription>Enter your account email to get a reset link.</CardDescription>
        </CardHeader>
        <CardContent>
          {mutation.isSuccess ? (
            <div className="flex flex-col gap-4">
              {mutation.data.resetToken ? (
                <>
                  <Alert>
                    <AlertDescription>
                      This project doesn&apos;t send real emails, so here&apos;s your reset link directly — in a
                      production deployment this would be emailed instead.
                    </AlertDescription>
                  </Alert>
                  <Button asChild>
                    <Link
                      to={`/reset-password?email=${encodeURIComponent(getValues('email'))}&token=${encodeURIComponent(mutation.data.resetToken)}`}
                    >
                      Continue to reset password
                    </Link>
                  </Button>
                </>
              ) : (
                <Alert>
                  <AlertDescription>
                    If an account exists for that email, a reset link would have been sent. (This project has no
                    email service, so nothing was actually sent.)
                  </AlertDescription>
                </Alert>
              )}
              <p className="text-center text-sm text-muted-foreground">
                <Link to="/login" className="underline underline-offset-4">
                  Back to sign in
                </Link>
              </p>
            </div>
          ) : (
            <form
              className="flex flex-col gap-4"
              onSubmit={handleSubmit((values) => mutation.mutate(values))}
            >
              {mutation.isError && (
                <ErrorState
                  message={
                    mutation.error instanceof ApiError
                      ? mutation.error.message
                      : 'Unable to process this request. Please try again.'
                  }
                />
              )}

              <div className="flex flex-col gap-2">
                <Label htmlFor="email">Email</Label>
                <Input id="email" type="email" autoComplete="email" {...register('email')} />
                {errors.email && <p className="text-sm text-destructive">{errors.email.message}</p>}
              </div>

              <Button type="submit" disabled={mutation.isPending}>
                {mutation.isPending ? 'Sending…' : 'Send reset link'}
              </Button>

              <p className="text-center text-sm text-muted-foreground">
                <Link to="/login" className="underline underline-offset-4">
                  Back to sign in
                </Link>
              </p>
            </form>
          )}
        </CardContent>
      </Card>
    </div>
  )
}
