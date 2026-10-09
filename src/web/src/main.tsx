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
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js').catch(() => {})
  })
}
