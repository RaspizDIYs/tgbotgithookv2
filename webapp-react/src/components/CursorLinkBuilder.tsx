import { useState } from 'react'
import { Input } from './ui/input'
import { Button } from './ui/button'

export default function CursorLinkBuilder() {
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

  return (
    <div className="space-y-3">
      <div className="flex gap-2">
        <Button variant={type==='prompt'?'default':'outline'} onClick={()=>setType('prompt')}>Промпт</Button>
        <Button variant={type==='file'?'default':'outline'} onClick={()=>setType('file')}>Файл</Button>
      </div>
      {type==='prompt' ? (
        <div className="space-y-2">
          <Input placeholder="Введите промпт" value={prompt} onChange={(e)=>setPrompt(e.target.value)} />
          <Button onClick={build}>Сгенерировать</Button>
        </div>
      ) : (
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
          <Input placeholder="C:/project/myapp" value={ws} onChange={(e)=>setWs(e.target.value)} />
          <Input placeholder="src/components/App.tsx" value={fp} onChange={(e)=>setFp(e.target.value)} />
          <Input placeholder="42" value={line} onChange={(e)=>setLine(e.target.value)} />
          <Input placeholder="10" value={col} onChange={(e)=>setCol(e.target.value)} />
          <div className="sm:col-span-2">
            <Button onClick={build}>Сгенерировать</Button>
          </div>
        </div>
      )}
      {url && (
        <div className="space-y-2">
          <Input readOnly value={url} />
          <Button variant="secondary" onClick={()=>{ try{navigator.clipboard.writeText(url)}catch{}}}>Копировать</Button>
        </div>
      )}
    </div>
  )
}


