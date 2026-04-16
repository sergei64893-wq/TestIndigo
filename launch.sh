#!/bin/bash

# Indigo Stock Data Aggregation System - Launch Script

set -e  # Exit on error

echo "🚀 Indigo Stock Data Aggregation System Launcher"
echo "=================================================="
echo ""

# Check if Docker is installed
if ! command -v docker &> /dev/null; then
    echo "❌ Docker не найден. Пожалуйста, установите Docker."
    exit 1
fi

echo "✅ Docker найден"
echo ""

# Check for docker-compose
if ! command -v docker-compose &> /dev/null; then
    echo "⚠️  docker-compose не найден. Используем 'docker compose'..."
    DOCKER_COMPOSE="docker compose"
else
    DOCKER_COMPOSE="docker-compose"
fi

echo "📋 Выбор режима запуска:"
echo "1) Docker Compose (рекомендуется)"
echo "2) Локальный запуск (требует PostgreSQL)"
echo ""
read -p "Введи номер (1 или 2): " choice

case $choice in
    1)
        echo ""
        echo "🐳 Запуск через Docker Compose..."
        echo ""
        
        # Kill existing containers if any
        echo "🧹 Очистка старых контейнеров..."
        $DOCKER_COMPOSE down 2>/dev/null || true
        
        # Start containers
        echo "▶️  Запуск контейнеров..."
        $DOCKER_COMPOSE up -d
        
        echo ""
        echo "⏳ Ожидание инициализации БД (10-15 сек)..."
        sleep 15
        
        # Check health
        echo ""
        echo "🏥 Проверка здоровья приложения..."
        
        for i in {1..30}; do
            if curl -s http://localhost:5000/health > /dev/null 2>&1; then
                echo "✅ Приложение запущено и готово!"
                break
            fi
            echo "⏳ Попытка $i/30... ждём запуска..."
            sleep 1
        done
        
        echo ""
        echo "🎉 Успешно! Доступно по адресам:"
        echo "   🌐 Swagger UI: http://localhost:5000/swagger/index.html"
        echo "   🏥 Health: http://localhost:5000/health"
        echo "   📊 API: http://localhost:5000/api/ticks"
        echo ""
        echo "📋 Просмотр логов:"
        echo "   docker logs indigo_app -f"
        echo ""
        echo "🛑 Для остановки:"
        echo "   docker-compose down"
        ;;
        
    2)
        echo ""
        echo "💻 Локальный запуск"
        echo ""
        echo "⚠️  Убедись, что PostgreSQL запущен!"
        echo ""
        
        cd Indigo
        
        echo "📦 Восстановление зависимостей..."
        dotnet restore
        
        echo ""
        echo "🗂️  Применение миграций БД..."
        dotnet ef database update
        
        echo ""
        echo "🚀 Запуск приложения..."
        dotnet run
        ;;
        
    *)
        echo "❌ Неверный выбор"
        exit 1
        ;;
esac

