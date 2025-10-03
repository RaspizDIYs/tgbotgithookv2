import React, { useCallback, useMemo, useRef, useState, useEffect } from 'react';
import { apiPost } from '../lib/api';
import { Button } from './ui/button';
import { TooltipProvider, Tooltip, TooltipTrigger, TooltipContent } from './ui/tooltip';

type ChatMessage = { author: 'you' | 'ai'; text: string };

export function Chat({ full = false, aiEnabled = true, onPendingChange }: { full?: boolean; aiEnabled?: boolean; onPendingChange?: (pending: boolean) => void }) {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState('');
  const listRef = useRef<HTMLDivElement | null>(null);
  const textareaRef = useRef<HTMLTextAreaElement | null>(null);
  const [pending, setPending] = useState<boolean>(false);
  const [copyIndex, setCopyIndex] = useState<number | null>(null);

  const append = useCallback((m: ChatMessage) => {
    setMessages((prev) => [...prev, m]);
    setTimeout(() => {
      if (listRef.current) listRef.current.scrollTop = listRef.current.scrollHeight;
    }, 0);
  }, []);

  const handleSend = useCallback(async () => {
    const text = input.trim();
    if (!text) return;
    if (!aiEnabled) {
      append({ author: 'ai', text: 'AI выключен' });
      return;
    }
    append({ author: 'you', text });
    setInput('');
    setPending(true);
    onPendingChange?.(true);
    try {
      const data = await apiPost<{ reply: string }>(`/api/ai/chat`, { text });
      append({ author: 'ai', text: data.reply || '...' });
    } catch {
      append({ author: 'ai', text: 'Ответ недоступен сейчас.' });
    } finally {
      setPending(false);
      onPendingChange?.(false);
    }
  }, [append, input, aiEnabled]);

  const onKeyDown = useCallback((e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey && !e.ctrlKey && !e.altKey && !e.metaKey) {
      e.preventDefault();
      e.stopPropagation();
      void handleSend();
    }
  }, [handleSend]);

  const resizeTextarea = useCallback(() => {
    const el = textareaRef.current;
    if (!el) return;
    el.style.height = 'auto';
    const styles = window.getComputedStyle(el);
    const lineHeight = parseFloat(styles.lineHeight || '20');
    const maxRows = 3;
    const maxHeight = lineHeight * maxRows + parseFloat(styles.paddingTop) + parseFloat(styles.paddingBottom);
    el.style.height = Math.min(el.scrollHeight, maxHeight) + 'px';
  }, []);

  useEffect(() => {
    resizeTextarea();
  }, [input, resizeTextarea]);

  const renderBlocks = useCallback((text: string) => {
    const parts: Array<{ type: 'code' | 'text'; content: string; lang?: string }> = [];
    const regex = /```(\w+)?\n([\s\S]*?)\n```/g;
    let lastIndex = 0;
    let match: RegExpExecArray | null;
    while ((match = regex.exec(text)) !== null) {
      const [full, lang, code] = match;
      const start = match.index;
      if (start > lastIndex) parts.push({ type: 'text', content: text.slice(lastIndex, start) });
      parts.push({ type: 'code', content: code, lang: lang || undefined });
      lastIndex = start + full.length;
    }
    if (lastIndex < text.length) parts.push({ type: 'text', content: text.slice(lastIndex) });
    return parts;
  }, []);

  const renderMessage = useCallback((m: ChatMessage, i: number) => {
    const isAI = m.author === 'ai';
    const side = isAI ? 'items-start' : 'items-end';
    const bubble = isAI
      ? 'bg-muted text-foreground rounded-2xl rounded-tl-sm'
      : 'bg-primary text-primary-foreground rounded-2xl rounded-tr-sm';
    const avatarBg = isAI ? 'bg-blue-500' : 'bg-emerald-500';
    const initial = isAI ? 'AI' : 'Вы';
    const blocks = renderBlocks(m.text);

    return (
      <div key={i} className={`flex ${side} gap-2 mb-3`}>
        {isAI && (
          <div className={`h-7 w-7 shrink-0 rounded-full ${avatarBg} grid place-items-center text-[10px] font-semibold text-white`}>{initial}</div>
        )}
        <div className={`max-w-[85%] sm:max-w-[75%] ${bubble} px-3 py-2 shadow-sm border border-border`}> 
          {blocks.map((b, idx) =>
            b.type === 'code' ? (
              <pre key={idx} className="my-2 overflow-x-auto rounded-md border bg-background text-foreground p-3 text-xs">
                <code>{b.content}</code>
              </pre>
            ) : (
              <div key={idx} className="whitespace-pre-wrap leading-relaxed">
                {b.content}
              </div>
            )
          )}
          <TooltipProvider>
            <div className="mt-1 flex justify-end gap-1 opacity-70">
              <Tooltip>
                <TooltipTrigger
                  aria-label="Копировать"
                  className="h-7 w-7 grid place-items-center rounded-md hover:bg-foreground/10"
                  onClick={() => {
                    void navigator.clipboard.writeText(m.text).then(() => {
                      setCopyIndex(i);
                      setTimeout(() => setCopyIndex((v) => (v === i ? null : v)), 1200);
                    });
                  }}
                >
                  <span className="material-icons text-base">content_copy</span>
                </TooltipTrigger>
                <TooltipContent>Копировать</TooltipContent>
              </Tooltip>
              <Tooltip>
                <TooltipTrigger
                  aria-label="Отправить"
                  className="h-7 w-7 grid place-items-center rounded-md hover:bg-foreground/10"
                  onClick={() => append({ author: 'you', text: m.text })}
                >
                  <span className="material-icons text-base">send</span>
                </TooltipTrigger>
                <TooltipContent>Отправить</TooltipContent>
              </Tooltip>
            </div>
          </TooltipProvider>
        </div>
        {!isAI && (
          <div className={`h-7 w-7 shrink-0 rounded-full ${avatarBg} grid place-items-center text-[10px] font-semibold text-white`}>{initial}</div>
        )}
      </div>
    );
  }, [renderBlocks, copyIndex]);

  return (
    <div className={full ? 'flex flex-col h-[calc(100vh-140px)]' : 'flex flex-col gap-3'}>
      <div
        ref={listRef}
        className={full
          ? 'flex-1 overflow-auto rounded-md border p-3 bg-card text-card-foreground'
          : 'min-h-[240px] max-h-[60vh] overflow-auto rounded-md border p-3 bg-card text-card-foreground'}
      >
        {messages.map((m, i) => renderMessage(m, i))}
        {pending && (
          <div className="flex items-start gap-2 mb-1">
            <div className="h-7 w-7 shrink-0 rounded-full bg-blue-500 grid place-items-center text-[10px] font-semibold text-white">AI</div>
            <div className="bg-muted rounded-2xl rounded-tl-sm px-3 py-2 border border-border">
              <span className="inline-flex gap-1">
                <span className="w-2 h-2 bg-foreground/50 rounded-full animate-pulse"></span>
                <span className="w-2 h-2 bg-foreground/50 rounded-full animate-pulse [animation-delay:100ms]"></span>
                <span className="w-2 h-2 bg-foreground/50 rounded-full animate-pulse [animation-delay:200ms]"></span>
              </span>
            </div>
          </div>
        )}
      </div>
      <div className={full ? 'mt-auto sticky left-0 right-0 bg-background/80 backdrop-blur border-t' : ''} style={full ? { bottom: 100 } : undefined}>
        <div className="max-w-3xl mx-auto px-3 py-2">
          <div className="relative w-full border rounded-md pl-3 pr-10 py-1.5">
            <textarea
              ref={textareaRef}
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={onKeyDown as any}
              rows={1}
              placeholder="Напишите сообщение..."
              className="w-full bg-transparent outline-none resize-none py-1.5 pr-10 leading-6 max-h-[7.5rem]"
            />
            <div className="absolute right-1.5 top-1/2 -translate-y-1/2">
              <Button size="icon" variant="ghost" onClick={() => void handleSend()} aria-label="Отправить" className="rounded-md bg-transparent hover:bg-accent text-foreground h-8 w-8">
                <span className="material-icons">send</span>
              </Button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}


