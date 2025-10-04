# Интеграция фронтенда с бекендом

## Настройка API URL

Создайте файл `.env` в папке `webapp-react` со следующим содержимым:

```env
# API Configuration
VITE_API_URL=http://localhost:5000

# For production, use your deployed backend URL:
# VITE_API_URL=https://tgbotgithookv2.onrender.com
```

## Проверенная интеграция

### ✅ AI Ассистент
- **Фронтенд**: Кнопки в табе AI (`/glaistart`, `/glaistop`, `/glaistats`, `/glaicurrent`, `/glaiswitch`, `/glaiclear`)
- **Бекенд**: Реализованы методы `HandleAi*CommandForWebAppAsync` в `TelegramBotService.cs`
- **API**: `/api/ai/chat` для отправки сообщений AI

### ✅ GIF функции
- **Фронтенд**: Кнопки в табе GIF (`/gifsearch`, `/gifrandom`, `/giftext`, `/gifcolor`, `/gifposition`)
- **Бекенд**: Реализованы методы `HandleGif*ForWebAppAsync` в `TelegramBotService.cs`
- **Интеграция**: GIF отображаются в чате через `onGifReceived` callback

### ✅ Игры
- **Фронтенд**: Кнопки в табе Games (`/game`, `/gamememe`, `/gamelol`, `/gameprogramming`, `/gamestop`, `/gametest`)
- **Бекенд**: Реализованы методы `HandleGame*CommandForWebAppAsync` в `TelegramBotService.cs`
- **Интеграция**: Игры запускаются в компоненте чата

### ✅ Статистика
- **Фронтенд**: Компонент `StatisticsBlocks` использует прямые API endpoints
- **Бекенд**: Endpoints `/api/bot/status`, `/api/ai/stats`, `/api/git/stats`, `/api/git/commits`, `/api/stats/leaderboard`
- **Интеграция**: Реальные данные из бекенда отображаются в диаграммах

### ✅ GitHub интеграция
- **Фронтенд**: Кнопки в табе GitHub (`/status`, `/commits`, `/branches`, `/prs`, `/ci`, `/authors`, `/search`)
- **Бекенд**: Реализованы методы `Handle*CommandForWebAppAsync` в `TelegramBotService.cs`
- **Интеграция**: GitHub данные отображаются в чате

### ✅ Достижения
- **Фронтенд**: Кнопки в табе Achievements (`/chatactivity`, `/resetactivity`, `/stats`, `/achievements`, `/leaderboard`, `/streaks`)
- **Бекенд**: Реализованы методы `Handle*CommandForWebAppAsync` в `TelegramBotService.cs`
- **Интеграция**: Статистика достижений отображается в чате

## API Endpoints

### Основные endpoints
- `GET /api/bot/status` - Статус бота
- `GET /api/ai/status` - Статус AI
- `GET /api/ai/stats` - Статистика AI
- `POST /api/ai/chat` - Чат с AI
- `POST /api/bot/command` - Универсальная команда бота

### GitHub endpoints
- `GET /api/git/stats` - Статистика Git
- `GET /api/git/commits` - Коммиты
- `GET /api/stats/leaderboard` - Таблица лидеров

## Запуск

1. Запустите бекенд: `cd TelegramGitHubBot && dotnet run`
2. Запустите фронтенд: `cd webapp-react && npm run dev`
3. Откройте http://localhost:5173

## Проверка интеграции

1. **AI таб**: Нажмите кнопки AI команд - они должны работать
2. **GIF таб**: Нажмите кнопки GIF команд - GIF должны отображаться в чате
3. **Games таб**: Нажмите кнопки игр - игры должны запускаться в чате
4. **Dashboard таб**: Статистика должна загружаться из бекенда
5. **GitHub таб**: GitHub команды должны работать
6. **Achievements таб**: Команды достижений должны работать
