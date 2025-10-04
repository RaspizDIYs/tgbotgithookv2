# Руководство по деплою Telegram GitHub Bot

## Обзор проекта

Проект состоит из двух частей:
1. **Backend** - ASP.NET Core Web API с Telegram Bot
2. **Frontend** - React приложение с Vite

## Архитектура

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Telegram      │    │   GitHub        │    │   AI (Gemini)   │
│   Bot API       │◄──►│   API           │◄──►│   API           │
└─────────────────┘    └─────────────────┘    └─────────────────┘
         ▲                       ▲                       ▲
         │                       │                       │
         ▼                       ▼                       ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Backend (ASP.NET Core)                      │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌──────────┐ │
│  │ Telegram    │ │ GitHub      │ │ AI          │ │ GIF      │ │
│  │ Bot Service │ │ Service     │ │ Manager     │ │ Service  │ │
│  └─────────────┘ └─────────────┘ └─────────────┘ └──────────┘ │
└─────────────────────────────────────────────────────────────────┘
         ▲
         │
         ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Frontend (React + Vite)                     │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌──────────┐ │
│  │ AI Tab      │ │ GIF Tab     │ │ Games Tab   │ │ GitHub   │ │
│  │             │ │             │ │             │ │ Tab      │ │
│  └─────────────┘ └─────────────┘ └─────────────┘ └──────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

## Деплой Backend (Render.com)

### 1. Подготовка репозитория

```bash
# Клонируйте репозиторий
git clone https://github.com/your-username/your-repo.git
cd your-repo

# Убедитесь что проект собирается
cd TelegramGitHubBot
dotnet build
```

### 2. Создание сервиса на Render.com

1. Зайдите на [render.com](https://render.com)
2. Нажмите "New +" → "Web Service"
3. Подключите ваш GitHub репозиторий
4. Настройте сервис:

**Build Settings:**
- Build Command: `cd TelegramGitHubBot && dotnet publish -c Release -o ./publish`
- Start Command: `cd TelegramGitHubBot/publish && ./TelegramGitHubBot`

**Environment Variables:**
```bash
# Обязательные
TELEGRAM_BOT_TOKEN=your_telegram_bot_token
TELEGRAM_CHAT_ID=your_chat_id
GITHUB_PAT=your_github_pat
GEMINI_API_KEY=your_gemini_api_key
TENOR_API_KEY=your_tenor_api_key

# Опциональные
GITHUB_OWNER=your_github_username
GITHUB_REPO=your_repo_name
ALLOWED_ORIGINS=https://your-frontend-domain.com
ASPNETCORE_ENVIRONMENT=Production
```

### 3. Настройка Telegram Webhook

После деплоя получите URL вашего сервиса (например: `https://your-app.onrender.com`)

```bash
# Установите webhook
curl -X POST "https://api.telegram.org/bot<YOUR_BOT_TOKEN>/setWebhook" \
     -H "Content-Type: application/json" \
     -d '{"url": "https://your-app.onrender.com/webhook/telegram/<YOUR_BOT_TOKEN>"}'
```

## Деплой Frontend (GitHub Pages)

### Вариант 1: Использование папки `/docs` (рекомендуется)

#### 1. Настройка GitHub Actions

Создайте файл `.github/workflows/deploy-docs.yml` (уже создан):

```yaml
name: Deploy to GitHub Pages (docs folder)

on:
  push:
    branches: [ main ]
    paths:
      - 'webapp-react/**'
      - '.github/workflows/deploy-docs.yml'
  workflow_dispatch:

jobs:
  build-and-deploy:
    runs-on: ubuntu-latest
    
    steps:
    - name: Checkout
      uses: actions/checkout@v4
      
    - name: Setup Node.js
      uses: actions/setup-node@v4
      with:
        node-version: '18'
        cache: 'npm'
        cache-dependency-path: webapp-react/package-lock.json
        
    - name: Install dependencies
      run: |
        cd webapp-react
        npm ci
        
    - name: Build
      run: |
        cd webapp-react
        npm run build
      env:
        VITE_API_URL: ${{ secrets.VITE_API_URL || 'http://localhost:5000' }}
        
    - name: Copy to docs folder
      run: |
        rm -rf docs/*
        cp -r webapp-react/dist/* docs/
        
    - name: Commit and push
      run: |
        git config --local user.email "action@github.com"
        git config --local user.name "GitHub Action"
        git add docs/
        git diff --staged --quiet || git commit -m "Deploy frontend to docs folder [skip ci]"
        git push
```

#### 2. Настройка GitHub Pages

1. Зайдите в Settings → Pages
2. Source: "Deploy from a branch"
3. Branch: `main`
4. Folder: `/docs`
5. Добавьте секрет `VITE_API_URL` с URL вашего бэкенда

### Вариант 2: Использование GitHub Actions (современный способ)

#### 1. Настройка GitHub Actions

Создайте файл `.github/workflows/deploy-frontend.yml` (уже создан):

```yaml
name: Deploy Frontend to GitHub Pages

on:
  push:
    branches: [ main ]
    paths:
      - 'webapp-react/**'
      - '.github/workflows/deploy-frontend.yml'
  workflow_dispatch:

permissions:
  contents: read
  pages: write
  id-token: write

concurrency:
  group: "pages"
  cancel-in-progress: false

jobs:
  build-and-deploy:
    runs-on: ubuntu-latest
    
    steps:
    - name: Checkout
      uses: actions/checkout@v4
      
    - name: Setup Node.js
      uses: actions/setup-node@v4
      with:
        node-version: '18'
        cache: 'npm'
        cache-dependency-path: webapp-react/package-lock.json
        
    - name: Install dependencies
      run: |
        cd webapp-react
        npm ci
        
    - name: Build
      run: |
        cd webapp-react
        npm run build
      env:
        VITE_API_URL: ${{ secrets.VITE_API_URL || 'http://localhost:5000' }}
        
    - name: Setup Pages
      uses: actions/configure-pages@v4
      
    - name: Upload artifact
      uses: actions/upload-pages-artifact@v3
      with:
        path: webapp-react/dist
        
    - name: Deploy to GitHub Pages
      id: deployment
      uses: actions/deploy-pages@v4
```

#### 2. Настройка GitHub Pages

1. Зайдите в Settings → Pages
2. Source: "GitHub Actions"
3. Добавьте секрет `VITE_API_URL` с URL вашего бэкенда

## Деплой Frontend (Vercel)

### 1. Подключение к Vercel

1. Зайдите на [vercel.com](https://vercel.com)
2. Import Project → выберите ваш репозиторий
3. Настройте проект:

**Build Settings:**
- Framework Preset: Vite
- Root Directory: `webapp-react`
- Build Command: `npm run build`
- Output Directory: `dist`

**Environment Variables:**
```bash
VITE_API_URL=https://your-backend-domain.com
```

## Деплой Frontend (Netlify)

### 1. Подключение к Netlify

1. Зайдите на [netlify.com](https://netlify.com)
2. New site from Git → выберите ваш репозиторий
3. Настройте проект:

**Build Settings:**
- Base directory: `webapp-react`
- Build command: `npm run build`
- Publish directory: `webapp-react/dist`

**Environment Variables:**
```bash
VITE_API_URL=https://your-backend-domain.com
```

## Локальная разработка

### 1. Backend

```bash
cd TelegramGitHubBot

# Создайте .env файл
cat > .env << EOF
TELEGRAM_BOT_TOKEN=your_telegram_bot_token
TELEGRAM_CHAT_ID=your_chat_id
GITHUB_PAT=your_github_pat
GEMINI_API_KEY=your_gemini_api_key
TENOR_API_KEY=your_tenor_api_key
VITE_API_URL=http://localhost:5000
EOF

# Запустите
dotnet run
```

### 2. Frontend

```bash
cd webapp-react

# Создайте .env файл
cat > .env << EOF
VITE_API_URL=http://localhost:5000
EOF

# Установите зависимости
npm install

# Запустите
npm run dev
```

## Проверка деплоя

### 1. Backend Health Check

```bash
curl https://your-backend-domain.com/health
curl https://your-backend-domain.com/ping
```

### 2. Frontend

Откройте ваш фронтенд в браузере и проверьте:
- Загружается ли приложение
- Работают ли API вызовы
- Отображается ли статистика

### 3. Telegram Bot

Отправьте команду `/start` боту и проверьте:
- Получаете ли ответ
- Работают ли команды
- Отображается ли веб-приложение

## Мониторинг

### 1. Логи Render.com

- Зайдите в Dashboard → ваш сервис → Logs
- Проверьте ошибки и предупреждения

### 2. Telegram Bot Logs

```bash
# Проверьте статус webhook
curl "https://api.telegram.org/bot<YOUR_BOT_TOKEN>/getWebhookInfo"
```

### 3. GitHub API Limits

```bash
# Проверьте лимиты GitHub API
curl -H "Authorization: token <YOUR_GITHUB_PAT>" \
     https://api.github.com/rate_limit
```

## Troubleshooting

### Backend не запускается

1. Проверьте переменные окружения
2. Проверьте логи в Render.com
3. Убедитесь что все токены валидны

### Frontend не подключается к Backend

1. Проверьте `VITE_API_URL`
2. Проверьте CORS настройки
3. Проверьте что Backend доступен

### Telegram Bot не отвечает

1. Проверьте webhook URL
2. Проверьте `TELEGRAM_BOT_TOKEN`
3. Проверьте `TELEGRAM_CHAT_ID`

## Безопасность

⚠️ **ВАЖНО**:
- Никогда не коммитьте токены в репозиторий
- Используйте секреты платформы
- Регулярно ротируйте токены
- Ограничьте CORS домены в продакшене
