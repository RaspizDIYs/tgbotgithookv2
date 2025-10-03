import { useCallback, useState } from 'react';
import './styles.css';
import { Tabs } from './components/Tabs';
import type { TabKey } from './components/Tabs';
import { Chat } from './components/Chat';
import { BotCommands } from './components/BotCommands';
import { ThemeToggle } from './components/ui/theme-toggle';
import CursorLinkBuilder from './components/CursorLinkBuilder';

function DashboardTab({ aiEnabled, onPendingChange }: { aiEnabled: boolean; onPendingChange: (p: boolean) => void }) {
  return (
    <div className="p-4 space-y-3">
      <h3 className="text-xl font-semibold">Главная</h3>
      <p className="text-sm text-muted-foreground">Короткие карточки/статусы можно перенести сюда.</p>
      <Chat aiEnabled={aiEnabled} onPendingChange={onPendingChange} />
    </div>
  );
}

function AITab({ aiEnabled, aiPending, onToggleAi, onPendingChange }: { aiEnabled: boolean; aiPending: boolean; onToggleAi: () => void; onPendingChange: (p: boolean) => void }) {
  return (
    <div className="p-4 space-y-3">
      <h3 className="text-xl font-semibold">AI ассистент</h3>
      <div className="flex items-center gap-2">
        <button className="inline-flex items-center gap-2 rounded-md border px-3 py-1.5 text-sm" onClick={onToggleAi}>
          <span className="material-icons text-base">{aiEnabled ? 'stop_circle' : 'play_circle'}</span>
          {aiEnabled ? 'AI стоп' : 'AI старт'}
        </button>
        <div className={`text-xs px-2 py-1 rounded-md border ${!aiEnabled ? 'bg-border text-muted-foreground' : aiPending ? 'bg-emerald-500 text-white' : 'bg-muted text-foreground'}`}>
          {!aiEnabled ? 'Отключен' : aiPending ? 'Активен' : 'Неактивен'}
        </div>
      </div>
      <BotCommands group="ai" />
      <Chat aiEnabled={aiEnabled} onPendingChange={onPendingChange} />
    </div>
  );
}

function GifsTab({ aiEnabled, onPendingChange }: { aiEnabled: boolean; onPendingChange: (p: boolean) => void }) {
  return (
    <div className="p-4 space-y-3">
      <h3 className="text-xl font-semibold">GIF</h3>
      <BotCommands group="gif" />
      <Chat aiEnabled={aiEnabled} onPendingChange={onPendingChange} />
    </div>
  );
}

function GamesTab({ aiEnabled, onPendingChange }: { aiEnabled: boolean; onPendingChange: (p: boolean) => void }) {
  return (
    <div className="p-4 space-y-3">
      <h3 className="text-xl font-semibold">Игры</h3>
      <BotCommands group="games" />
      <Chat aiEnabled={aiEnabled} onPendingChange={onPendingChange} />
    </div>
  );
}

function CursorTab() {
  return (
    <div className="p-4 space-y-3">
      <h3 className="text-xl font-semibold">Cursor</h3>
      <CursorLinkBuilder />
    </div>
  );
}

function GithubTab({ aiEnabled, onPendingChange }: { aiEnabled: boolean; onPendingChange: (p: boolean) => void }) {
  return (
    <div className="p-4 space-y-3">
      <h3 className="text-xl font-semibold">GitHub</h3>
      <BotCommands group="github" />
      <Chat aiEnabled={aiEnabled} onPendingChange={onPendingChange} />
    </div>
  );
}

function AchievementsTab({ aiEnabled, onPendingChange }: { aiEnabled: boolean; onPendingChange: (p: boolean) => void }) {
  return (
    <div className="p-4 space-y-3">
      <h3 className="text-xl font-semibold">Ачивки и стата</h3>
      <BotCommands group="achievements" />
      <Chat aiEnabled={aiEnabled} onPendingChange={onPendingChange} />
    </div>
  );
}

export default function App() {
  const [tab, setTab] = useState<TabKey>('dashboard');
  const [aiEnabled, setAiEnabled] = useState<boolean>(true);
  const [aiPending, setAiPending] = useState<boolean>(false);

  const onToggleAi = useCallback(() => {
    setAiEnabled((v) => !v);
  }, []);

  const onPendingChange = useCallback((p: boolean) => {
    setAiPending(p);
  }, []);

  return (
    <div className="min-h-screen bg-background text-foreground">
      <div className="flex items-center justify-between p-3 border-b sticky top-0 z-20 bg-background/80 backdrop-blur">
        <div className="flex items-center gap-2 font-semibold">
          <img src="/cursor-font.png" alt="logo" className="h-6 w-6" />
          GLV2 App
        </div>
        <ThemeToggle />
      </div>
      <Tabs value={tab} onChange={setTab} />
      {tab === 'dashboard' && <DashboardTab aiEnabled={aiEnabled} onPendingChange={onPendingChange} />}
      {tab === 'ai' && <AITab aiEnabled={aiEnabled} aiPending={aiPending} onToggleAi={onToggleAi} onPendingChange={onPendingChange} />}
      {tab === 'gif' && <GifsTab aiEnabled={aiEnabled} onPendingChange={onPendingChange} />}
      {tab === 'games' && <GamesTab aiEnabled={aiEnabled} onPendingChange={onPendingChange} />}
      {tab === 'cursor' && <CursorTab />}
      {tab === 'github' && <GithubTab aiEnabled={aiEnabled} onPendingChange={onPendingChange} />}
      {tab === 'achievements' && <AchievementsTab aiEnabled={aiEnabled} onPendingChange={onPendingChange} />}
    </div>
  );
}

