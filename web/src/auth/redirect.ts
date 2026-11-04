import { broadcastResponseToMainFrame } from '@azure/msal-browser/redirect-bridge'

// MSAL 5 popup/iframe callback: no router, application mount, or API requests here.
void broadcastResponseToMainFrame().catch(() => {
  document.getElementById('status')!.textContent = 'Sign-in could not be completed. Close this window and try again.'
})
