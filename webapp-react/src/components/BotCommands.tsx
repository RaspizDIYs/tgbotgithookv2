import React, { useMemo, useState } from 'react';
import { apiPost } from '../lib/api';
import { Button } from './ui/button';
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from './ui/tooltip';

type Command = { label: string; command: string; group: 'ai' | 'gif' | 'games' | 'cursor' | 'github' | 'achievements'; hint?: string };

export function BotCommands({ group }: { group: 'ai' | 'gif' | 'games' | 'cursor' | 'github' | 'achievements' }) {
  const [busy, setBusy] = useState<string | null>(null);
  const commands: Command[] = useMemo(() => ([
    // AI (старт/стоп удалены — используется тумблер во вью)
    { group: 'ai', label: 'Статы', command: '/glaistats' },
    { group: 'ai', label: 'Текущий', command: '/glaicurrent' },
    { group: 'ai', label: 'Свитч', command: '/glaiswitch' },
    { group: 'ai', label: 'Очистить', command: '/glaiclear' },
    // Cursor / WebApp
    { group: 'cursor', label: 'Открыть WebApp', command: '/webapp' },
    { group: 'cursor', label: 'Cursor меню', command: '/cursor' },
    // Игры
    { group: 'games', label: 'Игры меню', command: '/game' },
    { group: 'games', label: 'Мемы', command: '/gamememe' },
    { group: 'games', label: 'LoL', command: '/gamelol' },
    { group: 'games', label: 'Программирование', command: '/gameprogramming' },
    { group: 'games', label: 'Стоп игра', command: '/gamestop' },
    { group: 'games', label: 'Тест игры', command: '/gametest' },
    // GIF
    { group: 'gif', label: 'GIF поиск', command: '/gifsearch котики' },
    { group: 'gif', label: 'GIF рандом', command: '/gifrandom' },
    { group: 'gif', label: 'GIF текст', command: '/giftext' },
    { group: 'gif', label: 'GIF настройки', command: '/gifsettings' },
    { group: 'gif', label: 'GIF цвет', command: '/gifcolor' },
    { group: 'gif', label: 'GIF позиция', command: '/gifposition' },
    // GitHub (через REST эндпоинты Program.cs)
    { group: 'github', label: 'Git статистика', command: '/gitstats' },
    { group: 'github', label: 'Коммиты', command: '/gitcommits' },
    // Ачивки/стата (триггер текстом)
    { group: 'achievements', label: 'Активность чата', command: '/chatactivity' },
    { group: 'achievements', label: 'Сброс активности', command: '/resetactivity' },
    // Диагностика
    { group: 'gif', label: 'Тест Tenor', command: '/testtenor' },
  ]), []);

  async function send(cmd: Command) {
    if (busy) return;
    setBusy(cmd.command);
    try {
      await apiPost('/api/bot/command', { command: cmd.command });
    } finally {
      setBusy(null);
    }
  }

  const aiIcon = (cmd: string): string => {
    switch (cmd) {
      case '/glaistats': return 'bar_chart';
      case '/glaicurrent': return 'person_search';
      case '/glaiswitch': return 'sync_alt';
      case '/glaiclear': return 'delete_sweep';
      default: return 'smart_toy';
    }
  };

  return (
    <div className="p-0">
      {group === 'ai' ? (
        <TooltipProvider>
          <div className="flex items-center gap-2 overflow-x-auto no-scrollbar">
            {commands.filter(c => c.group === 'ai').map((c) => (
              <Tooltip key={c.command}>
                <div className="relative">
                  <TooltipTrigger
                    onClick={() => void send(c)}
                    disabled={busy === c.command}
                    className="h-10 w-10 grid place-items-center rounded-full border hover:bg-accent disabled:opacity-50"
                    aria-label={c.label}
                  >
                    <span className="material-icons text-base">{aiIcon(c.command)}</span>
                  </TooltipTrigger>
                  <div className="absolute left-1/2 top-0" />
                  <TooltipContent>{c.label}</TooltipContent>
                </div>
              </Tooltip>
            ))}
          </div>
        </TooltipProvider>
      ) : (
        <div className="flex flex-wrap gap-2">
          {commands.filter(c => c.group === group).map((c) => (
            <Button key={c.command} variant="outline" onClick={() => void send(c)} disabled={busy === c.command} className="px-3 py-2 text-sm">
              {c.label}
            </Button>
          ))}
        </div>
      )}
    </div>
  );
}


