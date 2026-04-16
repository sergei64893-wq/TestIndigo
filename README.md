# Система агрегации и обработки биржевых данных (Indigo)

## 📋 Описание

Высокопроизводительная система сбора, обработки и хранения ценовых данных с нескольких бирж в реальном времени на базе ASP.NET Core 9.0.

**Характеристики:**
- ⚡ Обработка 50-100 тиков/сек
- 🔗 2-3 одновременных WebSocket подключения
- 🗄️ PostgreSQL для хранения raw данных
- 🎯 Нормализация к единому формату
- 🔍 Удаление дубликатов (in-memory кэш или Redis)
- 📊 Логирование и мониторинг (Serilog)
- 🔄 Автоматическое переподключение с exponential backoff
- 🏗️ Clean Architecture с SOLID принципами
- 📨 Kafka для передачи нормализованных тиков
- 🗂️ Batch-отправка: по размеру или таймауту

## 🏛️ Архитектура

```
Domain Layer
├── Entities (PriceTick, ExchangeSource)
├── ValueObjects (NormalizedTick)
└── Exceptions (DuplicateTickException, InvalidTickDataException)

Application Layer
├── Interfaces (Repository, Client, Detector, etc.)
└── Services (TickAggregationService, MonitoringService, TickProducer, etc.)

Infrastructure Layer
├── Database (EF Core, PostgreSQL)
├── WebSocket Clients (Binance, Kraken)
├── Repositories (TickRepository, ExchangeSourceRepository)
├── Services (Monitoring, DuplicateDetection, Kafka, Redis)
└── Kafka (ITopicProducer)

Presentation Layer
├── Controllers (TicksController)
└── Middleware (GlobalExceptionHandlerMiddleware)
```

## 🛠️ Компоненты

- **Kafka** — для передачи нормализованных тиков (продюсер, топик по умолчанию: `exchange-ticks`, настраивается через `appsettings.json`).
- **Redis** — используется для дедупликации тиков (опционально, см. `REDIS_DUPLICATE_DETECTOR_SUMMARY.md`).
- **Batching** — отправка тиков в Kafka происходит батчами:
  - Батч отправляется либо при достижении размера (по умолчанию 1000), либо по таймауту (по умолчанию 5 секунд).
  - Логика реализована в `TickProducer`:
    - Если за 5 секунд не набралось 1000 тиков, отправляется всё, что есть.
    - Таймаут и размер батча настраиваются через конфиг:
      ```json
      {
        "TickAggregation": {
          "BatchSize": 1000,
          "FlushIntervalMs": 5000
        }
      }
      ```
- **Миграции** — используется только одна миграция (`InitialSchema`). Если миграций больше одной — оставьте только актуальную.

## 🚀 Установка и запуск

### Требования
- .NET 9.0 SDK
- PostgreSQL 12+
- Kafka (например, через Docker)
- Redis (опционально)
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

### 2. Настройка Kafka (Docker)
```bash
docker run -d --name zookeeper -p 2181:2181 confluentinc/cp-zookeeper:7.5.0

docker run -d --name kafka -p 9092:9092 \
  -e KAFKA_ZOOKEEPER_CONNECT=zookeeper:2181 \
  -e KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://localhost:9092 \
  -e KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1 \
  --network=host \
  confluentinc/cp-kafka:7.5.0
```

### 3. Настройка Redis (опционально)
```bash
docker run --name redis_indigo -p 6379:6379 -d redis:7
```

### 4. Обновление конфигурации

Отредактируйте `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=indigo_db;Username=postgres;Password=postgres"
  },
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "Topic": "exchange-ticks"
  },
  "Redis": {
    "Configuration": "localhost:6379"
  },
  "TickAggregation": {
    "BatchSize": 1000,
    "FlushIntervalMs": 5000
  }
}
```

### 5. Применение миграций

```bash
cd Indigo
# Оставьте только одну актуальную миграцию (InitialSchema)
dotnet ef database update
```

### 6. Запуск приложения

```bash
dotnet run
```

## 🧹 Чистота репозитория

- Добавьте в `.gitignore`:
  ```
  bin/
  obj/
  *.user
  *.suo
  *.db
  *.sqlite
  *.log
  Indigo.Tests/obj/
  Indigo.Tests/bin/
  Indigo.Infrastructure/Migrations/*_*.cs
  Indigo.Infrastructure/Migrations/*_*.Designer.cs
  Indigo.Infrastructure/Migrations/AppDbContextModelSnapshot.cs
  !Indigo.Infrastructure/Migrations/2026*_InitialSchema.cs
  !Indigo.Infrastructure/Migrations/2026*_InitialSchema.Designer.cs
  !Indigo.Infrastructure/Migrations/AppDbContextModelSnapshot.cs
  ```
- Не коммитьте сгенерированные файлы, dll, pdb и временные миграции.

---

## 📝 Пример настройки Kafka и Redis

```json
{
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "Topic": "exchange-ticks"
  },
  "Redis": {
    "Configuration": "localhost:6379"
  }
}
```

---

## 🏁 Логика TickProducer (Batching)

- Батч отправляется либо по размеру, либо по таймауту (см. `TickProducer`).
- Используется CancellationTokenSource с таймаутом для сброса батча.
- Если основной токен отменён — сервис корректно завершает работу.

---

## 📞 Контакты и поддержка

- Вопросы и баги — через Issues на GitHub.
- Документация по Redis и дедупликации: см. `REDIS_DUPLICATE_DETECTOR_SUMMARY.md`.
- Kafka: https://kafka.apache.org/quickstart
- EF Core: https://learn.microsoft.com/ef/core/
