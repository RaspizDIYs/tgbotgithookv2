import React, { useCallback, useRef } from 'react';
import { Button } from './ui/button';
import { Textarea } from './ui/textarea';

export function ChatInput({ 
  onSend, 
  aiEnabled = true 
}: { 
  onSend: (text: string) => void; 
  aiEnabled?: boolean; 
}) {
  const [input, setInput] = React.useState('');
  const textareaRef = useRef<HTMLTextAreaElement | null>(null);

  const handleSend = useCallback(() => {
    const text = input.trim();
    if (!text) return;
    if (!aiEnabled) {
      return;
    }
    onSend(text);
    setInput('');
  }, [input, aiEnabled, onSend]);

  const onKeyDown = useCallback((e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey && !e.ctrlKey && !e.altKey && !e.metaKey) {
      e.preventDefault();
      e.stopPropagation();
      handleSend();
    }
  }, [handleSend]);

  return (
    <div className="border-t bg-background py-2 px-2 sm:px-4 sticky bottom-0 z-10">
      <div className="max-w-4xl mx-auto sm:max-w-full">
        <div className="relative w-full border rounded-md pl-2 sm:pl-3 pr-16 sm:pr-20 py-1 flex items-center">
          <Textarea
            ref={textareaRef}
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={onKeyDown as any}
            rows={1}
            placeholder="Напишите сообщение..."
            className="w-full bg-transparent outline-none resize-none py-1 pr-16 sm:pr-20 leading-4 max-h-[1.25rem] border-0 shadow-none focus-visible:ring-0 placeholder:text-sm sm:placeholder:text-base flex items-center text-sm sm:text-base"
          />
          <div className="absolute right-1 top-1/2 -translate-y-1/2 flex gap-0.5 sm:gap-1">
            <Button 
              size="icon" 
              variant="ghost" 
              aria-label="GIF" 
              className="rounded-md bg-transparent hover:bg-accent text-foreground h-6 w-6 sm:h-8 sm:w-8 text-xs font-medium"
              onClick={() => {
                // Отправляем команду для показа сохраненных GIF
                fetch('/api/bot/command', {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/json' },
                  body: JSON.stringify({ command: '/gifsaved' })
                }).catch(console.error);
              }}
            >
              <span className="text-xs sm:text-sm">GIF</span>
            </Button>
            <Button 
              size="icon" 
              variant="ghost" 
              onClick={() => void handleSend()} 
              aria-label="Отправить" 
              className="rounded-md bg-transparent hover:bg-accent text-foreground h-6 w-6 sm:h-8 sm:w-8"
            >
              <span className="material-icons text-sm sm:text-base">send</span>
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
