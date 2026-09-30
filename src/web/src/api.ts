const apiBaseUrl = import.meta.env.VITE_API_BASE_URL

// All API calls include credentials so the session cookie set by /auth/session is sent back.
export async function apiFetch(path: string, init?: RequestInit): Promise<Response> {
  return fetch(`${apiBaseUrl}${path}`, {
    ...init,
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      ...init?.headers,
    },
  })
}

export type Me = {
  memberId: string
  householdId: string
  role: 'Parent' | 'Child'
  displayName: string
  email: string | null
}

export type Member = {
  id: string
  displayName: string
  role: string
  email: string | null
  canSignIn: boolean
}

export type Invitation = {
  id: string
  code: string
  role: string
  email: string | null
  status: string
  expiresAt: string
}
