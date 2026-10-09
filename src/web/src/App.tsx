import { useCallback, useEffect, useState } from 'react'
import type { AuthenticationResult } from '@azure/msal-browser'
import { apiFetch, type Me } from './api'
import { loginRequest, msal } from './auth/msal'
import Dashboard from './components/Dashboard'

// An invite link looks like https://<web>/?invite=<code>. Capture it before a redirect navigates
// away, so it survives the round-trip to Microsoft and back.
const urlInvite = new URLSearchParams(window.location.search).get('invite')
if (urlInvite) {
  sessionStorage.setItem('pending_invite', urlInvite)
}

function App({ initialRedirect }: { initialRedirect: AuthenticationResult | null }) {
  const [me, setMe] = useState<Me | null>(null)
  // Only block the whole screen while completing a returning Microsoft redirect (we must
  // exchange the ID token before we can show the dashboard). On a normal visit we render the
  // sign-in button immediately and check for an existing session in the background, so a
  // cold-starting API never delays sign-in (signing in is client-side MSAL; it needs no API).
  const [loading, setLoading] = useState(initialRedirect?.idToken != null)
  const [checking, setChecking] = useState(initialRedirect?.idToken == null)
  const [error, setError] = useState<string | null>(null)

  // Trade a Microsoft ID token for an app session cookie, applying any pending invitation code.
  const exchange = useCallback(async (idToken: string): Promise<void> => {
    const invite = sessionStorage.getItem('pending_invite')
    const res = await apiFetch('/auth/session', {
      method: 'POST',
      body: JSON.stringify({ provider: 'Microsoft', token: idToken, invitationCode: invite }),
    })
    if (res.ok) {
      sessionStorage.removeItem('pending_invite')
      setMe((await res.json()) as Me)
    } else if (res.status === 403) {
      const problem = (await res.json().catch(() => null)) as { title?: string } | null
      setError(problem?.title ?? 'You need an invitation to join a household.')
    } else {
      setError('Sign-in failed. Please try again.')
    }
  }, [])

  useEffect(() => {
    void (async () => {
      try {
        if (initialRedirect?.idToken) {
          // Just returned from Microsoft: complete sign-in.
          await exchange(initialRedirect.idToken)
        } else {
          // Otherwise, see if a session cookie already identifies us.
          const res = await apiFetch('/auth/me')
          if (res.ok) {
            setMe((await res.json()) as Me)
          }
        }
      } catch {
        // Leave the signed-out view showing.
      } finally {
        setLoading(false)
        setChecking(false)
      }
    })()
  }, [exchange, initialRedirect])

  const signIn = useCallback(async () => {
    setError(null)
    await msal.loginRedirect(loginRequest)
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
        {urlInvite && <p>You’ve been invited to join a household. Sign in to accept.</p>}
        <button onClick={signIn}>Sign in with Microsoft</button>
        {checking && <p className="hint">Checking for an existing session…</p>}
        {error && <p role="alert">{error}</p>}
      </main>
    )
  }

  return <Dashboard me={me} onSignOut={signOut} />
}

export default App
