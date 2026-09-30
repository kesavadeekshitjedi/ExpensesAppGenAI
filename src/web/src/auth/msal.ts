import { PublicClientApplication } from '@azure/msal-browser'

// MSAL is Microsoft's browser sign-in library. It runs a public-client flow with PKCE (no secret):
// the browser gets a signed ID token from Microsoft, which we hand to our API to start a session.
// The client ID is the Entra app registration's Application ID (non-secret, injected at build time).
const clientId = import.meta.env.VITE_ENTRA_CLIENT_ID

export const msal = new PublicClientApplication({
  auth: {
    clientId,
    // "common" allows both personal Microsoft accounts and work/school accounts.
    authority: 'https://login.microsoftonline.com/common',
    redirectUri: window.location.origin,
  },
  cache: {
    cacheLocation: 'sessionStorage',
  },
})

// Minimal scopes: we only need the ID token identifying the user, not access to Microsoft APIs.
export const loginRequest = { scopes: ['openid', 'profile', 'email'] }

// Initialize MSAL and complete a sign-in that is returning via redirect. Returns the auth result
// (with the ID token) when we've just come back from Microsoft, otherwise null.
export async function initAuth() {
  await msal.initialize()
  return msal.handleRedirectPromise()
}
