import { 
  BarChart, 
  Bar, 
  XAxis, 
  YAxis, 
  CartesianGrid, 
  Tooltip, 
  ResponsiveContainer,
  PieChart,
  Pie,
  Cell,
  LineChart,
  Line,
  Area,
  AreaChart,
  ScatterChart,
  Scatter,
  RadialBarChart,
  RadialBar,
  ComposedChart
} from 'recharts';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from './ui/card';
import { useTranslation } from '../hooks/useTranslation';
import type { Language } from '../locales/translations';

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

interface StatisticsChartsProps {
  stats: StatisticsData;
  language: Language;
}

const COLORS = ['#0088FE', '#00C49F', '#FFBB28', '#FF8042', '#8884D8', '#82CA9D', '#FF6B6B', '#4ECDC4', '#45B7D1', '#96CEB4'];

export function StatisticsCharts({ stats, language }: StatisticsChartsProps) {
  const { t } = useTranslation(language);

  return (
    <div className="space-y-2 sm:space-y-4">
      {/* 1. Общая статистика бота */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="flex items-center gap-2 text-sm">
            <span className="material-icons text-blue-600 text-base">smart_toy</span>
            {t('botStatistics')}
          </CardTitle>
          <CardDescription className="text-xs">
            {t('botStatisticsDesc')}
          </CardDescription>
        </CardHeader>
        <CardContent className="pt-2">
          <div className="grid grid-cols-1 gap-2 sm:gap-3">
            <div>
              <h4 className="font-medium mb-2 text-xs">{t('messageActivity')}</h4>
              <ResponsiveContainer width="100%" height={120}>
                <BarChart data={[
                  { name: t('totalMessages'), value: stats.totalMessages, fill: '#0088FE' },
                  { name: t('activeUsers'), value: stats.activeUsers, fill: '#00C49F' },
                  { name: t('aiRequests'), value: stats.aiRequests, fill: '#FFBB28' }
                ]}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="name" tick={{ fontSize: 12 }} />
                  <YAxis tick={{ fontSize: 12 }} />
                  <Tooltip contentStyle={{ backgroundColor: 'hsl(var(--card))', border: '1px solid hsl(var(--border))', borderRadius: '6px' }} />
                  <Bar dataKey="value" radius={[4, 4, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </div>
            <div>
              <h4 className="font-medium mb-2 text-xs">{t('userDistribution')}</h4>
              <ResponsiveContainer width="100%" height={120}>
                <PieChart>
                  <Pie
                    data={[
                      { name: t('activeUsers'), value: stats.activeUsers },
                      { name: t('aiRequests'), value: stats.aiRequests },
                      { name: t('totalMessages'), value: Math.max(0, stats.totalMessages - stats.activeUsers - stats.aiRequests) }
                    ].filter(item => item.value > 0)}
                    cx="50%"
                    cy="50%"
                    outerRadius={80}
                    dataKey="value"
                    label={({ name, percent }: any) => `${name} ${(percent * 100).toFixed(0)}%`}
                  >
                    {[0, 1, 2].map((_, index) => (
                      <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
                    ))}
                  </Pie>
                  <Tooltip contentStyle={{ backgroundColor: 'hsl(var(--card))', border: '1px solid hsl(var(--border))', borderRadius: '6px' }} />
                </PieChart>
              </ResponsiveContainer>
            </div>
          </div>
        </CardContent>
      </Card>

      {/* 2. GitHub статистика */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="flex items-center gap-2 text-sm">
            <span className="material-icons text-gray-600 text-base">code</span>
            GitHub {t('statistics')}
          </CardTitle>
          <CardDescription className="text-xs">
            {t('githubStatisticsDesc')}
          </CardDescription>
        </CardHeader>
        <CardContent className="pt-2">
          <div className="grid grid-cols-1 gap-2 sm:gap-3">
            <div>
              <h4 className="font-medium mb-2 text-xs">{t('repositoryActivity')}</h4>
              <ResponsiveContainer width="100%" height={120}>
                <ComposedChart data={[
                  { name: t('commits'), commits: stats.totalCommits, branches: stats.branches },
                  { name: t('prs'), commits: stats.pullRequests, branches: stats.authors },
                  { name: 'Workflows', commits: stats.workflows, branches: 1 }
                ]}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="name" tick={{ fontSize: 12 }} />
                  <YAxis tick={{ fontSize: 12 }} />
                  <Tooltip contentStyle={{ backgroundColor: 'hsl(var(--card))', border: '1px solid hsl(var(--border))', borderRadius: '6px' }} />
                  <Bar dataKey="commits" fill="#8884d8" radius={[4, 4, 0, 0]} />
                  <Line type="monotone" dataKey="branches" stroke="#82ca9d" strokeWidth={2} />
                </ComposedChart>
              </ResponsiveContainer>
            </div>
            <div>
              <h4 className="font-medium mb-2 text-xs">{t('contributors')}</h4>
              <ResponsiveContainer width="100%" height={120}>
                <RadialBarChart cx="50%" cy="50%" innerRadius="20%" outerRadius="80%" data={[
                  { name: t('authors'), value: stats.authors, fill: '#8884d8' },
                  { name: t('branches'), value: stats.branches, fill: '#82ca9d' },
                  { name: t('prs'), value: stats.pullRequests, fill: '#ffc658' }
                ]}>
                  <RadialBar dataKey="value" cornerRadius={10} fill="#8884d8" />
                  <Tooltip contentStyle={{ backgroundColor: 'hsl(var(--card))', border: '1px solid hsl(var(--border))', borderRadius: '6px' }} />
                </RadialBarChart>
              </ResponsiveContainer>
            </div>
          </div>
        </CardContent>
      </Card>

      {/* 3. Достижения и серии */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="flex items-center gap-2 text-sm">
            <span className="material-icons text-yellow-600 text-base">emoji_events</span>
            {t('achievements')} & {t('streaks')}
          </CardTitle>
          <CardDescription className="text-xs">
            {t('achievementsStreaksDesc')}
          </CardDescription>
        </CardHeader>
        <CardContent className="pt-2">
          <div className="grid grid-cols-1 gap-2 sm:gap-3">
            <div>
              <h4 className="font-medium mb-2 text-xs">{t('achievementProgress')}</h4>
              <ResponsiveContainer width="100%" height={120}>
                <AreaChart data={[
                  { period: t('achievements'), value: stats.achievements },
                  { period: t('currentStreak'), value: stats.currentStreak },
                  { period: t('bestStreak'), value: stats.bestStreak }
                ]}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="period" tick={{ fontSize: 12 }} />
                  <YAxis tick={{ fontSize: 12 }} />
                  <Tooltip contentStyle={{ backgroundColor: 'hsl(var(--card))', border: '1px solid hsl(var(--border))', borderRadius: '6px' }} />
                  <Area type="monotone" dataKey="value" stroke="#ffc658" fill="#ffc658" fillOpacity={0.6} />
                </AreaChart>
              </ResponsiveContainer>
            </div>
            <div>
              <h4 className="font-medium mb-2 text-xs">{t('streakComparison')}</h4>
              <ResponsiveContainer width="100%" height={120}>
                <ScatterChart data={[
                  { x: stats.currentStreak, y: stats.bestStreak, z: stats.achievements, name: 'User' }
                ]}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="x" name={t('currentStreak')} tick={{ fontSize: 12 }} />
                  <YAxis dataKey="y" name={t('bestStreak')} tick={{ fontSize: 12 }} />
                  <Tooltip contentStyle={{ backgroundColor: 'hsl(var(--card))', border: '1px solid hsl(var(--border))', borderRadius: '6px' }} />
                  <Scatter dataKey="z" fill="#8884d8" />
                </ScatterChart>
              </ResponsiveContainer>
            </div>
          </div>
        </CardContent>
      </Card>

      {/* 4. AI Ассистент статистика */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="flex items-center gap-2 text-sm">
            <span className="material-icons text-green-600 text-base">psychology</span>
            AI {t('assistant')} {t('statistics')}
          </CardTitle>
          <CardDescription className="text-xs">
            {t('aiStatisticsDesc')}
          </CardDescription>
        </CardHeader>
        <CardContent className="pt-2">
          <div className="grid grid-cols-1 gap-2 sm:gap-3">
            <div>
              <h4 className="font-medium mb-2 text-xs">{t('aiUsage')}</h4>
              <ResponsiveContainer width="100%" height={120}>
                <LineChart data={[
                  { time: '00:00', requests: Math.floor(stats.aiRequests * 0.1) },
                  { time: '06:00', requests: Math.floor(stats.aiRequests * 0.3) },
                  { time: '12:00', requests: Math.floor(stats.aiRequests * 0.8) },
                  { time: '18:00', requests: stats.aiRequests },
                  { time: '24:00', requests: Math.floor(stats.aiRequests * 0.2) }
                ]}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="time" tick={{ fontSize: 12 }} />
                  <YAxis tick={{ fontSize: 12 }} />
                  <Tooltip contentStyle={{ backgroundColor: 'hsl(var(--card))', border: '1px solid hsl(var(--border))', borderRadius: '6px' }} />
                  <Line type="monotone" dataKey="requests" stroke="#4ECDC4" strokeWidth={3} dot={{ fill: '#4ECDC4', strokeWidth: 2, r: 6 }} />
                </LineChart>
              </ResponsiveContainer>
            </div>
            <div>
              <h4 className="font-medium mb-2 text-xs">{t('aiEfficiency')}</h4>
              <ResponsiveContainer width="100%" height={120}>
                <PieChart>
                  <Pie
                    data={[
                      { name: t('successfulRequests'), value: Math.floor(stats.aiRequests * 0.85) },
                      { name: t('failedRequests'), value: Math.floor(stats.aiRequests * 0.15) }
                    ]}
                    cx="50%"
                    cy="50%"
                    innerRadius={60}
                    outerRadius={100}
                    dataKey="value"
                    label={({ name, percent }: any) => `${name} ${(percent * 100).toFixed(0)}%`}
                  >
                    <Cell fill="#4ECDC4" />
                    <Cell fill="#FF6B6B" />
                  </Pie>
                  <Tooltip contentStyle={{ backgroundColor: 'hsl(var(--card))', border: '1px solid hsl(var(--border))', borderRadius: '6px' }} />
                </PieChart>
              </ResponsiveContainer>
            </div>
          </div>
        </CardContent>
      </Card>

      {/* 5. Общая активность по времени */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="flex items-center gap-2 text-sm">
            <span className="material-icons text-purple-600 text-base">timeline</span>
            {t('activityTimeline')}
          </CardTitle>
          <CardDescription className="text-xs">
            {t('activityTimelineDesc')}
          </CardDescription>
        </CardHeader>
        <CardContent className="pt-2">
          <ResponsiveContainer width="100%" height={150}>
            <AreaChart data={[
              { period: 'Jan', messages: Math.floor(stats.totalMessages * 0.1), commits: Math.floor(stats.totalCommits * 0.1), ai: Math.floor(stats.aiRequests * 0.1) },
              { period: 'Feb', messages: Math.floor(stats.totalMessages * 0.2), commits: Math.floor(stats.totalCommits * 0.2), ai: Math.floor(stats.aiRequests * 0.2) },
              { period: 'Mar', messages: Math.floor(stats.totalMessages * 0.4), commits: Math.floor(stats.totalCommits * 0.3), ai: Math.floor(stats.aiRequests * 0.3) },
              { period: 'Apr', messages: Math.floor(stats.totalMessages * 0.6), commits: Math.floor(stats.totalCommits * 0.5), ai: Math.floor(stats.aiRequests * 0.4) },
              { period: 'May', messages: Math.floor(stats.totalMessages * 0.8), commits: Math.floor(stats.totalCommits * 0.7), ai: Math.floor(stats.aiRequests * 0.6) },
              { period: 'Jun', messages: stats.totalMessages, commits: stats.totalCommits, ai: stats.aiRequests }
            ]}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis dataKey="period" tick={{ fontSize: 12 }} />
              <YAxis tick={{ fontSize: 12 }} />
              <Tooltip contentStyle={{ backgroundColor: 'hsl(var(--card))', border: '1px solid hsl(var(--border))', borderRadius: '6px' }} />
              <Area type="monotone" dataKey="messages" stackId="1" stroke="#8884d8" fill="#8884d8" fillOpacity={0.6} />
              <Area type="monotone" dataKey="commits" stackId="1" stroke="#82ca9d" fill="#82ca9d" fillOpacity={0.6} />
              <Area type="monotone" dataKey="ai" stackId="1" stroke="#ffc658" fill="#ffc658" fillOpacity={0.6} />
            </AreaChart>
          </ResponsiveContainer>
        </CardContent>
      </Card>

      {/* 6. Пирог диаграмма - Распределение активности */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="flex items-center gap-2 text-sm">
            <span className="material-icons text-orange-600 text-base">pie_chart</span>
            Распределение активности (Пирог)
          </CardTitle>
          <CardDescription className="text-xs">
            Показывает распределение различных типов активности в процентах
          </CardDescription>
        </CardHeader>
        <CardContent className="pt-2">
          <div className="grid grid-cols-1 gap-2 sm:gap-3">
            <div>
              <h4 className="font-medium mb-2 text-xs">Основная активность</h4>
              <ResponsiveContainer width="100%" height={120}>
                <PieChart>
                  <Pie
                    data={[
                      { name: 'Сообщения', value: stats.totalMessages, fill: '#0088FE' },
                      { name: 'AI запросы', value: stats.aiRequests, fill: '#00C49F' },
                      { name: 'GIF запросы', value: stats.gifRequests, fill: '#FFBB28' },
                      { name: 'Игры', value: stats.gamesPlayed, fill: '#FF8042' }
                    ].filter(item => item.value > 0)}
                    cx="50%"
                    cy="50%"
                    outerRadius={80}
                    dataKey="value"
                    label={({ name, percent }: any) => `${name} ${(percent * 100).toFixed(0)}%`}
                  >
                    {[0, 1, 2, 3].map((_, index) => (
                      <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
                    ))}
                  </Pie>
                  <Tooltip contentStyle={{ backgroundColor: 'hsl(var(--card))', border: '1px solid hsl(var(--border))', borderRadius: '6px' }} />
                </PieChart>
              </ResponsiveContainer>
            </div>
            <div>
              <h4 className="font-medium mb-2 text-xs">GitHub активность</h4>
              <ResponsiveContainer width="100%" height={120}>
                <PieChart>
                  <Pie
                    data={[
                      { name: 'Коммиты', value: stats.totalCommits, fill: '#8884D8' },
                      { name: 'PR', value: stats.pullRequests, fill: '#82CA9D' },
                      { name: 'Issues', value: stats.issues, fill: '#FF6B6B' },
                      { name: 'Releases', value: stats.releases, fill: '#4ECDC4' }
                    ].filter(item => item.value > 0)}
                    cx="50%"
                    cy="50%"
                    outerRadius={80}
                    dataKey="value"
                    label={({ name, percent }: any) => `${name} ${(percent * 100).toFixed(0)}%`}
                  >
                    {[0, 1, 2, 3].map((_, index) => (
                      <Cell key={`cell-${index}`} fill={COLORS[(index + 4) % COLORS.length]} />
                    ))}
                  </Pie>
                  <Tooltip contentStyle={{ backgroundColor: 'hsl(var(--card))', border: '1px solid hsl(var(--border))', borderRadius: '6px' }} />
                </PieChart>
              </ResponsiveContainer>
            </div>
          </div>
        </CardContent>
      </Card>

      {/* 7. Пончик диаграмма - Популярность репозитория */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="flex items-center gap-2 text-sm">
            <span className="material-icons text-pink-600 text-base">donut_large</span>
            Популярность репозитория (Пончик)
          </CardTitle>
          <CardDescription className="text-xs">
            Показывает популярность репозитория через различные метрики
          </CardDescription>
        </CardHeader>
        <CardContent className="pt-2">
          <div className="grid grid-cols-1 gap-2 sm:gap-3">
            <div>
              <h4 className="font-medium mb-2 text-xs">Взаимодействие с репозиторием</h4>
              <ResponsiveContainer width="100%" height={120}>
                <PieChart>
                  <Pie
                    data={[
                      { name: 'Stars', value: stats.stars, fill: '#FFD700' },
                      { name: 'Forks', value: stats.forks, fill: '#FF6B6B' },
                      { name: 'Watchers', value: stats.watchers, fill: '#4ECDC4' },
                      { name: 'Downloads', value: Math.floor(stats.downloads / 10), fill: '#45B7D1' }
                    ].filter(item => item.value > 0)}
                    cx="50%"
                    cy="50%"
                    innerRadius={60}
                    outerRadius={100}
                    dataKey="value"
                    label={({ name, percent }: any) => `${name} ${(percent * 100).toFixed(0)}%`}
                  >
                    {[0, 1, 2, 3].map((_, index) => (
                      <Cell key={`cell-${index}`} fill={COLORS[(index + 6) % COLORS.length]} />
                    ))}
                  </Pie>
                  <Tooltip contentStyle={{ backgroundColor: 'hsl(var(--card))', border: '1px solid hsl(var(--border))', borderRadius: '6px' }} />
                </PieChart>
              </ResponsiveContainer>
            </div>
            <div>
              <h4 className="font-medium mb-2 text-xs">Достижения и серии</h4>
              <ResponsiveContainer width="100%" height={120}>
                <PieChart>
                  <Pie
                    data={[
                      { name: 'Достижения', value: stats.achievements, fill: '#FFD700' },
                      { name: 'Текущая серия', value: stats.currentStreak, fill: '#FF6B6B' },
                      { name: 'Лучшая серия', value: stats.bestStreak, fill: '#4ECDC4' },
                      { name: 'Webhooks', value: stats.webhooks, fill: '#45B7D1' }
                    ].filter(item => item.value > 0)}
                    cx="50%"
                    cy="50%"
                    innerRadius={60}
                    outerRadius={100}
                    dataKey="value"
                    label={({ name, percent }: any) => `${name} ${(percent * 100).toFixed(0)}%`}
                  >
                    {[0, 1, 2, 3].map((_, index) => (
                      <Cell key={`cell-${index}`} fill={COLORS[(index + 8) % COLORS.length]} />
                    ))}
                  </Pie>
                  <Tooltip contentStyle={{ backgroundColor: 'hsl(var(--card))', border: '1px solid hsl(var(--border))', borderRadius: '6px' }} />
                </PieChart>
              </ResponsiveContainer>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
