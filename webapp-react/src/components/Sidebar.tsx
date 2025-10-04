import type { TabKey } from './Tabs';
interface SidebarProps {
  isOpen: boolean;
  onClose: () => void;
  currentTab: TabKey;
  onTabChange: (tab: TabKey) => void;
}

export function Sidebar({ isOpen, onClose, currentTab, onTabChange }: SidebarProps) {
  const tabItems: { key: TabKey; label: string; icon?: string }[] = [
    { key: 'dashboard', label: 'Статистика', icon: 'analytics' },
    { key: 'ai', label: 'AI', icon: 'smart_toy' },
    { key: 'gif', label: 'GIF', icon: 'movie' },
    { key: 'games', label: 'Игры', icon: 'sports_esports' },
    { key: 'cursor', label: 'Cursor', icon: 'link' },
    { key: 'github', label: 'GitHub', icon: 'hub' },
    { key: 'achievements', label: 'Достижения', icon: 'emoji_events' },
  ];

  return (
    <>
      {/* Overlay */}
      {isOpen && (
        <div 
          className="fixed inset-0 bg-black/50 z-40 lg:hidden"
          onClick={onClose}
        />
      )}
      
      {/* Sidebar */}
      <div className={`
        fixed top-0 left-0 h-full w-56 bg-background border-r z-50 transform transition-transform duration-300 ease-in-out
        ${isOpen ? 'translate-x-0' : '-translate-x-full'}
        lg:translate-x-0 lg:static lg:z-auto lg:w-64 lg:min-h-screen
      `}>
        <div className="p-3 min-h-screen flex flex-col">
          <div className="flex items-center gap-2 font-semibold mb-4 text-sm">
            <img src="/cursor-font.png" alt="logo" className="h-5 w-5" />
            GLV2 App
          </div>
          
          <nav className="space-y-1 flex-1">
            {tabItems.map((item) => (
              <button
                key={item.key}
                onClick={() => {
                  onTabChange(item.key);
                  onClose();
                }}
                className={`
                  w-full flex items-center gap-2 px-2 py-1.5 rounded-md text-xs font-medium transition-colors
                  ${currentTab === item.key 
                    ? 'bg-primary text-primary-foreground' 
                    : 'text-foreground hover:bg-accent hover:text-accent-foreground'
                  }
                `}
              >
                {item.icon && <span className="material-icons text-sm">{item.icon}</span>}
                {item.label}
              </button>
            ))}
          </nav>
        </div>
      </div>
    </>
  );
}
