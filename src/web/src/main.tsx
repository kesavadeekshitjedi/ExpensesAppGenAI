import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { initAuth } from './auth/msal'

// Initialize MSAL and process a returning redirect before rendering (top-level await is fine here).
const initialRedirect = await initAuth()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App initialRedirect={initialRedirect} />
  </StrictMode>,
)

// Register the service worker so the app is installable on the home screen and loads offline.
if ('serviceWorker' in navigator) {
  // When a newly deployed service worker takes control, reload once so the open tab picks up the new
  // build instead of serving the previous one from cache. Only reload if a worker was already in control
  // (so the very first visit, which has no prior controller, doesn't reload).
  const hadController = !!navigator.serviceWorker.controller
  let reloading = false
  navigator.serviceWorker.addEventListener('controllerchange', () => {
    if (!hadController || reloading) return
    reloading = true
    window.location.reload()
  })
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js').catch(() => {})
  })
}
