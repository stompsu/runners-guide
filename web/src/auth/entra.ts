import { PublicClientApplication } from '@azure/msal-browser'

const clientId = import.meta.env.VITE_ENTRA_CLIENT_ID
const authority = import.meta.env.VITE_ENTRA_AUTHORITY
export const apiScope = import.meta.env.VITE_ENTRA_API_SCOPE as string | undefined

// These are public SPA identifiers, never client secrets.
function configureEntra() {
  if (!clientId || !authority || !apiScope) return undefined
  try {
    const url = new URL(authority)
    if (url.protocol !== 'https:') return undefined
    return new PublicClientApplication({
      auth: {
        clientId,
        authority,
        knownAuthorities: [url.hostname],
        redirectUri: window.location.origin + '/auth-callback.html',
      },
      cache: { cacheLocation: 'memoryStorage' },
    })
  } catch {
    // Misconfigured public identifiers must not prevent guest access.
    return undefined
  }
}
export let entraClient = configureEntra()

export async function initializeEntra() {
  try {
    await entraClient?.initialize()
  } catch {
    // Keep the sign-in page (and guest flow) available after initialization failures.
    entraClient = undefined
  }
}
