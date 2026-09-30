import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { msal } from './auth/msal'

// MSAL must be initialized once before any sign-in call (top-level await is fine in a Vite module).
await msal.initialize()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
