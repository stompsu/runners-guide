import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter } from 'react-router-dom'
import './index.css'
import App from './App'
import { MsalProvider } from '@azure/msal-react'
import { entraClient, initializeEntra } from './auth/entra'

const queryClient = new QueryClient()

async function start() {
  await initializeEntra()
  const app = <App />
  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          {entraClient ? <MsalProvider instance={entraClient}>{app}</MsalProvider> : app}
        </BrowserRouter>
      </QueryClientProvider>
    </StrictMode>,
  )
}

void start()
