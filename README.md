# Weather Bot ☀️

Телеграм-бот для прогноза погоды на завтра. Работает **без постоянно запущенного процесса**: GitHub Actions
просыпается каждый час, забирает новые сообщения, отвечает на команды и рассылает прогноз тем пользователям,
у кого в часовом поясе их города наступил час рассылки (по умолчанию **18:00 по местному времени города**).
Состояние (пользователи, их города и отметка о последней отправке) хранится в `state.json` прямо в репозитории,
поэтому ничего не теряется между запусками.

## Что умеет

| Возможность | Детали |
| --- | --- |
| Доступ по паролю | Первое сообщение — пароль (`BOT_PASSWORD`). До авторизации любые команды заменяются просьбой прислать пароль |
| Выбор города | Название города обычным сообщением или командой `/city Москва`. Поддерживаются уточнения: `Париж, FR` |
| Прогноз на завтра | Минимум/максимум, «ощущается как», характер погоды, вероятность и объём осадков, влажность, ветер и порывы |
| Ежедневная рассылка | Раз в сутки в 18:00 по времени города. Повторно прогноз на ту же дату не отправляется |
| Часовые пояса | Смещение берётся из ответа OpenWeatherMap для города; пока город не выбран — `BOT_TIMEZONE_OFFSET_HOURS` (по умолчанию +3) |
| Только личные чаты | Сообщения из групп и каналов игнорируются, чтобы бот не мешал в общих чатах |
| Режимы отладки | `--dry-run` (печать вместо отправки), `--force-send` (слать немедленно), `--print-forecast "Москва"` (посмотреть прогноз из консоли) |

### Команды бота

| Команда | Действие |
| --- | --- |
| `/start` | Начать работу: до авторизации просит пароль, после — включает рассылку |
| `/login <пароль>`, `/password <пароль>` | Вход по паролю (пароль можно прислать и просто сообщением) |
| `/city <город>` | Выбрать или изменить город |
| `/tomorrow`, `/weather`, `/forecast` | Прислать прогноз на завтра прямо сейчас |
| `/status` | Показать доступ, город, местное время, состояние рассылки, дату последнего прогноза |
| `/stop`, `/unsubscribe` | Выключить ежедневную рассылку |
| `/subscribe` | Включить ежедневную рассылку |
| `/logout` | Выйти (понадобится пароль) |
| `/chatid` | Показать идентификатор чата — полезно при первой настройке секретов |
| `/help` | Справка |
| любое другое текстовое сообщение | Воспринимается как название города (после авторизации) или как пароль (до авторизации) |

## Как это работает

```
GitHub Actions (cron: каждый час)
        │
        ├── dotnet test                       # тесты на xUnit
        ├── dotnet run --project src/WeatherBot
        │       ├── Telegram getUpdates       # новые сообщения → MessageProcessor (авторизация, города, команды)
        │       ├── DailySchedule.IsDue(...)  # кому уже пора получить прогноз
        │       ├── WeatherService            # geocoding + 5-day / 3-hour forecast → WeatherFormatter
        │       └── TelegramNotifier          # отправка сообщения (HTML)
        └── git commit state.json             # состояние возвращается в репозиторий
```

Прогноз на завтра собирается из 3-часовых точек OpenWeatherMap, попадающих в завтрашний день **города**
(дата считается по смещению из ответа API), и агрегируется в один день: минимум/максимум температуры,
максимальная вероятность осадков, сумма осадков, средняя влажность, максимальный ветер.

## Структура проекта

```
WeatherBot.sln
src/WeatherBot/
├── Program.cs                       # точка входа, сборка зависимостей, режим --print-forecast
├── Configuration/BotOptions.cs      # аргументы командной строки + переменные окружения
├── Models/
│   ├── BotState.cs                  # состояние: служебные поля + список пользователей
│   ├── BotUser.cs                   # пользователь: доступ, город, координаты, пояс, подписка
│   ├── WeatherModels.cs             # GeoCity, DailyForecast, CityForecast
│   ├── CityForecastExtensions.cs    # подстановка названия города из настроек пользователя
│   └── OpenWeather/OpenWeatherModels.cs  # DTO ответов geocoding и forecast
├── Security/PasswordChecker.cs      # сравнение пароля, устойчивое к таймингам
└── Services/
    ├── BotRunner.cs                 # один полный цикл работы бота
    ├── MessageProcessor.cs          # маршрутизация сообщений и команд
    ├── WeatherService.cs            # IWeatherService: HTTP-запросы к OpenWeatherMap
    ├── ForecastBuilder.cs           # агрегация 3-часовых точек в прогноз на день
    ├── WeatherFormatter.cs          # текст сообщения (HTML и plain text)
    ├── BotMessages.cs               # тексты сообщений бота
    ├── DailySchedule.cs             # когда и кому отправлять прогноз
    ├── TelegramNotifier.cs          # IMessageSender: отправка через Telegram.Bot (+ dry-run)
    ├── JsonStateStore.cs            # IStateStore: чтение/запись state.json
    └── ConsoleLog.cs                # логи с отметкой времени
tests/WeatherBot.Tests/              # xUnit: 168 тестов; сеть, время и Telegram подменяются
.github/workflows/weather-bot.yml    # hourly cron, тесты, запуск бота, коммит state.json
```

## Настройка

1. Клонируйте репозиторий и соберите проект (нужен .NET SDK 8.0):

   ```bash
   git clone https://github.com/<ваш-логин>/weather_bot.git
   cd weather_bot
   dotnet build WeatherBot.sln
   dotnet test WeatherBot.sln
   ```

2. Получите ключ OpenWeatherMap на [openweathermap.org/api](https://openweathermap.org/api) —
   раздел *My API keys*. Новый ключ активируется в течение пары часов: до этого API отвечает
   `401 Unauthorized`, а бот пишет «OpenWeatherMap отклонил API-ключ».
3. Создайте бота у [@BotFather](https://t.me/BotFather) и сохраните токен.
4. Придумайте пароль, который бот будет спрашивать у пользователей.
5. Добавьте секреты и переменные репозитория: `Settings → Secrets and variables → Actions`.

### Секреты и переменные GitHub Actions

| Имя | Где задавать | Обязательно | По умолчанию | Назначение |
| --- | --- | --- | --- | --- |
| `TELEGRAM_BOT_TOKEN` | secret | да | — | Токен бота от @BotFather |
| `OPENWEATHER_API_KEY` | secret | да | — | Ключ OpenWeatherMap |
| `BOT_PASSWORD` | secret | да | — | Пароль, который бот спрашивает у пользователей |
| `BOT_DAILY_SEND_HOUR` | variable | нет | `18` | Час рассылки (0–23) по местному времени города |
| `BOT_TIMEZONE_OFFSET_HOURS` | variable | нет | `3` | Смещение для городов без данных о поясе: от `-12` до `14`, допустимы дробные (`5.5`) |
| `BOT_DRY_RUN` | variable | нет | `false` | `true` — печатать прогноз в лог вместо отправки |
| `BOT_FORCE_SEND` | variable | нет | `false` | `true` — слать прогноз при каждом запуске, не глядя на час и прошлые отправки |
| `BOT_STATE_FILE` | variable | нет | `state.json` | Файл состояния; workflow коммитит именно `state.json`, менять имеет смысл для локального запуска |

Секреты обязательны всегда: workflow запускает и тесты, и бота. Если шаг «Сохранить состояние в репозитории»
падает с ошибкой доступа, проверьте `permissions: contents: write` в `.github/workflows/weather-bot.yml`
и `Settings → Actions → General → Workflow permissions` (нужно разрешение на запись).

Полезно знать: расписание GitHub Actions не гарантирует точность до минуты — под нагрузкой запуск может
задержаться на 5–20 минут. Прогноз всё равно придёт один раз, потому что дата последней отправки хранится
в `state.json`. Если репозиторий не проявляет активности 60 дней, GitHub отключает расписание — тогда
включите workflow заново на вкладке `Actions → Weather bot → Enable workflow`.

### Все настройки

Параметры читаются так (`src/WeatherBot/Configuration/BotOptions.cs`): сначала аргумент командной строки,
если его нет — переменная окружения, если и её нет — значение по умолчанию.

| Аргумент | Переменная окружения | По умолчанию | Назначение |
| --- | --- | --- | --- |
| `--telegram-token` | `TELEGRAM_BOT_TOKEN` | — | Токен бота. Обязателен, кроме режима `--print-forecast` |
| `--openweather-key` | `OPENWEATHER_API_KEY` | — | Ключ OpenWeatherMap. Обязателен всегда |
| `--password` | `BOT_PASSWORD` | — | Пароль доступа. Обязателен, кроме режима `--print-forecast` |
| `--print-forecast "<город>"` | — | — | Напечатать прогноз на завтра в консоль и выйти, ничего не отправляя |
| `--state` | `BOT_STATE_FILE` | `state.json` | Путь к файлу состояния |
| `--timezone-offset` | `BOT_TIMEZONE_OFFSET_HOURS` | `3` | Часовой пояс в часах от UTC: от `-12` до `14`, допустимы дробные (`5.5`) |
| `--send-hour` | `BOT_DAILY_SEND_HOUR` | `18` | Час рассылки в местном времени города: `0`–`23` |
| `--dry-run` | `BOT_DRY_RUN` | `false` | Печатать сообщения в лог вместо отправки |
| `--force-send` | `BOT_FORCE_SEND` | `false` | Отправить прогноз немедленно, даже если сегодня уже отправляли |

Аргументы можно писать и как `--send-hour 20`, и как `--send-hour=20`; флаги (`--dry-run`, `--force-send`)
значения не требуют. Некорректное значение (например `--send-hour 25`) приводит к понятному сообщению
и выходу с кодом 1.

### Запуск локально

```bash
# bash / Linux / macOS
export TELEGRAM_BOT_TOKEN="123456789:AA..."
export OPENWEATHER_API_KEY="ваш-ключ"
export BOT_PASSWORD="секретный-пароль"
```

```powershell
# PowerShell (Windows)
$env:TELEGRAM_BOT_TOKEN = "123456789:AA..."
$env:OPENWEATHER_API_KEY = "ваш-ключ"
$env:BOT_PASSWORD = "секретный-пароль"
```

```bash
# обычный цикл: ответы на сообщения + прогноз тем, у кого в городе наступил час рассылки
dotnet run --project src/WeatherBot

# ничего не отправлять, только печатать в консоль
dotnet run --project src/WeatherBot -- --dry-run

# разослать прогноз прямо сейчас, не дожидаясь часа рассылки
dotnet run --project src/WeatherBot -- --force-send

# проверить ключ OpenWeatherMap и текст сообщения (Telegram не нужен)
dotnet run --project src/WeatherBot -- --print-forecast "Москва"
```

Свой `chatId` для первой настройки можно узнать командой `/chatid`. После локального запуска рядом
появится `state.json` — это тот же файл, который workflow коммитит в репозиторий, и его удобно
просматривать глазами.

### Ручной запуск в GitHub Actions

`Actions → Weather bot → Run workflow`. Доступны три необязательных параметра:

| Параметр | Действие |
| --- | --- |
| `force_send` | Разослать прогноз немедленно, не дожидаясь часа рассылки |
| `dry_run` | Ничего не отправлять в Telegram, только вывести текст в лог |
| `print_forecast` | Напечатать прогноз на завтра для указанного города (например `Москва`) и выйти |

Параметры `force_send` и `dry_run` перекрывают одноимённые переменные репозитория: удобно проверить
настройки, не меняя их для регулярного расписания.

## Тесты

```bash
dotnet test WeatherBot.sln                                # все тесты
dotnet test WeatherBot.sln --filter WeatherServiceTests    # только один класс
```

Тесты не обращаются ни к сети, ни к Telegram, ни к системным часам: HTTP-транспорт, время, Telegram-клиент
и файловая система подменяются (см. `tests/WeatherBot.Tests/TestDoubles.cs` и `TestData.cs`).

| Файл | Что проверяет |
| --- | --- |
| `BotOptionsTests.cs` | Разбор `--аргументов` и переменных окружения, значения по умолчанию, валидация `--send-hour` и `--timezone-offset`, приоритет аргументов над окружением |
| `PasswordCheckerTests.cs` | Сравнение пароля: верный, неверный, пустой пароль, чувствительность к регистру |
| `BotStateTests.cs` | Получение и создание пользователя, обновление профиля, выборка подписчиков |
| `MessageProcessorTests.cs` | Авторизация по паролю, все команды (`/start`, `/help`, `/login`, `/city`, `/tomorrow`, `/status`, `/stop`, `/subscribe`, `/logout`, `/chatid`), поведение до входа, игнорирование общих чатов, ошибки поиска города и прогноза |
| `DailyScheduleTests.cs` | Местное время пользователя, дата «завтра», формат даты, условия отправки (час рассылки, повторная отправка в тот же день, `--force-send`) |
| `ForecastBuilderTests.cs` | Дата точки по смещению пояса, агрегация дня, выбор преобладающего состояния, иконки дня и ночи, порог осадков |
| `WeatherFormatterTests.cs` | Текст сообщения в HTML и plain text: экранирование, эмодзи, отсутствие данных о порывах и осадках |
| `WeatherServiceTests.cs` | HTTP-слой: параметры запросов geocoding и forecast, разбор ответов, тексты ошибок (`401`, `404`, `429`, `5xx`, битый JSON, пустой прогноз) на подменённом `HttpMessageHandler` |
| `JsonStateStoreTests.cs` | Чтение и запись `state.json`: camelCase-имена полей, отсутствие временного файла, создание каталогов, битый JSON, неизвестные поля |
| `TestData.cs`, `TestDoubles.cs` | Общие примеры данных и подмены: `FakeWeatherService`, `RecordingSender`, `StubHttpMessageHandler`, `FixedTimeProvider`, временный каталог и область переменных окружения |

## Лицензия

Проект распространяется «как есть» — используйте и меняйте свободно. Данные о погоде предоставляет
[OpenWeatherMap](https://openweathermap.org/), доставка сообщений — [Telegram Bot API](https://core.telegram.org/bots/api).

