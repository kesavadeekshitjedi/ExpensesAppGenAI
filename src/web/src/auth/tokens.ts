import type { Me } from '../api'

// Bearer-token session for the web/PWA. We deliberately avoid the cross-site session cookie because
// iOS Safari (and installed PWAs) block third-party cookies, which made every API call fail on iPhone.
// Tokens live in localStorage so they survive PWA relaunches; the refresh token rotates on each use.
const apiBaseUrl = import.meta.env.VITE_API_BASE_URL

const ACCESS = 'he_access'
const ACCESS_EXP = 'he_access_exp'
const REFRESH = 'he_refresh'

export type TokenBundle = { accessToken: string; accessExpiresAt: string; refreshToken: string; me: Me }

export function setTokens(t: TokenBundle): void {
  localStorage.setItem(ACCESS, t.accessToken)
  localStorage.setItem(ACCESS_EXP, t.accessExpiresAt)
  localStorage.setItem(REFRESH, t.refreshToken)
}

export function clearTokens(): void {
  localStorage.removeItem(ACCESS)
  localStorage.removeItem(ACCESS_EXP)
  localStorage.removeItem(REFRESH)
}

export const getAccessToken = (): string | null => localStorage.getItem(ACCESS)
export const getRefreshToken = (): string | null => localStorage.getItem(REFRESH)

let refreshing: Promise<Me | null> | null = null

// Exchanges the stored refresh token for a fresh access + refresh pair. Single-flight: concurrent
// callers share one request. Returns the member on success, or null (and clears tokens) on failure.
export function doRefresh(): Promise<Me | null> {
  if (refreshing) return refreshing
  const refreshToken = getRefreshToken()
  if (!refreshToken) return Promise.resolve(null)

  refreshing = (async () => {
    try {
      const res = await fetch(`${apiBaseUrl}/auth/refresh`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken }),
      })
      if (!res.ok) {
        clearTokens()
        return null
      }
      const bundle = (await res.json()) as TokenBundle
      setTokens(bundle)
      return bundle.me
    } catch {
      return null
    } finally {
      refreshing = null
    }
  })()
  return refreshing
}

// Returns a valid access token, refreshing first if it is missing or about to expire (30s skew).
export async function ensureFreshAccess(): Promise<string | null> {
  const access = getAccessToken()
  const expRaw = localStorage.getItem(ACCESS_EXP)
  if (access && expRaw && Date.now() < Date.parse(expRaw) - 30_000) return access
  return (await doRefresh()) ? getAccessToken() : null
}
