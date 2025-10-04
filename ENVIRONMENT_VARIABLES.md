# Переменные окружения для деплоя

## Обязательные переменные

### Telegram Bot
- `TELEGRAM_BOT_TOKEN` - Токен Telegram бота (обязательно)
- `TELEGRAM_CHAT_ID` - ID чата для отправки сообщений (обязательно)

### GitHub
- `GITHUB_PAT` - Personal Access Token для GitHub API (обязательно)
- `GITHUB_OWNER` - Владелец репозитория (по умолчанию: "RaspizDIYs")
- `GITHUB_REPO` - Название репозитория (по умолчанию: "goodluckv2")

### AI (Gemini)
- `GEMINI_API_KEY` - API ключ для Gemini (обязательно для AI функций)

### GIF (Tenor)
- `TENOR_API_KEY` - API ключ для Tenor (обязательно для GIF функций)

## Опциональные переменные

### Развертывание
- `PORT` - Порт для Render.com (автоматически устанавливается Render)
- `RENDER_EXTERNAL_URL` - Внешний URL для Render.com
- `ASPNETCORE_URLS` - URLs для ASP.NET Core
- `ASPNETCORE_ENVIRONMENT` - Окружение (Production/Development)

### CORS
- `ALLOWED_ORIGINS` - Разрешенные домены через запятую (если не указано - разрешены все)

### Данные
- `DATA_DIR` - Директория для хранения данных (по умолчанию: ./data)

### GitHub Persistence (для достижений)
- `GITHUB_PERSIST_OWNER` - Владелец репозитория для сохранения данных
- `GITHUB_PERSIST_REPO` - Репозиторий для сохранения данных
- `GITHUB_PERSIST_PATH` - Путь к файлу данных
- `GITHUB_PERSIST_BRANCH` - Ветка для сохранения данных

### Telegram Bot
- `TELEGRAM_ENABLE_POLLING` - Включить polling (по умолчанию: true)

### Web App
- `WEBAPP_URL` - URL веб-приложения

### Workspace Paths
- `GOODLUCK_WORKSPACE_PATH` - Путь к рабочему пространству GoodLuck
- `CURSOR_WORKSPACE_PATH` - Путь к рабочему пространству Cursor

### Bridge
- `BRIDGE_BASE_URL` - Базовый URL для моста

### GIF
- `TENOR_WEEKEND_GIF` - Специальный GIF для выходных

## Frontend переменные

### React/Vite
- `VITE_API_URL` - URL API бэкенда (по умолчанию: http://localhost:5000)

## Примеры для разных платформ

### Render.com
```bash
TELEGRAM_BOT_TOKEN=your_telegram_bot_token
TELEGRAM_CHAT_ID=your_chat_id
GITHUB_PAT=your_github_pat
GEMINI_API_KEY=your_gemini_api_key
TENOR_API_KEY=your_tenor_api_key
GITHUB_OWNER=your_github_username
GITHUB_REPO=your_repo_name
ALLOWED_ORIGINS=https://your-frontend-domain.com
```

### GitHub Pages (только фронтенд)
```bash
VITE_API_URL=https://your-backend-domain.com
```

### Локальная разработка
```bash
TELEGRAM_BOT_TOKEN=your_telegram_bot_token
TELEGRAM_CHAT_ID=your_chat_id
GITHUB_PAT=your_github_pat
GEMINI_API_KEY=your_gemini_api_key
TENOR_API_KEY=your_tenor_api_key
VITE_API_URL=http://localhost:5000
```

## Безопасность

⚠️ **ВАЖНО**: Никогда не коммитьте токены и ключи в репозиторий!

- Используйте `.env` файлы для локальной разработки
- Добавьте `.env*` в `.gitignore`
- Используйте секреты платформы для продакшена
- Регулярно ротируйте токены и ключи
