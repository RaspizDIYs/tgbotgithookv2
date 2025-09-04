# Используем официальный образ .NET 8.0
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Копируем csproj файлы и восстанавливаем зависимости
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["TelegramGitHubBot.csproj", "TelegramGitHubBot.csproj"]
RUN dotnet restore "TelegramGitHubBot.csproj"

# Копируем все файлы и собираем проект
COPY . .
WORKDIR "/src"
RUN dotnet build "TelegramGitHubBot.csproj" -c Release -o /app/build

# Публикуем приложение
FROM build AS publish
WORKDIR "/src"
RUN dotnet publish "TelegramGitHubBot.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Финальный образ
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TelegramGitHubBot.dll"]
