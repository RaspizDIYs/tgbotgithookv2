import { useMemo, useState } from 'react';
import { apiPost } from '../lib/api';
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from './ui/tooltip';
import { useTranslation } from '../hooks/useTranslation';
import type { Language } from '../locales/translations';
import { GifColorPicker } from './GifColorPicker';
import { GifPositionPicker } from './GifPositionPicker';
import { GifSearchDialog } from './GifSearchDialog';
import { GifTextWizard } from './GifTextWizard';

type Command = { label: string; command: string; group: 'ai' | 'gif' | 'games' | 'cursor' | 'github' | 'achievements'; hint?: string };

export function BotCommands({ group, language, onGifReceived }: { group: 'ai' | 'gif' | 'games' | 'cursor' | 'github' | 'achievements'; language: Language; onGifReceived?: (gifUrl: string) => void }) {
  const { t } = useTranslation(language);
  const [busy, setBusy] = useState<string | null>(null);
  const [showColorPicker, setShowColorPicker] = useState(false);
  const [showPositionPicker, setShowPositionPicker] = useState(false);
  const [showSearchDialog, setShowSearchDialog] = useState(false);
  const [showTextWizard, setShowTextWizard] = useState(false);
  const commands: Command[] = useMemo(() => ([
    // AI
    { group: 'ai', label: t('aiStats'), command: '/glaistats' },
    { group: 'ai', label: t('aiCurrent'), command: '/glaicurrent' },
    { group: 'ai', label: t('aiSwitch'), command: '/glaiswitch' },
    { group: 'ai', label: t('aiClear'), command: '/glaiclear' },
    // Cursor / WebApp
    { group: 'cursor', label: t('openWebApp'), command: '/webapp' },
    { group: 'cursor', label: t('cursorMenu'), command: '/cursor' },
    // Игры
    { group: 'games', label: t('gamesMenu'), command: '/game' },
    { group: 'games', label: t('memes'), command: '/gamememe' },
    { group: 'games', label: t('lol'), command: '/gamelol' },
    { group: 'games', label: t('programming'), command: '/gameprogramming' },
    { group: 'games', label: t('stopGame'), command: '/gamestop' },
    { group: 'games', label: t('testGame'), command: '/gametest' },
    // GIF
    { group: 'gif', label: t('gifSearch'), command: '/gifsearch котики' },
    { group: 'gif', label: t('gifRandom'), command: '/gifrandom' },
    { group: 'gif', label: t('gifText'), command: '/giftext' },
    { group: 'gif', label: t('gifSettings'), command: '/gifsettings' },
    { group: 'gif', label: t('gifColor'), command: '/gifcolor' },
    { group: 'gif', label: t('gifPosition'), command: '/gifposition' },
    { group: 'gif', label: t('gifSaved'), command: '/gifsaved' },
    { group: 'gif', label: t('testTenor'), command: '/testtenor' },
    // GitHub
    { group: 'github', label: t('gitStats'), command: '/status' },
    { group: 'github', label: t('commits'), command: '/commits' },
    { group: 'github', label: t('branches'), command: '/branches' },
    { group: 'github', label: t('prs'), command: '/prs' },
    { group: 'github', label: t('ci'), command: '/ci' },
    { group: 'github', label: t('authors'), command: '/authors' },
    { group: 'github', label: t('search'), command: '/search' },
    // Ачивки/стата
    { group: 'achievements', label: t('chatActivity'), command: '/chatactivity' },
    { group: 'achievements', label: t('resetActivity'), command: '/resetactivity' },
    { group: 'achievements', label: t('statistics'), command: '/stats' },
    { group: 'achievements', label: t('achievements'), command: '/achievements' },
    { group: 'achievements', label: t('leaderboard'), command: '/leaderboard' },
    { group: 'achievements', label: t('streaks'), command: '/streaks' },
  ]), [t]);

  async function send(cmd: Command) {
    if (busy) return;
    
    // Обработка специальных GIF команд
    if (group === 'gif') {
      switch (cmd.command) {
        case '/gifsearch котики':
          setShowSearchDialog(true);
          return;
        case '/gifcolor':
          setShowColorPicker(true);
          return;
        case '/gifposition':
          setShowPositionPicker(true);
          return;
        case '/giftext':
          setShowTextWizard(true);
          return;
      }
    }
    
    setBusy(cmd.command);
    try {
      const response = await apiPost<{ gifUrl?: string; message?: string }>('/api/bot/command', { command: cmd.command });
      if (response.gifUrl && onGifReceived) {
        onGifReceived(response.gifUrl);
      }
    } finally {
      setBusy(null);
    }
  }

  const handleGifSearch = async (query: string) => {
    setBusy('/gifsearch');
    try {
      const response = await apiPost<{ gifUrl?: string; message?: string }>('/api/bot/command', { command: `/gifsearch ${query}` });
      if (response.gifUrl && onGifReceived) {
        onGifReceived(response.gifUrl);
      }
    } finally {
      setBusy(null);
    }
  };

  const handleColorSelect = async (color: string) => {
    setBusy('/gifcolor');
    try {
      const response = await apiPost<{ gifUrl?: string; message?: string }>('/api/bot/command', { command: `/gifcolor ${color}` });
      if (response.gifUrl && onGifReceived) {
        onGifReceived(response.gifUrl);
      }
    } finally {
      setBusy(null);
    }
  };

  const handlePositionSelect = async (position: string) => {
    setBusy('/gifposition');
    try {
      const response = await apiPost<{ gifUrl?: string; message?: string }>('/api/bot/command', { command: `/gifposition ${position}` });
      if (response.gifUrl && onGifReceived) {
        onGifReceived(response.gifUrl);
      }
    } finally {
      setBusy(null);
    }
  };

  const getIcon = (cmd: string): string => {
    switch (cmd) {
      // AI
      case '/glaistats': return 'bar_chart';
      case '/glaicurrent': return 'person_search';
      case '/glaiswitch': return 'sync_alt';
      case '/glaiclear': return 'delete_sweep';
      // Cursor
      case '/webapp': return 'web';
      case '/cursor': return 'code';
      // Games
      case '/game': return 'sports_esports';
      case '/gamememe': return 'emoji_emotions';
      case '/gamelol': return 'videogame_asset';
      case '/gameprogramming': return 'terminal';
      case '/gamestop': return 'stop_circle';
      case '/gametest': return 'bug_report';
      // GIF
      case '/gifsearch котики': return 'search';
      case '/gifrandom': return 'shuffle';
      case '/giftext': return 'text_fields';
      case '/gifsettings': return 'settings';
      case '/gifcolor': return 'palette';
      case '/gifposition': return 'open_with';
      case '/gifsaved': return 'bookmark';
      case '/testtenor': return 'api';
      // GitHub
      case '/status': return 'analytics';
      case '/commits': return 'commit';
      case '/branches': return 'account_tree';
      case '/prs': return 'merge';
      case '/ci': return 'build';
      case '/authors': return 'people';
      case '/search': return 'search';
      // Achievements
      case '/chatactivity': return 'trending_up';
      case '/resetactivity': return 'refresh';
      case '/stats': return 'analytics';
      case '/achievements': return 'emoji_events';
      case '/leaderboard': return 'leaderboard';
      case '/streaks': return 'local_fire_department';
      default: return 'help';
    }
  };

  return (
    <>
      {/* Информационный блок для Cursor команд */}
      {group === 'cursor' && (
        <div className="mb-2 p-2 bg-muted/50 rounded-lg border">
          <div className="flex items-center gap-2 mb-2">
            <span className="material-icons text-blue-500 text-base">code</span>
            <h3 className="font-semibold text-sm">{t('cursorCommandsInfo')}</h3>
          </div>
          <p className="text-xs text-muted-foreground mb-2">{t('cursorCommandsDesc')}</p>
          
          <div className="space-y-1">
            {commands.filter(c => c.group === 'cursor').map((c) => (
              <div key={c.command} className="flex items-center gap-2 p-1.5 rounded-md hover:bg-background/50 transition-colors">
                <div className="h-6 w-6 grid place-items-center rounded-full bg-blue-100 dark:bg-blue-900/30">
                  <span className="material-icons text-blue-600 dark:text-blue-400 text-xs">{getIcon(c.command)}</span>
                </div>
                <div className="flex-1">
                  <div className="font-medium text-xs">{c.label}</div>
                  <div className="text-xs text-muted-foreground">{c.command}</div>
                </div>
                <div className="text-xs text-muted-foreground">
                  {c.command === '/webapp' ? t('openWebAppDesc') : t('cursorMenuDesc')}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      <div className="p-0">
        <TooltipProvider>
          <div className="flex items-center gap-2 overflow-x-auto no-scrollbar">
            {commands.filter(c => c.group === group).map((c) => (
              <Tooltip key={c.command}>
                <TooltipTrigger
                  onClick={() => void send(c)}
                  disabled={busy === c.command}
                  className="h-8 w-8 grid place-items-center rounded-full border hover:bg-accent disabled:opacity-50"
                  aria-label={c.label}
                >
                  <span className="material-icons text-sm">{getIcon(c.command)}</span>
                </TooltipTrigger>
                <TooltipContent>
                  <div className="text-center">
                    <div className="font-medium">{c.label}</div>
                    <div className="text-xs text-muted-foreground">{c.command}</div>
                  </div>
                </TooltipContent>
              </Tooltip>
            ))}
          </div>
        </TooltipProvider>
      </div>

      {/* GIF Модальные окна */}
      {showColorPicker && (
        <GifColorPicker
          language={language}
          onColorSelect={handleColorSelect}
          onClose={() => setShowColorPicker(false)}
        />
      )}

      {showPositionPicker && (
        <GifPositionPicker
          onPositionSelect={handlePositionSelect}
          onClose={() => setShowPositionPicker(false)}
        />
      )}

      {showSearchDialog && (
        <GifSearchDialog
          language={language}
          onSearch={handleGifSearch}
          onClose={() => setShowSearchDialog(false)}
        />
      )}

      {showTextWizard && (
        <GifTextWizard
          language={language}
          onClose={() => setShowTextWizard(false)}
        />
      )}
    </>
  );
}