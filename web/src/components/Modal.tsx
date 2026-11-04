import * as Dialog from '@radix-ui/react-dialog'
import type { ReactNode } from 'react'
import { useEffect, useRef, useState } from 'react'
import { en } from '../i18n'

export function Modal({ title, description, children, onClose }: {
  title: string; description: string; children: ReactNode; onClose: () => void
}) {
  const [opener] = useState(() => document.activeElement as HTMLElement | null)
  const content = useRef<HTMLDivElement>(null)
  useEffect(() => {
    content.current?.querySelector<HTMLElement>('[data-cancel-delete], h2')?.focus()
  }, [title])
  return <Dialog.Root open onOpenChange={open => { if (!open) onClose() }}>
    <Dialog.Portal>
      <Dialog.Overlay className="modal-overlay" />
      <Dialog.Content ref={content} className="modal-content" onCloseAutoFocus={event => {
        // Panels are opened by multiple calendar controls, not a single Dialog.Trigger.
        event.preventDefault()
        if (opener?.isConnected) opener.focus()
        else document.querySelector<HTMLElement>('[data-panel-return]')?.focus()
      }}>
        <div className="toolbar">
          <Dialog.Title tabIndex={-1}>{title}</Dialog.Title>
          <Dialog.Close aria-label={en.panel.close}>×</Dialog.Close>
        </div>
        <Dialog.Description className="muted">{description}</Dialog.Description>
        {children}
      </Dialog.Content>
    </Dialog.Portal>
  </Dialog.Root>
}
