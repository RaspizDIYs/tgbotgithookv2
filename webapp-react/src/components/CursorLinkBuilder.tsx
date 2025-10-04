import { useState } from 'react'
import { Input } from './ui/input'
import { Button } from './ui/button'
import { TooltipProvider, Tooltip, TooltipTrigger, TooltipContent } from './ui/tooltip'
import type { Language } from '../locales/translations'

export default function CursorLinkBuilder({ }: { language?: Language }) {
  const [type, setType] = useState<'prompt' | 'file'>('prompt')
  const [prompt, setPrompt] = useState('')
  const [ws, setWs] = useState('')
  const [fp, setFp] = useState('')
  const [line, setLine] = useState('')
  const [col, setCol] = useState('')
  const [url, setUrl] = useState('')

  function build() {
    if (type === 'prompt') {
      if (!prompt.trim()) return
      setUrl(`cursor://anysphere.cursor-deeplink/prompt?text=${encodeURIComponent(prompt.trim())}`)
      return
    }
    if (!ws.trim() || !fp.trim()) return
    const normWs = ws.trim().replace(/\\\\/g, '/')
    const normFp = fp.trim().replace(/^\/+/, '')
    let u = `cursor://file/${normWs}/${normFp}`
    const qs: string[] = []
    if (line) qs.push('line=' + encodeURIComponent(line))
    if (col) qs.push('column=' + encodeURIComponent(col))
    if (qs.length) u += '?' + qs.join('&')
    setUrl(u)
  }

  function openInBrowser() {
    if (!url) return
    // Открываем диплинк в браузере
    window.open(url, '_blank')
  }

  return (
    <TooltipProvider>
      <div className="space-y-3">
        <div className="flex gap-2">
          <Tooltip>
            <TooltipTrigger asChild>
              <Button variant={type==='prompt'?'default':'outline'} size="sm" onClick={()=>setType('prompt')}>
                <span className="material-icons text-base">chat</span>
              </Button>
            </TooltipTrigger>
            <TooltipContent>Промпт</TooltipContent>
          </Tooltip>
          <Tooltip>
            <TooltipTrigger asChild>
              <Button variant={type==='file'?'default':'outline'} size="sm" onClick={()=>setType('file')}>
                <span className="material-icons text-base">description</span>
              </Button>
            </TooltipTrigger>
            <TooltipContent>Файл</TooltipContent>
          </Tooltip>
        </div>
        {type==='prompt' ? (
          <div className="space-y-2">
            <Input placeholder="Введите промпт" value={prompt} onChange={(e)=>setPrompt(e.target.value)} />
            <div className="flex gap-2">
              <Tooltip>
                <TooltipTrigger asChild>
                  <Button size="sm" onClick={build}>
                    <span className="material-icons text-base">build</span>
                  </Button>
                </TooltipTrigger>
                <TooltipContent>Сгенерировать ссылку</TooltipContent>
              </Tooltip>
            </div>
          </div>
        ) : (
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
            <Input placeholder="C:/project/myapp" value={ws} onChange={(e)=>setWs(e.target.value)} />
            <Input placeholder="src/components/App.tsx" value={fp} onChange={(e)=>setFp(e.target.value)} />
            <Input placeholder="42" value={line} onChange={(e)=>setLine(e.target.value)} />
            <Input placeholder="10" value={col} onChange={(e)=>setCol(e.target.value)} />
            <div className="sm:col-span-2">
              <Tooltip>
                <TooltipTrigger asChild>
                  <Button size="sm" onClick={build}>
                    <span className="material-icons text-base">build</span>
                  </Button>
                </TooltipTrigger>
                <TooltipContent>Сгенерировать ссылку</TooltipContent>
              </Tooltip>
            </div>
          </div>
        )}
        {url && (
          <div className="space-y-2">
            <Input readOnly value={url} />
            <div className="flex gap-2">
              <Tooltip>
                <TooltipTrigger asChild>
                  <Button variant="secondary" size="sm" onClick={()=>{ try{navigator.clipboard.writeText(url)}catch{}}}>
                    <span className="material-icons text-base">content_copy</span>
                  </Button>
                </TooltipTrigger>
                <TooltipContent>Копировать ссылку</TooltipContent>
              </Tooltip>
              <Tooltip>
                <TooltipTrigger asChild>
                  <Button variant="default" size="sm" onClick={openInBrowser}>
                    <span className="material-icons text-base">open_in_browser</span>
                  </Button>
                </TooltipTrigger>
                <TooltipContent>Открыть в Cursor</TooltipContent>
              </Tooltip>
            </div>
          </div>
        )}
      </div>
    </TooltipProvider>
  )
}


