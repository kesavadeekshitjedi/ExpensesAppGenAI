import { useCallback, useEffect, useState } from 'react'
import { apiFetch, type Me } from './api'
import { loginRequest, msal } from './auth/msal'
import Dashboard from './components/Dashboard'

// An invite link looks like https://<web>/?invite=<code>; the code is passed through at sign-in.
const inviteCode = new URLSearchParams(window.location.search).get('invite')

function App() {
  const [me, setMe] = useState<Me | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // On load, check whether a session cookie already identifies us.
  useEffect(() => {
    apiFetch('/auth/me')
      .then((res) => (res.ok ? (res.json() as Promise<Me>) : null))
      .then(setMe)
      .catch(() => setMe(null))
      .finally(() => setLoading(false))
  }, [])

  const signIn = useCallback(async () => {
    setError(null)
    try {
      const result = await msal.loginPopup(loginRequest)
      const res = await apiFetch('/auth/session', {
        method: 'POST',
        body: JSON.stringify({ provider: 'Microsoft', token: result.idToken, invitationCode: inviteCode }),
      })
      if (res.ok) {
        setMe((await res.json()) as Me)
      } else if (res.status === 403) {
        const problem = (await res.json().catch(() => null)) as { title?: string } | null
        setError(problem?.title ?? 'You need an invitation to join a household.')
      } else {
        setError('Sign-in failed. Please try again.')
      }
    } catch {
      setError('Sign-in was cancelled.')
    }
  }, [])

  const signOut = useCallback(async () => {
    await apiFetch('/auth/logout', { method: 'POST' })
    setMe(null)
  }, [])

  if (loading) {
    return (
      <main>
        <p>Loading…</p>
      </main>
    )
  }

  if (!me) {
    return (
      <main>
        <h1>Home Expenses</h1>
        {inviteCode && <p>You’ve been invited to join a household. Sign in to accept.</p>}
        <button onClick={signIn}>Sign in with Microsoft</button>
        {error && <p role="alert">{error}</p>}
      </main>
    )
  }

  return <Dashboard me={me} onSignOut={signOut} />
}

export default App
