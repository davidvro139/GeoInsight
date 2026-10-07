import { apiFetch } from './client'

export interface AuthResponse {
  token: string
  expiresAtUtc: string
  userId: number
  email: string
  displayName: string
}

export interface RegisterPayload {
  email: string
  password: string
  displayName: string
}

export interface LoginPayload {
  email: string
  password: string
}

export function register(payload: RegisterPayload): Promise<AuthResponse> {
  return apiFetch<AuthResponse>('/api/auth/register', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export function login(payload: LoginPayload): Promise<AuthResponse> {
  return apiFetch<AuthResponse>('/api/auth/login', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export function demoLogin(): Promise<AuthResponse> {
  return apiFetch<AuthResponse>('/api/auth/demo-login', { method: 'POST' })
}

export interface ForgotPasswordResponse {
  resetToken: string | null
}

export function forgotPassword(email: string): Promise<ForgotPasswordResponse> {
  return apiFetch<ForgotPasswordResponse>('/api/auth/forgot-password', {
    method: 'POST',
    body: JSON.stringify({ email }),
  })
}

export interface ResetPasswordPayload {
  email: string
  token: string
  newPassword: string
}

export function resetPassword(payload: ResetPasswordPayload): Promise<AuthResponse> {
  return apiFetch<AuthResponse>('/api/auth/reset-password', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}
