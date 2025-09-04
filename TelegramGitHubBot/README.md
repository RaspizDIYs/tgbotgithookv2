# Telegram GitHub Bot

Бот для мониторинга CI/CD проекта goodluckv2 через Telegram с интеграцией GitHub API.

## 🚀 Возможности

### Мониторинг и уведомления
- ✅ Уведомления о новых коммитах
- ✅ Уведомления о pull requests (открытие, закрытие, мердж)
- ✅ Уведомления о CI/CD статусе
- ✅ Уведомления о релизах
- ✅ Уведомления о задачах (issues)

### Команды бота
- `/start` - Приветственное сообщение
- `/help` - Справка по командам
- `/status` - Статус репозитория
- `/commits [ветка] [количество]` - Последние коммиты
- `/branches` - Список веток
- `/prs` - Открытые pull requests
- `/ci [ветка] [количество]` - CI/CD статус
- `/deploy [среда]` - Запустить деплой

## ⚙️ Настройка

### 🔐 Безопасная настройка секретов

**НИКОГДА не храните секреты в репозитории!**

#### Вариант 1: Переменные окружения (Рекомендуемый)

1. **Создайте файл `.env`** (скопируйте из `env.example`):
   ```bash
   cp env.example .env
   ```

2. **Заполните `.env`** реальными значениями:
   ```env
   TELEGRAM_BOT_TOKEN=
   TELEGRAM_CHAT_ID=YOUR_TELEGRAM_CHAT_ID
   GITHUB_PAT=
   GITHUB_WEBHOOK_SECRET=YOUR_WEBHOOK_SECRET
   ```

#### Вариант 2: Файл конфигурации (Только для локальной разработки)

1. **Создайте `appsettings.Development.json`**:
   ```bash
   cp appsettings.Development.json.example appsettings.Development.json
   ```

2. **Добавьте секреты в `appsettings.Development.json`**:
   ```json
   {
     "Telegram": {
       "BotToken": "YOUR_TELEGRAM_BOT_TOKEN",
       "ChatId": "YOUR_TELEGRAM_CHAT_ID"
     },
     "GitHub": {
       "PersonalAccessToken": "YOUR_GITHUB_PAT",
       "WebhookSecret": "YOUR_WEBHOOK_SECRET"
     }
   }
   ```

#### Вариант 3: Системные переменные окружения

Установите переменные окружения на уровне системы:

**Windows (PowerShell):**
```powershell
$env:TELEGRAM_BOT_TOKEN="YOUR_TOKEN"
$env:GITHUB_PAT="YOUR_PAT"
```

**Linux/macOS:**
```bash
export TELEGRAM_BOT_TOKEN="YOUR_TOKEN"
export GITHUB_PAT="YOUR_PAT"
```

### 📝 Получение токенов

#### Telegram Bot Token
1. Напишите [@BotFather](https://t.me/botfather) в Telegram
2. Создайте нового бота командой `/newbot`
3. Скопируйте токен из ответа

#### GitHub Personal Access Token
1. Перейдите в [GitHub Settings > Developer settings > Personal access tokens](https://github.com/settings/tokens)
2. Создайте новый токен с правами:
   - ✅ `repo` (Full control of private repositories)
   - ✅ `workflow` (Update GitHub Action workflows)
   - ✅ `read:org` (Read org and team membership)
3. **Сохраните токен в безопасном месте!**

#### Telegram Chat ID
1. Добавьте бота в нужный чат
2. Отправьте сообщение в чат
3. Получите Chat ID через API или [@userinfobot](https://t.me/userinfobot)

### 2. Настройка GitHub Webhook

1. Перейдите в Settings репозитория [RaspizDIYs/goodluckv2](https://github.com/RaspizDIYs/goodluckv2/settings/hooks)
2. Выберите **Webhooks → Add webhook**
3. Укажите настройки:
   - **Payload URL**: `https://tgbotgithookv2.onrender.com/webhook/github`
   - **Content type**: `application/json`
   - **Secret**: Оставьте пустым (или укажите в переменной окружения `GITHUB_WEBHOOK_SECRET`)
   - **SSL verification**: Включено
   - **Events**: Выберите нужные события:
     - ✅ Push
     - ✅ Pull requests
     - ✅ Issues
     - ✅ Releases
     - ✅ Workflow runs

4. Нажмите **Add webhook**

### 🔄 Тестирование вебхука

После настройки вебхука:
1. Сделайте тестовый push в репозиторий
2. Проверьте логи в Render dashboard
3. Убедитесь, что бот получает уведомления в Telegram

### 🏥 Проверка статуса бота

**Health Check endpoint:**
```
GET https://tgbotgithookv2.onrender.com/health
```

**Ожидаемый ответ:**
```json
{
  "status": "healthy",
  "timestamp": "2025-09-04T12:00:00.0000000Z",
  "version": "1.0.0",
  "environment": "Production",
  "service": "TelegramGitHubBot"
}
```

### 3. Настройка Telegram бота

1. Создайте бота через [@BotFather](https://t.me/botfather)
2. Получите токен бота
3. Добавьте бота в нужный чат/канал
4. Получите Chat ID (можно через @userinfobot или API)

## 🚀 Запуск

### 🔧 Подготовка

1. **Настройте секреты** (см. раздел [Безопасная настройка секретов](#-безопасная-настройка-секретов))

2. **Проверьте конфигурацию**:
   ```bash
   dotnet build
   ```

### 💻 Локальный запуск

#### С переменными окружения
```bash
# Загрузите переменные из .env файла
dotnet run
```

#### С файлом конфигурации
```bash
cp appsettings.json.example appsettings.json
# Отредактируйте appsettings.json с реальными секретами
dotnet run
```

### 🐳 Docker запуск

#### С переменными окружения
```bash
# Создайте .env файл из шаблона
cp env.example .env
# Отредактируйте .env с реальными секретами

# Запустите через Docker Compose
docker-compose up -d
```

#### С переменными окружения системы
```bash
# Установите переменные окружения
export TELEGRAM_BOT_TOKEN="your_token"
export GITHUB_PAT="your_pat"

# Соберите и запустите
docker build -t telegram-github-bot .
docker run -p 8080:80 -p 8443:443 telegram-github-bot
```

### ☁️ Продакшн развертывание

#### 🚀 Render (Текущая настройка)

Ваш бот уже развернут на Render: **https://tgbotgithookv2.onrender.com**

**Настройка переменных окружения в Render:**
1. Перейдите в Render Dashboard → Your Service → Environment
2. Добавьте переменные:

   | Переменная | Значение | Обязательно |
   |------------|----------|-------------|
   | `TELEGRAM_BOT_TOKEN` | | ✅ |
   | `TELEGRAM_CHAT_ID` | Ваш Chat ID из Telegram | ✅ |
   | `GITHUB_PAT` | Ваш GitHub Personal Access Token | ✅ |
   | `GITHUB_WEBHOOK_SECRET` | Секрет для webhook (опционально) | ❌ |
   | `ASPNETCORE_ENVIRONMENT` | `Production` | ✅ |
   | `ASPNETCORE_URLS` | `http://+:10000` | ✅ |

3. **Перезапустите сервис** после добавления переменных

#### Azure App Service
```bash
# Установите переменные окружения в Azure Portal
# Application Settings:
# TELEGRAM_BOT_TOKEN = your_token
# GITHUB_PAT = your_pat
# ASPNETCORE_ENVIRONMENT = Production
```

#### Railway/Heroku
```bash
# Установите переменные окружения в панели управления
```

#### IIS/Windows Server
```bash
# Опубликуйте приложение
dotnet publish -c Release -o ./publish

# Настройте переменные окружения в IIS Manager
# или через web.config
```

## 🔧 Конфигурация

### Переменные окружения
```bash
Telegram__BotToken=YOUR_BOT_TOKEN
Telegram__ChatId=YOUR_CHAT_ID
GitHub__PersonalAccessToken=YOUR_GITHUB_PAT
GitHub__WebhookSecret=YOUR_SECRET
```

### Разрешенные пользователи для деплоя
В файле `TelegramBotService.cs` в методе `HandleDeployCommandAsync` добавьте разрешенных пользователей:

```csharp
var allowedUsers = new[] { "username1", "username2" };
```

## 📡 Webhook эндпоинты

- `POST /webhook/github` - Обработка GitHub webhook
- `POST /webhook/telegram/{token}` - Обработка Telegram webhook

## 🔍 Логирование

Бот логирует все события в консоль. Для продакшена настройте логирование в файл или внешние системы.

## 🔒 Безопасность

### ⚠️ Важные правила безопасности

1. **Никогда не коммитьте секреты** в репозиторий
2. **Используйте переменные окружения** для продакшена
3. **Регулярно обновляйте токены** (минимум раз в год)
4. **Используйте минимально необходимые права** для токенов
5. **Включайте GitHub Secret Scanning** в репозитории

### 🛡️ Защита секретов

#### GitHub Repository Settings
```bash
# Включите Secret Scanning
Repository Settings > Security > Code security > Secret scanning
```

#### GitGuardian или другие сканеры
- Регулярно сканируйте код на наличие секретов
- Настройте pre-commit хуки для проверки

#### Восстановление после утечки
1. **Немедленно отзовите** скомпрометированные токены
2. **Создайте новые токены** с другими значениями
3. **Проверьте логи** на подозрительную активность
4. **Уведомите** всех пользователей системы

### 🔐 Best Practices

- **Не используйте** production секреты для разработки
- **Храните** секреты в защищенном менеджере паролей
- **Документируйте** все используемые секреты
- **Ротируйте** секреты регулярно
- **Мониторьте** использование API токенов

## 🤝 Contributing

1. **Не коммитьте секреты** - они будут автоматически отклонены
2. Fork репозиторий
3. Создайте feature branch (`git checkout -b feature/amazing-feature`)
4. Сделайте изменения
5. **Убедитесь что нет секретов** в коммите (`git status --ignored`)
6. Создайте Pull Request

## 📄 Лицензия

MIT License
