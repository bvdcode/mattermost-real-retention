# Mattermost Real File Retention

![Docker Pulls](https://img.shields.io/docker/pulls/bvdcode/mattermost-real-retention)
![GitHub Release](https://img.shields.io/github/v/release/bvdcode/mattermost-real-file-retention)
![.NET](https://img.shields.io/badge/.NET-9.0-blue)

**Безопасная автоматическая очистка файлов-сирот в Mattermost Community/Team Edition без необходимости Enterprise лицензии.**

Сервис автоматически находит и удаляет файлы, которые больше не связаны с активными постами в Mattermost, помогая экономить дисковое пространство и поддерживать чистоту файловой системы.

<img width="718" height="459" alt="image" src="https://github.com/user-attachments/assets/53142610-f641-4305-8e29-872fd3d9156f" />

## ✨ Особенности

- 🔄 **Автоматическая очистка**: Ежедневный запуск задачи очистки файлов-сирот
- 🛡️ **Безопасность**: Режим "сухого прогона" (dry run) по умолчанию для тестирования
- 📊 **Подробное логирование**: Детальная информация о процессе очистки
- 🐳 **Docker Ready**: Готовый Docker образ для быстрого развертывания
- ⚡ **Высокая производительность**: Построен на .NET 9.0 с оптимизацией
- 🔒 **Безопасная работа с БД**: Только удаление записей о файлах, никаких изменений данных Mattermost

## 🏗️ Архитектура

Проект построен на современном стеке технологий:

- **.NET 9.0** - Основная платформа
- **Entity Framework Core** - ORM для работы с базой данных PostgreSQL
- **Quartz.NET** - Планировщик задач для автоматического запуска
- **ASP.NET Core** - Web API хост
- **PostgreSQL** - База данных Mattermost

### Компоненты системы

```
Sources/
├── Program.cs                    # Точка входа приложения
├── Database/
│   ├── AppDbContext.cs          # Контекст Entity Framework
│   └── Models/
│       ├── MattermostPost.cs    # Модель постов Mattermost
│       └── MattermostFileInfo.cs # Модель файловой информации
└── Jobs/
    └── RetentionJob.cs          # Основная задача очистки файлов
```

## 🚀 Быстрый старт

### Использование Docker Compose (Рекомендуется)

1. Создайте файл `docker-compose.yml`:

```yaml
services:
  mattermost-real-retention:
    image: bvdcode/mattermost-real-retention:latest
    restart: always
    environment:
      - PostgresHost=postgres
      - PostgresPort=5432
      - PostgresUser=mattermost
      - PostgresPassword=changeme
      - PostgresDatabase=mattermost
      - DryRun=true  # Установите false для реального удаления
    volumes:
      - /path/to/mattermost/data:/mattermost/data:rw
    networks:
      - mattermost_network
```

2. Запустите контейнер:

```bash
docker-compose up -d
```

### Использование Docker

```bash
docker run -d \
  --name mattermost-retention \
  --restart always \
  -e PostgresHost=your_postgres_host \
  -e PostgresPort=5432 \
  -e PostgresUser=mattermost \
  -e PostgresPassword=your_password \
  -e PostgresDatabase=mattermost \
  -e DryRun=true \
  -v /path/to/mattermost/data:/mattermost/data:rw \
  bvdcode/mattermost-real-retention:latest
```

## ⚙️ Конфигурация

### Переменные окружения

| Переменная | Описание | По умолчанию | Обязательная |
|------------|----------|--------------|--------------|
| `PostgresHost` | Хост PostgreSQL сервера | `postgres-server` | ✅ |
| `PostgresPort` | Порт PostgreSQL | `5432` | ❌ |
| `PostgresUser` | Имя пользователя PostgreSQL | `mattermost_server` | ✅ |
| `PostgresPassword` | Пароль PostgreSQL | - | ✅ |
| `PostgresDatabase` | Имя базы данных | `mattermost` | ✅ |
| `DryRun` | Режим тестирования (не удаляет файлы) | `true` | ❌ |

### Настройка подключения к базе данных

Сервис использует те же настройки подключения к PostgreSQL, что и ваш Mattermost сервер. Убедитесь, что:

1. Пользователь имеет права на чтение таблиц `posts` и `fileinfo`
2. Пользователь имеет права на удаление записей из таблицы `fileinfo` (только при `DryRun=false`)
3. Сервис может подключиться к базе данных Mattermost

## 🔧 Как это работает

### Алгоритм работы

1. **Сканирование файлов**: Каждые 24 часа сервис сканирует директорию `/mattermost/data/`
2. **Поиск файлов по датам**: Обрабатываются только директории в формате `YYYYMMDD`
3. **Проверка в базе данных**: Для каждого файла проверяется:
   - Существует ли запись в таблице `fileinfo`
   - Связан ли файл с активным постом (не удаленным)
   - Помечен ли сам файл как удаленный
4. **Безопасное удаление**: Файлы-сироты удаляются как из файловой системы, так и из базы данных

### Типы файлов для удаления

Сервис удаляет файлы в следующих случаях:

- ✅ Файл не найден в таблице `fileinfo`
- ✅ Файл связан с удаленным постом (`posts.deleteat > 0`)
- ✅ Файл помечен как удаленный (`fileinfo.deleteat > 0`)

### Безопасность

- 🔒 Никогда не удаляет файлы, связанные с активными постами
- 📝 Подробное логирование всех операций
- 🧪 Режим "сухого прогона" для тестирования
- ⏱️ Задержка 250мс между проверками файлов для снижения нагрузки

## 📊 Мониторинг и логирование

### Уровни логирования

- **Information**: Общая информация о процессе
- **Warning**: Найденные файлы-сироты
- **Debug**: Детальная информация о каждом файле

### Примеры логов

```
[Information] Found 1523 files in 45 date directories in /mattermost/data/.
[Warning] File 20241201/abc123/image.jpg not found in the database - deleting from filesystem.
[Information] Dry run enabled, skipping actual deletion.
[Information] Retention job completed. 42 files deleted, 1523 files total.
```

## 🛠️ Разработка

### Требования

- .NET 9.0 SDK
- PostgreSQL (для тестирования)
- Docker (опционально)

### Сборка проекта

```bash
cd Sources
dotnet restore
dotnet build
```

### Запуск в режиме разработки

```bash
cd Sources
dotnet run
```

### Сборка Docker образа

```bash
docker build -t mattermost-retention ./Sources
```

## 🔄 CI/CD

Проект использует GitHub Actions для автоматической сборки и публикации Docker образов:

- **Docker Hub**: `bvdcode/mattermost-real-retention`
- **GitHub Container Registry**: `ghcr.io/bvdcode/mattermost-real-retention`

Образы собираются автоматически при каждом push в ветку `main`.

## 📋 Требования к системе

### Минимальные требования

- **CPU**: 1 ядро
- **RAM**: 1GB
- **Диск**: Минимум для хранения образа (~200MB)
- **Доступ**: Чтение директории данных Mattermost
- **Сеть**: Подключение к PostgreSQL серверу

### Рекомендации по развертыванию

- Запускайте сервис на том же сервере, где находятся файлы Mattermost
- Используйте сетевое хранилище если Mattermost работает в кластере
- Настройте мониторинг логов для отслеживания работы
- Начните с режима `DryRun=true` для оценки объема очистки

## ❗ Важные замечания

1. **Режим сухого прогона**: По умолчанию включен `DryRun=true` - файлы не удаляются, только логируются
2. **Резервные копии**: Обязательно создайте резервную копию данных перед первым запуском
3. **Тестирование**: Протестируйте сервис в режиме dry run перед продакшн использованием
4. **Права доступа**: Убедитесь, что контейнер имеет права на чтение/запись в директории данных

## 🤝 Вклад в проект

Приветствуются любые вклады в проект:

1. Fork репозиторий
2. Создайте feature ветку
3. Внесите изменения
4. Создайте Pull Request

## 📄 Лицензия

Этот проект распространяется под лицензией MIT. См. файл [LICENSE](LICENSE) для деталей.

## 🆘 Поддержка

Если у вас возникли вопросы или проблемы:

- Создайте [Issue](https://github.com/bvdcode/mattermost-real-file-retention/issues)
- Ознакомьтесь с существующими Issues
- Проверьте логи сервиса для диагностики

