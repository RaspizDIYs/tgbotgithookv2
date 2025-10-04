import React, { useEffect, useState } from 'react'
import { Button } from './button'

export const ThemeToggle = React.forwardRef<HTMLButtonElement>((props, ref) => {
  const [dark, setDark] = useState<boolean>(() => {
    // Проверяем localStorage, затем системную тему
    const savedTheme = localStorage.getItem('theme')
    if (savedTheme) {
      return savedTheme === 'dark'
    }
    return window.matchMedia('(prefers-color-scheme: dark)').matches
  })

  useEffect(() => {
    // Инициализируем тему при загрузке
    const root = document.documentElement
    if (dark) {
      root.classList.add('dark')
      localStorage.setItem('theme', 'dark')
    } else {
      root.classList.remove('dark')
      localStorage.setItem('theme', 'light')
    }
  }, [dark])

  useEffect(() => {
    // Принудительно применяем тему при монтировании
    const root = document.documentElement
    const savedTheme = localStorage.getItem('theme')
    if (savedTheme === 'dark') {
      root.classList.add('dark')
    } else {
      root.classList.remove('dark')
    }
  }, [])

  useEffect(() => {
    // Слушаем изменения системной темы
    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)')
    const handleChange = (e: MediaQueryListEvent) => {
      if (!localStorage.getItem('theme')) {
        setDark(e.matches)
      }
    }
    mediaQuery.addEventListener('change', handleChange)
    return () => mediaQuery.removeEventListener('change', handleChange)
  }, [])

  const handleClick = () => {
    console.log('Theme toggle clicked, current dark:', dark)
    setDark((v) => !v)
  }

  return (
    <Button 
      ref={ref} 
      variant="ghost" 
      size="icon" 
      aria-label="Toggle theme" 
      onClick={handleClick}
      className="hover:bg-accent hover:text-accent-foreground border border-border/50"
      style={{ pointerEvents: 'auto', zIndex: 60 }}
      {...props}
    >
      <span className="material-icons align-middle">{dark ? 'light_mode' : 'dark_mode'}</span>
    </Button>
  )
})

ThemeToggle.displayName = 'ThemeToggle'


