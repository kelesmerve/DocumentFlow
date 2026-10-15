import { useEffect, useId, useRef, type KeyboardEvent, type ReactNode } from 'react'
import { X } from 'lucide-react'

type DialogProps = {
  title: string
  children: ReactNode
  onClose: () => void
  busy?: boolean
  className?: string
}

const focusable = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

export function Dialog({ title, children, onClose, busy = false, className = '' }: DialogProps) {
  const titleId = useId()
  const panelRef = useRef<HTMLElement>(null)
  const previousFocus = useRef<HTMLElement | null>(null)

  useEffect(() => {
    previousFocus.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    const panel = panelRef.current
    const initial = panel?.querySelector<HTMLElement>('[data-dialog-initial-focus]') ?? panel?.querySelector<HTMLElement>(focusable)
    initial?.focus()
    return () => {
      document.body.style.overflow = previousOverflow
      previousFocus.current?.focus()
    }
  }, [])

  function onKeyDown(event: KeyboardEvent<HTMLElement>) {
    if (event.key === 'Escape') {
      event.stopPropagation()
      if (!busy) onClose()
      return
    }
    if (event.key !== 'Tab') return
    const elements = Array.from(panelRef.current?.querySelectorAll<HTMLElement>(focusable) ?? [])
    if (!elements.length) { event.preventDefault(); panelRef.current?.focus(); return }
    const first = elements[0]!
    const last = elements[elements.length - 1]!
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus() }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
  }

  return <div className="dialog-backdrop" onMouseDown={(event) => { if (!busy && event.target === event.currentTarget) onClose() }}>
    <section ref={panelRef} className={`workflow-dialog ${className}`.trim()} role="dialog" aria-modal="true" aria-labelledby={titleId} onKeyDown={onKeyDown} tabIndex={-1}>
      <header><h2 id={titleId}>{title}</h2><button type="button" className="icon-button" onClick={onClose} disabled={busy} aria-label="Kapat" data-dialog-initial-focus><X size={18} /></button></header>
      {children}
    </section>
  </div>
}
