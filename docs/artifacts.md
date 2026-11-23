# Артефакты и протоколы обмена

УП.02, вариант 13, работа 3, занятие 6.

| Артефакт | Путь или назначение | Политика хранения |
|---|---|---|
| Решение, проекты и настройки запуска | FurnitureWorkshop.slnx, *.csproj, FurnitureWorkshop.slnLaunch, Directory.Build.props | Git |
| Исходный код | FurnitureWorkshop.*/ | Git |
| Миграции SQLite | FurnitureWorkshop.Data/Migrations/ | Git |
| Документация, схемы и шаблоны задач | docs/, README.md, .github/ | Git |
| Тесты и сценарий проверки API | FurnitureWorkshop.Tests/ и FurnitureWorkshop.UiTests/, Smoke-Api.ps1 | Git |
| База заказов | %LOCALAPPDATA%/FurnitureWorkshop/workshop.db | Локальные данные |
| Сборки и рабочие файлы Visual Studio | bin/, obj/, .vs/ | Игнорируются |
| Результаты тестов и профилирования | TestResults/, coveragereport/, BenchmarkDotNet.Artifacts/ | Игнорируются |
| Журналы и выгрузки | logs/, exports/, publish/ | Игнорируются |
| Пароли демонстрационных аккаунтов | Передаются отдельно от исходного кода | В Git хранятся только хеши для начальной базы |
| Отчёт и дневник | Word-документ в рабочей папке практики | Передаются преподавателю отдельно |

WPF обращается к API по HTTP на локальном интерфейсе. Структура запросов — REST, данные передаются в JSON. Токен передаётся как Bearer JWT. Ошибки валидации и конфликты оформляются по модели ProblemDetails. Импорт использует CSV, экспорт поддерживает CSV, JSON и XML. Переход на HTTPS запланирован работой 28.

См. [методы и форматы API](api-spec.md) и [зависимости модулей](architecture.md).
