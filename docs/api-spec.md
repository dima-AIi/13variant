# Спецификация REST API

УП.02, вариант 13, работа 3, занятие 6.

Отдельно запущенный API использует `http://127.0.0.1:5130`. При запуске из WPF сервер выбирает свободный локальный порт. JSON-идентификаторы полей записываются в camelCase. Защищённые операции принимают заголовок `Authorization: Bearer <token>`. Обозначения ролей: **А** — администратор, **М** — менеджер, **С** — сборщик.

| Метод и маршрут | Доступ | Запрос | Успех | Основные отказы |
|---|---|---|---|---|
| GET /health | Все | — | 200, {"status":"ok"} | 503, ProblemDetails «База данных недоступна» |
| POST /api/auth/login | Все | login, password | 200, token и user | 400, 401, 429 |
| GET /api/auth/me | А, М, С | — | 200, данные пользователя | 401 |
| POST /api/auth/logout | А, М, С | — | 204 | 401 |
| GET /api/customers | А, М | — | 200, карточки заказчиков | 401, 403 |
| POST /api/customers | А, М | name, phone, email | 201, id | 400, 401, 403 |
| PUT /api/customers/{id} | А, М | name, phone, email | 204 | 400, 401, 403, 404 |
| POST /api/customers/import | А, М | UTF-8 CSV, text/csv | 200, число imported | 400, 401, 403, 413 |
| GET /api/users | А | — | 200, учётные записи | 401, 403 |
| GET /api/users/assemblers | А, М | — | 200, сборщики | 401, 403 |
| GET /api/orders | А, М, С | status и q в строке запроса, оба необязательны | 200, заказы | 400, 401 |
| GET /api/orders/{id} | А, М, С | id в пути | 200, карточка заказа | 401, 404 |
| POST /api/orders | А, М | customerId, title, address, notes, assemblerId, amount, deliveryDate | 201, id | 400, 401, 403 |
| PUT /api/orders/{id} | А, М | Поля заказа и version | 204 | 400, 401, 403, 404, 409 |
| POST /api/orders/{id}/transition | А, М, С | status, reason, version | 204 | 400, 401, 404, 409 |
| GET /api/orders/statistics | А, М, С | — | 200, показатели и byStatus | 401 |
| GET /api/orders/export/{format} | А, М | csv, json или xml, необязательные status и q | 200, файл | 400, 401, 403 |

Сборщик получает свои заказы, включая личную статистику. Запрос чужой карточки или перехода чужого заказа получает 404. Числовые значения денежных полей в JSON записываются с точкой, даты доставки — `yyyy-MM-dd`. Статусы в протоколе: Measurement, Design, Manufacturing, Delivery, Installed, Cancelled.

## Пример предметной ошибки

Для заказа № 12 с текущей версией 2 в статусе «Проект» сборщик ещё не назначен.

```http
POST /api/orders/12/transition HTTP/1.1
Content-Type: application/json
Authorization: Bearer <token>

{"status":"Manufacturing","reason":null,"version":2}
```

```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{"title":"Проверьте данные","status":400,"detail":"Перед изготовлением назначьте сборщика."}
```

Показаны поля ответа, проверяемые клиентом. Сервер может добавить ссылку на тип ошибки. Код 409 при изменении заказа означает, что переданная версия устарела. Лимитер входа возвращает HTTP 429 при достижении лимита.

## CSV заказчиков и выгрузка заказов

Файл импорта — UTF-8, заголовок `Name,Phone,Email` или `Name;Phone;Email`. Имена колонок сравниваются без учёта регистра. Поля с разделителем можно заключать в кавычки. Максимум — 1 МиБ и 5 000 записей.

Если отдельные записи некорректны, API возвращает 400 и массив `errors` с номером строки `line` и пояснением `message`. Сохранение выполняется после проверки всего файла. См. [тестовый файл](examples/customers-check.csv) и [проверки API](api-tests.md).

```csv
Name,Phone,Email
Анна Орлова,+79000001301,anna@example.test
```

| Входная колонка | Поле заказчика | Правило |
|---|---|---|
| Name | Name | Обязательное значение, до 150 символов |
| Phone | Phone | Обязательное значение, до 40 символов |
| Email | Email | Допускается пустое значение, заполненный адрес проверяется |

JSON выгрузки — массив OrderInfo. XML содержит корневой элемент Orders и вложенные элементы Order. CSV выгрузки использует UTF-8 с BOM и точку с запятой. Заголовок CSV приведён в порядке колонок:

```text
Id;CustomerId;Customer;Phone;Title;Address;Notes;AssemblerId;Assembler;Status;Amount;DeliveryDate;CancellationReason;CreatedAt;CompletedAt;Version
```

Выгрузка применяет заданные фильтры status и q. Значения CSV заключаются в кавычки, кавычки внутри поля удваиваются.
