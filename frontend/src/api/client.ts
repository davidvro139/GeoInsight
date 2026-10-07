const API_BASE_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:5035'

const TOKEN_STORAGE_KEY = 'geoinsight.token'

export function getStoredToken(): string | null {
  return localStorage.getItem(TOKEN_STORAGE_KEY)
}

export function setStoredToken(token: string | null): void {
  if (token) {
    localStorage.setItem(TOKEN_STORAGE_KEY, token)
  } else {
    localStorage.removeItem(TOKEN_STORAGE_KEY)
  }
}

export class ApiError extends Error {
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.status = status
  }
}

export async function apiFetch<TResponse>(
  path: string,
  init: RequestInit = {},
): Promise<TResponse> {
  const token = getStoredToken()
  const headers = new Headers(init.headers)
  // Leave Content-Type unset for FormData bodies (file uploads) — the browser
  // needs to add its own multipart boundary, which setting it here would break.
  if (!(init.body instanceof FormData)) {
    headers.set('Content-Type', 'application/json')
  }
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }

  const response = await fetch(`${API_BASE_URL}${path}`, { ...init, headers })

  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    const message = problem?.detail ?? problem?.title ?? response.statusText
    throw new ApiError(message, response.status)
  }

  if (response.status === 204) {
    return undefined as TResponse
  }

  return (await response.json()) as TResponse
}
