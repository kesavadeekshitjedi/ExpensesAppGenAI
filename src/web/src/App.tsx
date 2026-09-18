import { useEffect, useState } from 'react'

type ApiStatus = 'checking' | 'healthy' | 'unreachable'

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL

function App() {
  const [status, setStatus] = useState<ApiStatus>('checking')

  useEffect(() => {
    fetch(`${apiBaseUrl}/health`)
      .then((response) => setStatus(response.ok ? 'healthy' : 'unreachable'))
      .catch(() => setStatus('unreachable'))
  }, [])

  return (
    <main>
      <h1>Home Expenses</h1>
      <p>
        API status: <strong data-status={status}>{status}</strong>
      </p>
    </main>
  )
}

export default App
