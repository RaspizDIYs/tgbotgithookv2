import * as React from 'react'

type TooltipContextValue = {
  open: boolean
  setOpen: (v: boolean) => void
}

const TooltipCtx = React.createContext<TooltipContextValue | null>(null)

export function TooltipProvider({ children }: { children: React.ReactNode }) {
  return <>{children}</>
}

export function Tooltip({ children }: { children: React.ReactNode }) {
  const [open, setOpen] = React.useState(false)
  return (
    <TooltipCtx.Provider value={{ open, setOpen }}>{children}</TooltipCtx.Provider>
  )
}

export const TooltipTrigger = React.forwardRef<
  HTMLButtonElement,
  React.ButtonHTMLAttributes<HTMLButtonElement>
>(function TooltipTrigger({ onMouseEnter, onMouseLeave, onFocus, onBlur, ...props }, ref) {
  const ctx = React.useContext(TooltipCtx)
  return (
    <button
      ref={ref}
      onMouseEnter={(e) => { ctx?.setOpen(true); onMouseEnter?.(e) }}
      onMouseLeave={(e) => { ctx?.setOpen(false); onMouseLeave?.(e) }}
      onFocus={(e) => { ctx?.setOpen(true); onFocus?.(e) }}
      onBlur={(e) => { ctx?.setOpen(false); onBlur?.(e) }}
      {...props}
    />
  )
})

export const TooltipContent = React.forwardRef<
  HTMLDivElement,
  React.HTMLAttributes<HTMLDivElement>
>(function TooltipContent({ className, style, ...props }, ref) {
  const ctx = React.useContext(TooltipCtx)
  if (!ctx) return null
  return ctx.open ? (
    <div
      ref={ref}
      role="tooltip"
      className={[
        'z-50 rounded-md border bg-popover text-popover-foreground shadow-md px-2 py-1 text-xs',
        'animate-in fade-in-0 zoom-in-95',
        className
      ].filter(Boolean).join(' ')}
      style={{ position: 'absolute', transform: 'translate(-50%, -8px)', left: '50%', bottom: '100%', ...style }}
      {...props}
    />
  ) : null
})
