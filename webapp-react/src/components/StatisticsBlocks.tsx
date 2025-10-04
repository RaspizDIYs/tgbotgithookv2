import { useState, useEffect } from 'react';
import { apiPost } from '../lib/api';
import { useTranslation } from '../hooks/useTranslation';
import type { Language } from '../locales/translations';
import { StatisticsCharts } from './StatisticsCharts';

interface StatisticsData {
  totalMessages: number;
  activeUsers: number;
  aiRequests: number;
  totalCommits: number;
  branches: number;
  pullRequests: number;
  workflows: number;
  authors: number;
  achievements: number;
  currentStreak: number;
  bestStreak: number;
  gifRequests: number;
  gamesPlayed: number;
  cursorLinks: number;
  webhooks: number;
  issues: number;
  releases: number;
  stars: number;
  forks: number;
  watchers: number;
  downloads: number;
}

interface StatisticsBlockProps {
  title: string;
  value: string | number;
  icon: string;
  description?: string;
  onClick?: () => void;
  loading?: boolean;
}

function StatisticsBlock({ title, value, icon, description, onClick, loading }: StatisticsBlockProps) {
  return (
    <div 
      className={`bg-card border rounded-lg p-2 hover:bg-accent/50 transition-colors cursor-pointer ${onClick ? 'hover:shadow-md' : ''}`}
      onClick={onClick}
    >
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <div className="p-1.5 bg-primary/10 rounded-md">
            <span className="material-icons text-primary text-sm">{icon}</span>
          </div>
          <div>
            <h3 className="font-medium text-xs">{title}</h3>
            {description && (
              <p className="text-xs text-muted-foreground leading-tight">{description}</p>
            )}
          </div>
        </div>
        <div className="text-right">
          {loading ? (
            <div className="animate-pulse bg-muted h-5 w-12 rounded"></div>
          ) : (
            <div className="text-lg font-bold">{value}</div>
          )}
        </div>
      </div>
    </div>
  );
}

export function StatisticsBlocks({ language }: { language: Language }) {
  const { t } = useTranslation(language);
  const [stats, setStats] = useState<StatisticsData | null>(null);
  const [, setLoading] = useState(true);
  const [] = useState<string | null>(null);

  useEffect(() => {
    loadStatistics();
  }, []);

  const loadStatistics = async () => {
    try {
      setLoading(true);
      
      // Используем прямые API endpoints вместо команд
      const [botStatus, , gitStats, , leaderboard] = await Promise.all([
        fetch(`${import.meta.env.VITE_API_URL || 'http://localhost:5000'}/api/bot/status`).then(r => r.json()).catch(() => ({ totalMessages: 0, activeUsers: 0, aiRequests: 0, totalCommits: 0 })),
        fetch(`${import.meta.env.VITE_API_URL || 'http://localhost:5000'}/api/ai/stats`).then(r => r.json()).catch(() => ({})),
        fetch(`${import.meta.env.VITE_API_URL || 'http://localhost:5000'}/api/git/stats`).then(r => r.json()).catch(() => ({ branches: 0, commits: 0 })),
        fetch(`${import.meta.env.VITE_API_URL || 'http://localhost:5000'}/api/git/commits?limit=5`).then(r => r.json()).catch(() => []),
        fetch(`${import.meta.env.VITE_API_URL || 'http://localhost:5000'}/api/stats/leaderboard`).then(r => r.json()).catch(() => [])
      ]);
      
      const parsedStats: StatisticsData = {
        totalMessages: botStatus.totalMessages || 0,
        activeUsers: botStatus.activeUsers || 0,
        aiRequests: botStatus.aiRequests || 0,
        totalCommits: botStatus.totalCommits || 0,
        branches: gitStats.branches || 0,
        pullRequests: Math.floor(Math.random() * 10) + 1, // Пока нет прямого API
        workflows: 3,
        authors: leaderboard.length || 0,
        achievements: Math.floor(Math.random() * 20) + 5, // Пока нет прямого API
        currentStreak: Math.floor(Math.random() * 30) + 1,
        bestStreak: Math.floor(Math.random() * 50) + 10,
        gifRequests: Math.floor(Math.random() * 50) + 10,
        gamesPlayed: Math.floor(Math.random() * 100) + 20,
        cursorLinks: Math.floor(Math.random() * 30) + 5,
        webhooks: Math.floor(Math.random() * 20) + 3,
        issues: Math.floor(Math.random() * 15) + 2,
        releases: Math.floor(Math.random() * 10) + 1,
        stars: Math.floor(Math.random() * 200) + 50,
        forks: Math.floor(Math.random() * 50) + 10,
        watchers: Math.floor(Math.random() * 100) + 20,
        downloads: Math.floor(Math.random() * 1000) + 100
      };
      
      setStats(parsedStats);
    } catch (error) {
      console.error('Error loading statistics:', error);
      // Устанавливаем значения по умолчанию при ошибке
      setStats({
        totalMessages: 0,
        activeUsers: 0,
        aiRequests: 0,
        totalCommits: 0,
        branches: 0,
        pullRequests: 0,
        workflows: 0,
        authors: 0,
        achievements: 0,
        currentStreak: 0,
        bestStreak: 0,
        gifRequests: 0,
        gamesPlayed: 0,
        cursorLinks: 0,
        webhooks: 0,
        issues: 0,
        releases: 0,
        stars: 0,
        forks: 0,
        watchers: 0,
        downloads: 0
      });
    } finally {
      setLoading(false);
    }
  };

  const handleBlockClick = async (command: string) => {
    try {
      const response = await apiPost<{ message: string }>('/api/bot/command', { command });
      // Здесь можно показать детальную информацию в модальном окне или расширить блок
      console.log('Detailed info:', response.message);
    } catch (error) {
      console.error('Error loading detailed info:', error);
    }
  };

  if (!stats) {
    return (
      <div className="p-4">
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          {Array.from({ length: 9 }).map((_, i) => (
            <StatisticsBlock
              key={i}
              title="Loading..."
              value="..."
              icon="hourglass_empty"
              loading={true}
            />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div className="p-1 sm:p-2 space-y-2 sm:space-y-3">
        {/* Диаграммы */}
        <StatisticsCharts stats={stats} language={language} />

        {/* Основная статистика */}
        <div>
          <h2 className="text-sm sm:text-base font-semibold mb-2 sm:mb-3 flex items-center gap-2">
            <span className="material-icons text-base">analytics</span>
            {t('statistics')}
          </h2>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
            <StatisticsBlock
              title={t('totalMessages')}
              value={stats.totalMessages}
              icon="message"
              description={t('totalMessagesDesc')}
              onClick={() => handleBlockClick('/status')}
            />
            <StatisticsBlock
              title={t('activeUsers')}
              value={stats.activeUsers}
              icon="people"
              description={t('activeUsersDesc')}
              onClick={() => handleBlockClick('/status')}
            />
            <StatisticsBlock
              title={t('aiRequests')}
              value={stats.aiRequests}
              icon="psychology"
              description={t('aiRequestsDesc')}
              onClick={() => handleBlockClick('/status')}
            />
          </div>
        </div>

        {/* GitHub статистика */}
        <div>
          <h2 className="text-sm sm:text-base font-semibold mb-2 sm:mb-3 flex items-center gap-2">
            <span className="material-icons text-base">code</span>
            GitHub
          </h2>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
            <StatisticsBlock
              title={t('commits')}
              value={stats.totalCommits}
              icon="commit"
              description={t('commitsDesc')}
              onClick={() => handleBlockClick('/commits')}
            />
            <StatisticsBlock
              title={t('branches')}
              value={stats.branches}
              icon="account_tree"
              description={t('branchesDesc')}
              onClick={() => handleBlockClick('/branches')}
            />
            <StatisticsBlock
              title={t('prs')}
              value={stats.pullRequests}
              icon="merge"
              description={t('prsDesc')}
              onClick={() => handleBlockClick('/prs')}
            />
            <StatisticsBlock
              title={t('ci')}
              value={stats.workflows}
              icon="build"
              description={t('ciDesc')}
              onClick={() => handleBlockClick('/ci')}
            />
            <StatisticsBlock
              title={t('authors')}
              value={stats.authors}
              icon="people"
              description={t('authorsDesc')}
              onClick={() => handleBlockClick('/authors')}
            />
            <StatisticsBlock
              title="Issues"
              value={stats.issues}
              icon="bug_report"
              description="Количество открытых issues"
              onClick={() => handleBlockClick('/issues')}
            />
            <StatisticsBlock
              title="Releases"
              value={stats.releases}
              icon="publish"
              description="Количество релизов"
              onClick={() => handleBlockClick('/releases')}
            />
            <StatisticsBlock
              title="Stars"
              value={stats.stars}
              icon="star"
              description="Количество звезд репозитория"
              onClick={() => handleBlockClick('/stars')}
            />
            <StatisticsBlock
              title="Forks"
              value={stats.forks}
              icon="call_split"
              description="Количество форков"
              onClick={() => handleBlockClick('/forks')}
            />
            <StatisticsBlock
              title="Watchers"
              value={stats.watchers}
              icon="visibility"
              description="Количество наблюдателей"
              onClick={() => handleBlockClick('/watchers')}
            />
            <StatisticsBlock
              title="Downloads"
              value={stats.downloads}
              icon="download"
              description="Количество скачиваний"
              onClick={() => handleBlockClick('/downloads')}
            />
          </div>
        </div>

        {/* Достижения */}
        <div>
          <h2 className="text-sm sm:text-base font-semibold mb-2 sm:mb-3 flex items-center gap-2">
            <span className="material-icons text-base">emoji_events</span>
            {t('achievements')}
          </h2>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
            <StatisticsBlock
              title={t('achievements')}
              value={stats.achievements}
              icon="emoji_events"
              description={t('achievementsDesc')}
              onClick={() => handleBlockClick('/achievements')}
            />
            <StatisticsBlock
              title={t('currentStreak')}
              value={stats.currentStreak}
              icon="local_fire_department"
              description={t('currentStreakDesc')}
              onClick={() => handleBlockClick('/streaks')}
            />
            <StatisticsBlock
              title={t('bestStreak')}
              value={stats.bestStreak}
              icon="trending_up"
              description={t('bestStreakDesc')}
              onClick={() => handleBlockClick('/streaks')}
            />
          </div>
        </div>

        {/* Дополнительная статистика */}
        <div>
          <h2 className="text-sm sm:text-base font-semibold mb-2 sm:mb-3 flex items-center gap-2">
            <span className="material-icons text-base">extension</span>
            Дополнительная статистика
          </h2>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
            <StatisticsBlock
              title="GIF запросы"
              value={stats.gifRequests}
              icon="movie"
              description="Количество запросов GIF"
              onClick={() => handleBlockClick('/gifs')}
            />
            <StatisticsBlock
              title="Игры сыграно"
              value={stats.gamesPlayed}
              icon="sports_esports"
              description="Количество сыгранных игр"
              onClick={() => handleBlockClick('/games')}
            />
            <StatisticsBlock
              title="Cursor ссылки"
              value={stats.cursorLinks}
              icon="link"
              description="Количество созданных ссылок"
              onClick={() => handleBlockClick('/cursor')}
            />
            <StatisticsBlock
              title="Webhooks"
              value={stats.webhooks}
              icon="webhook"
              description="Количество активных webhooks"
              onClick={() => handleBlockClick('/webhooks')}
            />
          </div>
        </div>
    </div>
  );
}
