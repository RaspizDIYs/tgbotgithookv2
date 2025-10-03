import { useEffect, useState } from 'react'
import { Button } from './button'

export function ThemeToggle() {
  const [dark, setDark] = useState<boolean>(() => document.documentElement.classList.contains('dark'))

  useEffect(() => {
    if (dark) {
      document.documentElement.classList.add('dark')
      localStorage.setItem('theme', 'dark')
    } else {
      document.documentElement.classList.remove('dark')
      localStorage.setItem('theme', 'light')
    }
  }, [dark])

  return (
    <Button variant="outline" size="icon" aria-label="Toggle theme" onClick={() => setDark((v) => !v)}>
      <span className="material-icons align-middle">{dark ? 'dark_mode' : 'light_mode'}</span>
    </Button>
  )
}


