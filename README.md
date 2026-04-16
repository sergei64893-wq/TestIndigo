# Система агрегации и обработки биржевых данных (Indigo)

## 📋 Описание

Высокопроизводительная система сбора, обработки и хранения ценовых данных с нескольких бирж в реальном времени на базе ASP.NET Core 9.0.

**Характеристики:**
- ⚡ Обработка 50-100 тиков/сек
- 🔗 2-3 одновременных WebSocket подключения
- 🗄️ PostgreSQL для хранения raw данных
- 🎯 Нормализация к единому формату
- 🔍 Удаление дубликатов (in-memory кэш)
- 📊 Логирование и мониторинг (Serilog)
- 🔄 Автоматическое переподключение с exponential backoff
- 🏗️ Clean Architecture с SOLID принципами

## 🏛️ Архитектура

```
Domain Layer
├── Entities (PriceTick, ExchangeSource)
├── ValueObjects (NormalizedTick)
└── Exceptions (DuplicateTickException, InvalidTickDataException)

Application Layer
├── Interfaces (Repository, Client, Detector, etc.)
└── Services (TickAggregationService, MonitoringService, etc.)

Infrastructure Layer
├── Database (EF Core, PostgreSQL)
├── WebSocket Clients (Binance, Kraken)
├── Repositories (TickRepository, ExchangeSourceRepository)
└── Services (Monitoring, DuplicateDetection)

Presentation Layer
├── Controllers (TicksController)
└── Middleware (GlobalExceptionHandlerMiddleware)
```

## 🚀 Установка и запуск

### Требования
- .NET 9.0 SDK
- PostgreSQL 12+
- Docker (опционально)

### 1. Настройка БД

Создайте БД PostgreSQL:
```bash
psql -U postgres
CREATE DATABASE indigo_db;
CREATE USER indigo_user WITH PASSWORD 'indigo_password';
ALTER ROLE indigo_user SET client_encoding TO 'utf8';
ALTER ROLE indigo_user SET default_transaction_isolation TO 'read committed';
ALTER ROLE indigo_user SET default_transaction_deferrable TO on;
GRANT ALL PRIVILEGES ON DATABASE indigo_db TO indigo_user;
```

Или используйте Docker:
```bash
docker run --name postgres_indigo \
  -e POSTGRES_USER=postgres \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=indigo_db \
  -p 5432:5432 \
  -d postgres:15
```

### 2. Обновление конфигурации

Отредактируйте `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=indigo_db;Username=postgres;Password=postgres"
  }
}
```

### 3. Применение миграций

```bash
cd Indigo
dotnet ef database update
```

### 4. Запуск приложения

```bash
dotnet run
```
