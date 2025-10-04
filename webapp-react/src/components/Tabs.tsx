//

type TabKey = 'dashboard' | 'ai' | 'gif' | 'games' | 'cursor' | 'github' | 'achievements';
import { Button } from './ui/button';

export function Tabs({ value, onChange }: { value: TabKey; onChange: (v: TabKey) => void }) {
  const items: { key: TabKey; label: string; icon?: string }[] = [
    { key: 'dashboard', label: 'Статистика', icon: 'analytics' },
    { key: 'ai', label: 'AI', icon: 'smart_toy' },
    { key: 'gif', label: 'GIF', icon: 'movie' },
    { key: 'games', label: 'Игры', icon: 'sports_esports' },
    { key: 'cursor', label: 'Cursor', icon: 'link' },
    { key: 'github', label: 'GitHub', icon: 'hub' },
    { key: 'achievements', label: 'Ачивки', icon: 'emoji_events' },
  ];
  return (
    <div className="sticky top-0 z-10 border-b bg-background/80 backdrop-blur">
      <div className="flex flex-wrap gap-2 p-3 overflow-visible">
        {items.map((it) => (
          <Button
            key={it.key}
            variant={value === it.key ? 'default' : 'outline'}
            onClick={() => onChange(it.key)}
            className=""
          >
            {it.icon ? <span className="material-icons mr-1 align-middle text-base">{it.icon}</span> : null}
            {it.label}
          </Button>
        ))}
      </div>
    </div>
  );
}

export type { TabKey };


