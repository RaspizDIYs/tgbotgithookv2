import { useCallback, useState } from 'react';
import './styles.css';
import type { TabKey } from './components/Tabs';
import { Chat } from './components/Chat';
import { ChatInput } from './components/ChatInput';
import { BotCommands } from './components/BotCommands';
import { StatisticsBlocks } from './components/StatisticsBlocks';
import { ThemeToggle } from './components/ui/theme-toggle';
import type { Language } from './locales/translations';
import CursorLinkBuilder from './components/CursorLinkBuilder';
import { Sidebar } from './components/Sidebar';
import { BurgerMenu } from './components/BurgerMenu';
import { LanguageSelector } from './components/LanguageSelector';
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from './components/ui/tooltip';

function DashboardTab({ language }: { language: Language }) {
  return (
    <div className="h-full flex flex-col">
      <div className="p-2 flex-shrink-0">
        <div className="bg-muted/50 rounded-lg p-2 border">
          <div className="flex items-center gap-2 mb-1">
            <span className="material-icons text-base">analytics</span>
            <h4 className="font-medium text-sm">Статистика</h4>
          </div>
          <p className="text-xs text-muted-foreground">
            Просматривайте статистику активности в чате, количество сообщений и другую аналитику
          </p>
        </div>
      </div>
      <div className="flex-1 overflow-y-auto">
        <StatisticsBlocks language={language} />
      </div>
    </div>
  );
}

function AITab({ aiEnabled, aiPending, onToggleAi, onPendingChange, language, onGifReceived }: { aiEnabled: boolean; aiPending: boolean; onToggleAi: () => void; onPendingChange: (p: boolean) => void; language: Language; onGifReceived: (gifUrl: string) => void }) {
  const [chatSendFunction, setChatSendFunction] = useState<((text: string) => void) | null>(null);

  return (
    <div className="flex flex-col h-full">
      <div className="p-2 flex-shrink-0 space-y-2">
        <div className="bg-muted/50 rounded-lg p-2 border">
          <div className="flex items-center gap-2 mb-1">
            <span className="material-icons text-base">smart_toy</span>
            <h4 className="font-medium text-sm">AI ассистент</h4>
          </div>
          <p className="text-xs text-muted-foreground mb-2">
            Общайтесь с AI ассистентом, используйте команды для генерации контента и получения помощи
          </p>
          <div className="text-xs text-muted-foreground">
            <p className="font-medium mb-1">Доступные команды:</p>
            <ul className="space-y-0.5">
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">bar_chart</span>
                <span>Статы - статистика AI ассистента</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">person_search</span>
                <span>Текущий - текущий режим AI</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">sync_alt</span>
                <span>Свитч - переключение режима AI</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">delete_sweep</span>
                <span>Очистить - очистка истории диалога</span>
              </li>
            </ul>
          </div>
          <div className="flex items-center gap-2 mt-2">
            <button className="inline-flex items-center gap-1 rounded-md border px-2 py-1 text-xs" onClick={onToggleAi}>
              <span className="material-icons text-sm">{aiEnabled ? 'stop_circle' : 'play_circle'}</span>
              {aiEnabled ? 'AI стоп' : 'AI старт'}
            </button>
            <div className={`text-xs px-2 py-1 rounded-md border ${!aiEnabled ? 'bg-border text-muted-foreground' : aiPending ? 'bg-emerald-500 text-white' : 'bg-muted text-foreground'}`}>
              {!aiEnabled ? 'Отключен' : aiPending ? 'Активен' : 'Неактивен'}
            </div>
          </div>
        </div>
        <BotCommands group="ai" language={language} onGifReceived={onGifReceived} />
      </div>
      <div className="flex-1 min-h-0">
        <Chat aiEnabled={aiEnabled} onPendingChange={onPendingChange} onMessageSent={setChatSendFunction} />
      </div>
      <ChatInput onSend={chatSendFunction || (() => {})} aiEnabled={aiEnabled} />
    </div>
  );
}

function GifsTab({ aiEnabled, onPendingChange, language, onGifReceived }: { aiEnabled: boolean; onPendingChange: (p: boolean) => void; language: Language; onGifReceived: (gifUrl: string) => void }) {
  return (
    <div className="flex flex-col">
      <div className="p-2 flex-shrink-0 space-y-2">
        <div className="bg-muted/50 rounded-lg p-2 border">
          <div className="flex items-center gap-2 mb-1">
            <span className="material-icons text-base">movie</span>
            <h4 className="font-medium text-sm">GIF</h4>
          </div>
          <p className="text-xs text-muted-foreground mb-2">
            Ищите и создавайте GIF анимации, добавляйте текст и эффекты к изображениям
          </p>
          <div className="text-xs text-muted-foreground">
            <p className="font-medium mb-1">Доступные команды:</p>
            <ul className="space-y-0.5">
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">search</span>
                <span>GIF поиск - поиск GIF по запросу</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">shuffle</span>
                <span>GIF рандом - случайная GIF анимация</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">text_fields</span>
                <span>GIF текст - добавление текста на GIF</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">settings</span>
                <span>GIF настройки - параметры GIF</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">palette</span>
                <span>GIF цвет - настройка цвета текста</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">open_with</span>
                <span>GIF позиция - настройка позиции текста</span>
              </li>
            </ul>
          </div>
        </div>
        <BotCommands group="gif" language={language} onGifReceived={onGifReceived} />
      </div>
      <div className="flex-1">
        <Chat aiEnabled={aiEnabled} onPendingChange={onPendingChange} />
      </div>
    </div>
  );
}

function GamesTab({ aiEnabled, onPendingChange, language, onGifReceived }: { aiEnabled: boolean; onPendingChange: (p: boolean) => void; language: Language; onGifReceived: (gifUrl: string) => void }) {
  return (
    <div className="flex flex-col">
      <div className="p-2 flex-shrink-0 space-y-2">
        <div className="bg-muted/50 rounded-lg p-2 border">
          <div className="flex items-center gap-2 mb-1">
            <span className="material-icons text-base">sports_esports</span>
            <h4 className="font-medium text-sm">Игры</h4>
          </div>
          <p className="text-xs text-muted-foreground mb-2">
            Играйте в мини-игры, участвуйте в викторинах и соревнованиях с другими пользователями
          </p>
          <div className="text-xs text-muted-foreground">
            <p className="font-medium mb-1">Доступные команды:</p>
            <ul className="space-y-0.5">
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">sports_esports</span>
                <span>Игры меню - показать меню игр</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">emoji_emotions</span>
                <span>Мемы - игра на знание мемов</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">videogame_asset</span>
                <span>LoL - игра на знание League of Legends</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">terminal</span>
                <span>Программирование - игра на знание программирования</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">stop_circle</span>
                <span>Стоп игра - остановить текущую игру</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">bug_report</span>
                <span>Тест игры - тестировать игру</span>
              </li>
            </ul>
          </div>
        </div>
        <BotCommands group="games" language={language} onGifReceived={onGifReceived} />
      </div>
      <div className="flex-1">
        <Chat aiEnabled={aiEnabled} onPendingChange={onPendingChange} />
      </div>
    </div>
  );
}

function CursorTab({ language }: { language: Language }) {
  return (
    <div className="p-2 space-y-2">
      <div className="bg-muted/50 rounded-lg p-2 border">
        <div className="flex items-center gap-2 mb-1">
          <span className="material-icons text-base">link</span>
          <h4 className="font-medium text-sm">Cursor</h4>
        </div>
        <p className="text-xs text-muted-foreground">
          Создавайте ссылки для интеграции с Cursor IDE, настраивайте подключения к проектам
        </p>
      </div>
      <CursorLinkBuilder language={language} />
    </div>
  );
}

function GithubTab({ aiEnabled, onPendingChange, language, onGifReceived }: { aiEnabled: boolean; onPendingChange: (p: boolean) => void; language: Language; onGifReceived: (gifUrl: string) => void }) {
  return (
    <div className="flex flex-col">
      <div className="p-2 flex-shrink-0 space-y-2">
        <div className="bg-muted/50 rounded-lg p-2 border">
          <div className="flex items-center gap-2 mb-1">
            <span className="material-icons text-base">hub</span>
            <h4 className="font-medium text-sm">GitHub</h4>
          </div>
          <p className="text-xs text-muted-foreground mb-2">
            Управляйте репозиториями GitHub, просматривайте коммиты, создавайте issues и pull requests
          </p>
          <div className="text-xs text-muted-foreground">
            <p className="font-medium mb-1">Доступные команды:</p>
            <ul className="space-y-0.5">
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">analytics</span>
                <span>Git статистика - статистика репозитория</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">commit</span>
                <span>Коммиты - последние коммиты</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">account_tree</span>
                <span>Ветки - все ветки репозитория</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">merge</span>
                <span>PR - Pull Requests</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">build</span>
                <span>CI/CD - статус сборки</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">people</span>
                <span>Авторы - авторы коммитов</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">search</span>
                <span>Поиск - поиск по репозиторию</span>
              </li>
            </ul>
          </div>
        </div>
        <BotCommands group="github" language={language} onGifReceived={onGifReceived} />
      </div>
      <div className="flex-1">
        <Chat aiEnabled={aiEnabled} onPendingChange={onPendingChange} />
      </div>
    </div>
  );
}

function AchievementsTab({ aiEnabled, onPendingChange, language, onGifReceived }: { aiEnabled: boolean; onPendingChange: (p: boolean) => void; language: Language; onGifReceived: (gifUrl: string) => void }) {
  return (
    <div className="flex flex-col">
      <div className="p-2 flex-shrink-0 space-y-2">
        <div className="bg-muted/50 rounded-lg p-2 border">
          <div className="flex items-center gap-2 mb-1">
            <span className="material-icons text-base">emoji_events</span>
            <h4 className="font-medium text-sm">Ачивки и стата</h4>
          </div>
          <p className="text-xs text-muted-foreground mb-2">
            Отслеживайте свои достижения, получайте награды за активность и просматривайте статистику
          </p>
          <div className="text-xs text-muted-foreground">
            <p className="font-medium mb-1">Доступные команды:</p>
            <ul className="space-y-0.5">
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">trending_up</span>
                <span>Активность чата - статистика сообщений</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">refresh</span>
                <span>Сброс активности - очистка данных</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">analytics</span>
                <span>Статистика - личная статистика</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">emoji_events</span>
                <span>Ачивки - список достижений</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">leaderboard</span>
                <span>Лидеры - таблица лидеров</span>
              </li>
              <li className="flex items-center gap-2">
                <span className="material-icons text-xs">local_fire_department</span>
                <span>Стрики - серии активности</span>
              </li>
            </ul>
          </div>
        </div>
        <BotCommands group="achievements" language={language} onGifReceived={onGifReceived} />
      </div>
      <div className="flex-1">
        <Chat aiEnabled={aiEnabled} onPendingChange={onPendingChange} />
      </div>
    </div>
  );
}

export default function App() {
  const [tab, setTab] = useState<TabKey>('dashboard');
  const [aiEnabled, setAiEnabled] = useState<boolean>(true);
  const [aiPending, setAiPending] = useState<boolean>(false);
  const [sidebarOpen, setSidebarOpen] = useState<boolean>(false);
  const [language, setLanguage] = useState<Language>('ru');

  const onToggleAi = useCallback(() => {
    setAiEnabled((v) => !v);
  }, []);

  const onPendingChange = useCallback((p: boolean) => {
    setAiPending(p);
  }, []);

  const toggleSidebar = useCallback(() => {
    setSidebarOpen(prev => !prev);
  }, []);

  const closeSidebar = useCallback(() => {
    setSidebarOpen(false);
  }, []);

  const handleGifReceived = useCallback((gifUrl: string) => {
    // Обработка полученных GIF (можно добавить логику в будущем)
    console.log('GIF received:', gifUrl);
  }, []);

  const getTabTitle = (tabKey: TabKey): string => {
    const titles: Record<TabKey, Record<Language, string>> = {
      dashboard: { ru: 'Статистика', en: 'Statistics' },
      ai: { ru: 'AI', en: 'AI' },
      gif: { ru: 'GIF', en: 'GIF' },
      games: { ru: 'Игры', en: 'Games' },
      cursor: { ru: 'Cursor', en: 'Cursor' },
      github: { ru: 'GitHub', en: 'GitHub' },
      achievements: { ru: 'Ачивки', en: 'Achievements' }
    };
    return titles[tabKey][language];
  };

  return (
    <div className="min-h-screen bg-background text-foreground">
      <div className="flex min-h-screen">
        {/* Sidebar */}
        <Sidebar 
          isOpen={sidebarOpen} 
          onClose={closeSidebar}
          currentTab={tab}
          onTabChange={setTab}
        />
        
        {/* Main content */}
        <div className="flex-1 lg:ml-0 flex flex-col min-h-screen">
          {/* Header */}
          <div className="flex-shrink-0 bg-background/80 backdrop-blur border-b">
            <div className="flex items-center justify-between p-2">
              <div className="flex items-center gap-2">
                <TooltipProvider>
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <BurgerMenu isOpen={sidebarOpen} onToggle={toggleSidebar} />
                    </TooltipTrigger>
                    <TooltipContent>
                      <p>{language === 'ru' ? 'Меню' : 'Menu'}</p>
                    </TooltipContent>
                  </Tooltip>
                </TooltipProvider>
                <h1 className="text-base font-semibold">{getTabTitle(tab)}</h1>
              </div>
              <div className="flex items-center gap-2">
                <TooltipProvider>
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <LanguageSelector currentLanguage={language} onLanguageChange={setLanguage} />
                    </TooltipTrigger>
                    <TooltipContent>
                      <p>{language === 'ru' ? 'Язык' : 'Language'}</p>
                    </TooltipContent>
                  </Tooltip>
                  <Tooltip>
                    <TooltipTrigger>
                      <ThemeToggle />
                    </TooltipTrigger>
                    <TooltipContent>
                      <p>{language === 'ru' ? 'Тема' : 'Theme'}</p>
                    </TooltipContent>
                  </Tooltip>
                </TooltipProvider>
              </div>
            </div>
          </div>
          
          {/* Tab content */}
          <div className="flex-1 overflow-y-auto">
            {tab === 'dashboard' && <DashboardTab language={language} />}
            {tab === 'ai' && <AITab aiEnabled={aiEnabled} aiPending={aiPending} onToggleAi={onToggleAi} onPendingChange={onPendingChange} language={language} onGifReceived={handleGifReceived} />}
            {tab === 'gif' && <GifsTab aiEnabled={aiEnabled} onPendingChange={onPendingChange} language={language} onGifReceived={handleGifReceived} />}
            {tab === 'games' && <GamesTab aiEnabled={aiEnabled} onPendingChange={onPendingChange} language={language} onGifReceived={handleGifReceived} />}
            {tab === 'cursor' && <CursorTab language={language} />}
            {tab === 'github' && <GithubTab aiEnabled={aiEnabled} onPendingChange={onPendingChange} language={language} onGifReceived={handleGifReceived} />}
            {tab === 'achievements' && <AchievementsTab aiEnabled={aiEnabled} onPendingChange={onPendingChange} language={language} onGifReceived={handleGifReceived} />}
          </div>
        </div>
      </div>
    </div>
  );
}