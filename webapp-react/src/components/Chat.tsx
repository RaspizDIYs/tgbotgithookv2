import { useCallback, useRef, useState, useEffect } from 'react';
import { apiPost } from '../lib/api';
import { TooltipProvider, Tooltip, TooltipTrigger, TooltipContent } from './ui/tooltip';

type ChatMessage = { author: 'you' | 'ai'; text: string };

export function Chat({ aiEnabled = true, onPendingChange, onMessageSent }: { aiEnabled?: boolean; onPendingChange?: (pending: boolean) => void; onMessageSent?: (sendFunction: (text: string) => void) => void }) {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const listRef = useRef<HTMLDivElement | null>(null);
  const [pending, setPending] = useState<boolean>(false);
  const [copyIndex, setCopyIndex] = useState<number | null>(null);

  const append = useCallback((m: ChatMessage) => {
    setMessages((prev) => [...prev, m]);
    setTimeout(() => {
      if (listRef.current) listRef.current.scrollTop = listRef.current.scrollHeight;
    }, 0);
  }, []);

  const handleSend = useCallback(async (text: string) => {
    if (!text.trim()) return;
    if (!aiEnabled) {
      append({ author: 'ai', text: 'AI выключен' });
      return;
    }
    append({ author: 'you', text });
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
  }, [append, aiEnabled]);

  // Экспортируем функцию для внешнего использования
  useEffect(() => {
    if (onMessageSent) {
      onMessageSent(handleSend);
    }
  }, [onMessageSent, handleSend]);



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
    const bubble = isAI
      ? 'bg-muted text-foreground rounded-lg rounded-tl-sm'
      : 'bg-primary text-primary-foreground rounded-lg rounded-tr-sm';
    const avatarBg = isAI ? 'bg-blue-500' : 'bg-emerald-500';
    const initial = isAI ? 'AI' : 'Вы';
    const blocks = renderBlocks(m.text);

    return (
      <div key={i} className={`flex ${isAI ? 'items-start' : 'items-end justify-end'} gap-2 sm:gap-3 mb-3 sm:mb-4`}>
        {isAI && (
          <div className={`h-6 w-6 sm:h-8 sm:w-8 shrink-0 rounded-full ${avatarBg} grid place-items-center text-[9px] sm:text-[11px] font-semibold text-white shadow-sm`}>{initial}</div>
        )}
        <div className={`relative max-w-[85%] sm:max-w-[80%] ${bubble} px-3 sm:px-4 py-1 shadow-lg border border-border/50 backdrop-blur-sm`}> 
          {blocks.map((b, idx) =>
            b.type === 'code' ? (
              <pre key={idx} className="my-2 overflow-x-auto rounded-md border bg-background text-foreground p-3 text-xs">
                <code>{b.content}</code>
              </pre>
            ) : (
              <div key={idx} className="whitespace-pre-wrap leading-relaxed text-xs sm:text-sm">
                {b.content}
              </div>
            )
          )}
        </div>
        {isAI && (
          <TooltipProvider>
            <Tooltip>
              <TooltipTrigger
                aria-label="Копировать"
                className="h-2.5 w-2.5 sm:h-3 sm:w-3 grid place-items-center rounded hover:bg-foreground/15 opacity-40 hover:opacity-100 transition-opacity ml-1"
                onClick={() => {
                  void navigator.clipboard.writeText(m.text).then(() => {
                    setCopyIndex(i);
                    setTimeout(() => setCopyIndex((v) => (v === i ? null : v)), 1200);
                  });
                }}
              >
                <span className="material-icons text-[8px] sm:text-[10px]">content_copy</span>
              </TooltipTrigger>
              <TooltipContent>Копировать</TooltipContent>
            </Tooltip>
          </TooltipProvider>
        )}
        {!isAI && (
          <div className={`h-6 w-6 sm:h-8 sm:w-8 shrink-0 rounded-full ${avatarBg} grid place-items-center text-[9px] sm:text-[11px] font-semibold text-white shadow-sm`}>{initial}</div>
        )}
      </div>
    );
  }, [renderBlocks, copyIndex]);

  return (
    <div className="flex flex-col">
      <div
        ref={listRef}
        className="flex-1 overflow-auto p-2 sm:p-4"
      >
        {messages.map((m, i) => renderMessage(m, i))}
        {pending && (
          <div className="flex items-start gap-2 mb-1">
            <div className="h-6 w-6 sm:h-7 sm:w-7 shrink-0 rounded-full bg-blue-500 grid place-items-center text-[8px] sm:text-[10px] font-semibold text-white">AI</div>
            <div className="bg-muted rounded-2xl rounded-tl-sm px-2 sm:px-3 py-1 sm:py-2 border border-border">
              <span className="inline-flex gap-1">
                <span className="w-1.5 h-1.5 sm:w-2 sm:h-2 bg-foreground/50 rounded-full animate-pulse"></span>
                <span className="w-1.5 h-1.5 sm:w-2 sm:h-2 bg-foreground/50 rounded-full animate-pulse [animation-delay:100ms]"></span>
                <span className="w-1.5 h-1.5 sm:w-2 sm:h-2 bg-foreground/50 rounded-full animate-pulse [animation-delay:200ms]"></span>
              </span>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}