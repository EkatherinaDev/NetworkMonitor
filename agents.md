# Agents Guide / Руководство для агентов

## Русский

### Роль файла

Этот файл описывает техническое устройство проекта для разработчиков и будущих кодовых агентов. Пользовательская документация находится в `README.md`.

### Стек

- Язык: C#.
- UI: Windows Forms.
- Target framework: `net8.0-windows`.
- Сборка: `dotnet publish` через `build.ps1`.
- Пакеты NuGet не используются.

### Основные файлы

- `NetworkMonitor/Form1.cs` - логика формы, обработчики кнопок, запуск сканирования, обновление таблицы.
- `NetworkMonitor/Form1.Designer.cs` - WinForms-разметка интерфейса.
- `NetworkMonitor/NetworkScanner.cs` - ICMP ping, поиск адресов локальной сети, reverse DNS, ARP, TCP-проверка портов, определение серверов.
- `NetworkMonitor/ServiceChecker.cs` - проверка сервисов через DNS IPv4 resolution и ping.
- `NetworkMonitor/DefaultServerEndpoint.cs` - модель строки вкладки `Сервера`.
- `NetworkMonitor/DefaultServerStore.cs` - загрузка и сохранение фиксированного списка серверов.
- `NetworkMonitor/default_servers.json` - опциональный локальный шаблон списка серверов; файл игнорируется Git и не должен содержать приватные адреса в репозитории.
- `NetworkMonitor/ServiceEndpoint.cs` - модель строки вкладки `Сервисы`.
- `NetworkMonitor/ServiceEndpointStore.cs` - загрузка и сохранение адресов сервисов.
- `NetworkMonitor/default_services.json` - дефолтный список сервисов, встраивается как ресурс и копируется в профиль пользователя.
- `NetworkMonitor/ManualIpStore.cs` - загрузка и сохранение ручных IP.
- `NetworkMonitor/NetworkDevice.cs` - модель строки таблицы.
- `NetworkMonitor/NetworkScanResult.cs` - результат сканирования и прогресс.
- `NetworkMonitor/AppInstaller.cs` - режим само-установки и удаления.
- `NetworkMonitor/Program.cs` - точка входа и выбор режима: установка или обычный запуск.
- `build.ps1` - публикация x64 и x86.

### Важные архитектурные детали

Для вкладки `Сеть` не используйте ping как критерий доступности. Статус строки должен определяться открытым TCP-портом `3389` через `TcpClient`. Остальные открытые порты показываются как детали. Для вкладки `Сервисы` ping допустим только через `Ping.SendPingAsync` из .NET, без `cmd`, `ping.exe`, batch-файлов или PowerShell.

Вкладка `Сервера` должна быть первой в `TabControl` и открываться при запуске приложения. Она читает фиксированный список из `%AppData%\NetworkMonitor\default_servers.json`. Этот файл создается из `NetworkMonitor/default_servers.json` рядом с приложением, из embedded resource или как пустой список только если файла еще нет. Сканирование сети, автообнаружение и `server_ips.json` не должны изменять `default_servers.json`. Изменение этого файла допускается только вручную пользователем или через контекстное меню вкладки `Сервера`.

Проверка на вкладке `Сервера` выполняется по всем IP в строке: ICMP ping через `Ping.SendPingAsync`, проверка TCP-порта `3389` и попытка получить RDP-сертификат. Строка считается `В сети`, если хотя бы один IP одновременно ответил на ping и имеет открытый `3389`. Сертификат используется как дополнительная диагностика и источник имени; отсутствие сертификата само по себе не должно делать сервер недоступным. В журнал нужно писать подробный результат по каждому IP.

`arp.exe -a` запускается только для чтения ARP-кэша и показа MAC-адресов. Не используйте ARP как критерий доступности устройства. ARP-кэш читается до и после проверки портов, чтобы заполнить MAC, появившийся после сетевого подключения; для IP за маршрутизатором реальный MAC удаленного сервера может быть недоступен.

`nmblookup` не должен быть внешней зависимостью приложения. Для получения имени сервера сначала читается RDP-сертификат на TCP-порту `3389`: `NetworkScanner` отправляет RDP Negotiation request, запускает TLS через `SslStream` и берет имя из сертификата. Если сертификат не дал имя, используются `ping.exe -a -n 1 -w 1000 <ip>`, `nbtstat.exe -A <ip>`, встроенный NetBIOS Node Status request по UDP/137 и reverse DNS. Имя нужно для отображения и группировки; статус online во вкладке `Сеть` ставится только при открытом `3389`.

Проверка серверных портов выполняется через `TcpClient`. Список портов и токены имен находятся в `NetworkScanner`. Не используйте любой открытый порт как признак типа `Сервер`: `80`, `443`, `3389`, `22`, `8080` и `8443` слишком общие и должны только отображаться как детали. Для типа `Сервер` используйте серверные токены имени или `ServerIdentityPorts`.

Строки таблицы `Сеть` строятся через `NetworkDeviceGroup`: IP-адреса с одинаковым известным именем показываются в одной строке. `Неизвестное устройство` не группируется по имени, чтобы разные неизвестные IP оставались отдельными строками. `_devices` в `Form1` остается словарем по IP, потому что автопроверка и ручные проверки должны работать с реальными адресами.

Контекстное меню `devicesGrid` должно содержать `Копировать`, `Редактировать`, `Удалить`, `Проверить`. Для `NetworkDeviceGroup` действия применяются ко всем IP внутри строки. Удаление убирает IP из текущей таблицы, `manual_ips.json` и `server_ips.json`; автоматически найденный IP может появиться снова после полного сканирования, если у него открыты порты.

Автоматическая проверка запускается WinForms-таймером в `Form1`: интервал 10 минут. Таймер должен проверять только строки вкладки `Сервера`, а не всю подсеть. Полный обход сети должен запускаться только вручную кнопкой `Сканировать всю сеть`.

Сканирование должно оставаться асинхронным. Не блокируйте UI-поток ожиданием ping, DNS, ARP или TCP-портов.

Журнал событий мониторинга находится в `Form1` и отображается через `eventLogListBox`. В журнал нужно писать запуск и завершение проверок, ошибки, найденные серверы и изменения статуса. `AddEventLog` должен писать каждую запись и в интерфейс, и в дневной UTF-8 файл `%AppData%\NetworkMonitor\logs\monitoring-YYYY-MM-DD.log`. В интерфейсе хранится не больше `MaxEventLogItems` записей; файловые логи должны сохраняться после перезапуска приложения. Ошибка записи в файл не должна прерывать мониторинг или обновление UI. Отдельная вкладка `Логи` читает эти файлы и по умолчанию показывает последние `MaxLogViewerItems` записей. Фильтры вкладки должны поддерживать имя/IP, текст, тип события, временной промежуток и режим `Только недоступные`.

При ручной проверке выбранной строки `devicesGrid` нужно логировать подробности по каждому IP из строки: заголовок `Проверка <имя>:` и отдельные записи `IP: В сети, порты: ...`, `IP: Недоступен, порты: ...` или `IP: Недоступен, открытых портов нет`. Для этого используется `Form1.AddDeviceCheckDetailsToEventLog`.

Вкладка `Сервисы` создается в `Form1.BuildTabbedLayout()`. Не ищите DNS-имя или IP сервиса обходом подсети: сервисный адрес должен быть введен пользователем или загружен из `%AppData%\NetworkMonitor\service_endpoints.json`. Автоматически определяется только IPv4 для уже заданного DNS-имени через `Dns.GetHostAddressesAsync`, затем адрес проверяется параллельно через `ServiceChecker`. Не запускайте `ping -4` через cmd; это только пользовательский аналог того, что делает код. Одна строка сервиса может содержать несколько DNS-имен или IP через `;`; `ServiceChecker` должен проверить кандидаты по очереди и вывести первый IPv4, который ответил на ping. После проверки сервисов нужно писать в журнал отдельную строку по каждому сервису через `FormatServiceCheckLog`: название, DNS/IP, найденный IPv4, статус и детали. Нельзя ограничиваться только итогом `в сети N, недоступно N`, потому что вкладка `Логи` должна находить недоступность конкретного сервиса по имени или IP.

Таблица сервисов должна быть read-only. Не возвращайте редактирование ячеек простым кликом. Верхняя строка вкладки `Сервисы` - это быстрая проверка по одному полю `DNS-имя или IP` и кнопке `Проверить`. Кнопка ищет существующую строку по названию, DNS/IP или найденному IPv4; если строки нет, создает новый `ServiceEndpoint`, где `Name` и `Address` равны введенному значению, сохраняет список и сразу запускает проверку. Редактирование, удаление, сканирование и копирование строки выполняются через контекстное меню правой кнопкой мыши. Отдельная кнопка `Сохранить адреса` не нужна: `ServiceEndpointStore.Save` вызывается после добавления, редактирования и удаления.

Контекстное меню `defaultServersGrid` должно содержать `Копировать`, `Добавить`, `Редактировать`, `Удалить`, `Проверить`. Добавление, редактирование и удаление сохраняют `%AppData%\NetworkMonitor\default_servers.json`. Двойной клик по строке проверяет все IP внутри этой строки.

В таблицах `defaultServersGrid`, `devicesGrid` и `servicesGrid`, а также в `eventLogListBox` должно работать копирование текста. Ctrl+C копирует выбранную строку или запись журнала. Правый клик по ячейке таблицы должен давать пункт `Копировать` для значения текущей ячейки.

### Хранение данных

Ручные IP хранятся здесь:

```text
%AppData%\NetworkMonitor\manual_ips.json
```

Формат:

```json
[
  "192.168.1.10"
]
```

Не храните этот файл рядом с EXE в `Program Files`, потому что обычный пользователь может не иметь прав на запись.

Фиксированный список серверов для вкладки `Сервера` и автоматической проверки каждые 10 минут хранится здесь:

```text
%AppData%\NetworkMonitor\default_servers.json
```

Опциональный локальный шаблон рядом с приложением:

```text
NetworkMonitor/default_servers.json
```

Формат:

```json
[
  {
    "name": "SERVER-NAME",
    "address": "192.168.1.10; 192.168.1.11"
  }
]
```

Файл обслуживается `DefaultServerStore`. `NetworkMonitor/default_servers.json` игнорируется Git, чтобы не публиковать приватные адреса. Если пользовательский файл уже существует, его нельзя автоматически сливать, пополнять или перезаписывать локальным шаблоном. Сканирование сети не должно вызывать `DefaultServerStore.Save`.

Известные серверные IP, найденные вкладкой `Сеть`, хранятся здесь:

```text
%AppData%\NetworkMonitor\server_ips.json
```

Формат такой же: JSON-массив строк. Файл обслуживается тем же `ManualIpStore`, но с другим именем файла.

Дефолтный список сервисов хранится здесь:

```text
%AppData%\NetworkMonitor\default_services.json
```

Источник дефолтов в репозитории:

```text
NetworkMonitor/default_services.json
```

Файл в репозитории встраивается в EXE как ресурс `NetworkMonitor.default_services.json`, потому установщик может оставаться одним EXE. `ServiceEndpointStore` при первом запуске создает файл дефолтов в профиле пользователя из файла рядом с приложением или из embedded resource. Не переносите дефолтные сервисы обратно в C#-массив.

Текущий рабочий список сервисов Covid19, Промед, DIGIPAX, RIS App, ARM EDN и Telemed хранится здесь:

```text
%AppData%\NetworkMonitor\service_endpoints.json
```

Файл обслуживается `ServiceEndpointStore`. Если файла еще нет, он создается из `default_services.json`. Текущие дефолты: `Covid19` - `covid19.vologdamed.local;10.35.0.66`, `Промед` - `rmisvo.cifromed35.ru`, `DIGIPAX` - `10.0.5.67`, `RIS App` - `172.24.3.11`, `ARM EDN` - `edn.vologdamed.local`, `Telemed` - `10.35.0.99`. Если рабочий файл уже есть, загрузка должна использовать сохраненный список, добавить недостающие дефолтные строки и заполнить пустые адреса стандартных сервисов из файла дефолтов.

### Установка

Опубликованный EXE работает в двух режимах:

- если имя содержит `Setup`, `Install` или `Installer`, запускается установка;
- иначе запускается обычное приложение.

Режим установки копирует EXE в `%ProgramFiles%\NetworkMonitor\NetworkMonitor.exe`, создает ярлык в меню Пуск и запись удаления в `HKLM`.

### Сборка и проверка

Команда проверки проекта:

```powershell
dotnet build .\NetworkMonitor\NetworkMonitor.csproj --configuration Release
```

Команда публикации:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Ожидаемые артефакты:

- `artifacts/NetworkMonitorSetup-x64.exe`
- `artifacts/NetworkMonitorSetup-x86.exe`

### Правила изменений

- Не используйте ping как критерий статуса на вкладке `Сеть`; доступность там определяется открытым TCP-портом `3389`.
- Не добавляйте зависимость от внешних сетевых утилит для ping на вкладке `Сервисы`.
- Не добавляйте зависимость от внешней утилиты `nmblookup`; для имени используйте RDP-сертификат на `3389`, затем `ping.exe -a <ip>`, `nbtstat.exe -A <ip>` и встроенный UDP/137 fallback.
- Не добавляйте запуск `ping -4` через cmd для сервисов; используйте DNS IPv4 resolution в `ServiceChecker`.
- Не запускайте долгие операции в UI-потоке.
- Не возвращайте автоматическое сканирование всей сети по таймеру; таймер должен проверять только вкладку `Сервера`.
- Не изменяйте `%AppData%\NetworkMonitor\default_servers.json` из результатов сканирования сети или автообнаружения серверов.
- Не меняйте формат `manual_ips.json` без обновления `ManualIpStore` и документации.
- Не меняйте формат `server_ips.json` без обновления `ManualIpStore` и документации.
- Если меняете серверные порты или критерии имени, обновите `README.md`.
- После изменения кода выполните `dotnet build`.

## English

### File Purpose

This file documents the project internals for developers and future coding agents. User-facing documentation is in `README.md`.

### Stack

- Language: C#.
- UI: Windows Forms.
- Target framework: `net8.0-windows`.
- Build: `dotnet publish` through `build.ps1`.
- No NuGet packages are used.

### Key Files

- `NetworkMonitor/Form1.cs` - form logic, button handlers, scan orchestration, table rendering.
- `NetworkMonitor/Form1.Designer.cs` - WinForms UI layout.
- `NetworkMonitor/NetworkScanner.cs` - ICMP ping, local target discovery, reverse DNS, ARP, TCP port checks, server detection.
- `NetworkMonitor/ServiceChecker.cs` - service checks through DNS IPv4 resolution and ping.
- `NetworkMonitor/DefaultServerEndpoint.cs` - row model for the `Сервера` tab.
- `NetworkMonitor/DefaultServerStore.cs` - fixed server list loading and saving.
- `NetworkMonitor/default_servers.json` - optional local server-list template; the file is ignored by Git and must not contain private addresses in the repository.
- `NetworkMonitor/ServiceEndpoint.cs` - row model for the `Сервисы` tab.
- `NetworkMonitor/ServiceEndpointStore.cs` - service address loading and saving.
- `NetworkMonitor/default_services.json` - default service list, embedded as a resource and copied to the user profile.
- `NetworkMonitor/ManualIpStore.cs` - manual IP loading and saving.
- `NetworkMonitor/NetworkDevice.cs` - table row model.
- `NetworkMonitor/NetworkScanResult.cs` - scan result and progress records.
- `NetworkMonitor/AppInstaller.cs` - self-install and uninstall mode.
- `NetworkMonitor/Program.cs` - entry point and mode selection.
- `build.ps1` - x64 and x86 publishing script.

### Important Architecture Notes

For the `Сеть` tab, do not use ping as the availability criterion. Row status must be determined by open TCP port `3389` through `TcpClient`. Other open ports are displayed as details. For the `Сервисы` tab, ping is allowed only through .NET `Ping.SendPingAsync`, without `cmd`, `ping.exe`, batch files, or PowerShell.

The `Сервера` tab must be the first `TabControl` tab and open when the application starts. It reads the fixed list from `%AppData%\NetworkMonitor\default_servers.json`. This file is created from `NetworkMonitor/default_servers.json` next to the application, from the embedded resource, or as an empty list only when the user-profile file does not exist. Network scans, automatic discovery, and `server_ips.json` must not change `default_servers.json`. This file may be changed only manually by the user or through the `Сервера` tab context menu.

Checks on the `Сервера` tab run for every IP address in a row: ICMP ping through `Ping.SendPingAsync`, TCP port `3389` check, and an attempt to retrieve the RDP certificate. A row is marked `В сети` when at least one IP both replies to ping and has `3389` open. The certificate is used as extra diagnostics and a hostname source; failure to read it must not by itself make the server unavailable. The event log should include a detailed result for every IP address.

`arp.exe -a` is launched only to read the Windows ARP cache and display MAC addresses. Do not use ARP as the source of truth for device availability. The ARP cache is read before and after port checks to fill MAC addresses that appear after network connections; for IP addresses behind a router, the remote server's real MAC address may not be available.

`nmblookup` must not be an external application dependency. Server name lookup first reads the RDP certificate on TCP port `3389`: `NetworkScanner` sends an RDP Negotiation request, starts TLS through `SslStream`, and takes the name from the certificate. If the certificate gives no name, it uses `ping.exe -a -n 1 -w 1000 <ip>`, `nbtstat.exe -A <ip>`, the built-in NetBIOS Node Status request over UDP/137, and reverse DNS. The name is used for display and grouping; online status on the `Сеть` tab requires port `3389` to be open.

Server port checks are performed through `TcpClient`. The port list and hostname tokens are defined in `NetworkScanner`. Do not use any open port as a `Сервер` type signal: `80`, `443`, `3389`, `22`, `8080`, and `8443` are too generic and should only be displayed as details. For the `Сервер` type, use server-like hostname tokens or `ServerIdentityPorts`.

Rows in the `Сеть` table are built through `NetworkDeviceGroup`: IP addresses with the same known hostname are displayed in one row. `Неизвестное устройство` is not grouped by name, so unrelated unknown IP addresses remain separate rows. `_devices` in `Form1` remains keyed by IP because automatic and manual checks still need real addresses.

The `devicesGrid` context menu should contain `Копировать`, `Редактировать`, `Удалить`, `Проверить`. For a `NetworkDeviceGroup`, actions apply to all IP addresses inside the row. Delete removes IP addresses from the current table, `manual_ips.json`, and `server_ips.json`; an automatically discovered IP can appear again after a full scan if it has open ports.

Automatic checking is started by a WinForms timer in `Form1`: the interval is 10 minutes. The timer must check only rows from the `Сервера` tab, not the whole subnet. Full network enumeration must be started only manually by the `Сканировать всю сеть` button.

Scanning must remain asynchronous. Do not block the UI thread while waiting for ping, DNS, ARP, or TCP port checks.

The monitoring event log is owned by `Form1` and displayed through `eventLogListBox`. It should record check starts and finishes, errors, discovered servers, and status changes. `AddEventLog` must write each entry both to the UI and to the daily UTF-8 file `%AppData%\NetworkMonitor\logs\monitoring-YYYY-MM-DD.log`. The UI keeps at most `MaxEventLogItems` entries; file logs must persist across application restarts. File logging failures must not interrupt monitoring or UI updates. The separate `Логи` tab reads these files and shows the latest `MaxLogViewerItems` entries by default. The tab filters should support name/IP, text, event type, time range, and `Только недоступные` mode.

When a selected `devicesGrid` row is checked manually, log details for each IP address in that row: a `Проверка <name>:` header and separate `IP: В сети, порты: ...`, `IP: Недоступен, порты: ...`, or `IP: Недоступен, открытых портов нет` entries. This is handled by `Form1.AddDeviceCheckDetailsToEventLog`.

The `Сервисы` tab is created in `Form1.BuildTabbedLayout()`. Do not discover service DNS names or IP addresses through subnet scanning: service addresses must be entered by the user or loaded from `%AppData%\NetworkMonitor\service_endpoints.json`. Only IPv4 resolution for an already configured DNS name is automatic through `Dns.GetHostAddressesAsync`; the result is checked in parallel through `ServiceChecker`. Do not run `ping -4` through cmd; it is only the user-facing equivalent of what the code does. A single service row may contain several DNS names or IP addresses separated by `;`; `ServiceChecker` should try candidates in order and display the first IPv4 address that replies to ping. After service checks, write one log entry per service through `FormatServiceCheckLog`: service name, DNS/IP, resolved IPv4, status, and details. Do not log only the summary `online N, unavailable N`, because the `Логи` tab must be able to find a specific service outage by name or IP.

The services table must be read-only. Do not bring back single-click cell editing. The top row of the `Сервисы` tab is a quick check row with one `DNS-имя или IP` field and a `Проверить` button. The button searches existing rows by name, DNS/IP, or resolved IPv4; if no row exists, it creates a new `ServiceEndpoint` where `Name` and `Address` are both the entered value, saves the list, and immediately checks it. Edit, delete, scan, and copy actions are done through the right-click context menu. A separate `Сохранить адреса` button is not needed: call `ServiceEndpointStore.Save` after add, edit, and delete actions.

The `defaultServersGrid` context menu should contain `Копировать`, `Добавить`, `Редактировать`, `Удалить`, `Проверить`. Add, edit, and delete actions save `%AppData%\NetworkMonitor\default_servers.json`. Double-clicking a row checks every IP address inside that row.

Text copying should work in `defaultServersGrid`, `devicesGrid`, `servicesGrid`, and `eventLogListBox`. Ctrl+C copies the selected row or log entry. Right-clicking a table cell should expose a `Копировать` item for the current cell value.

### Data Storage

Manual IP addresses are stored here:

```text
%AppData%\NetworkMonitor\manual_ips.json
```

Format:

```json
[
  "192.168.1.10"
]
```

Do not store this file next to the EXE under `Program Files`, because normal users may not have write permission there.

The fixed server list for the `Сервера` tab and the 10-minute automatic check is stored here:

```text
%AppData%\NetworkMonitor\default_servers.json
```

Optional local template next to the application:

```text
NetworkMonitor/default_servers.json
```

Format:

```json
[
  {
    "name": "SERVER-NAME",
    "address": "192.168.1.10; 192.168.1.11"
  }
]
```

The file is managed by `DefaultServerStore`. `NetworkMonitor/default_servers.json` is ignored by Git to avoid publishing private addresses. If the user-profile file already exists, do not automatically merge, populate, or overwrite it with a local template. Network scans must not call `DefaultServerStore.Save`.

Known server IP addresses discovered by the `Сеть` tab are stored here:

```text
%AppData%\NetworkMonitor\server_ips.json
```

The format is the same: a JSON string array. The same `ManualIpStore` class manages it with a different file name.

The default service list is stored here:

```text
%AppData%\NetworkMonitor\default_services.json
```

The default source in the repository:

```text
NetworkMonitor/default_services.json
```

The repository file is embedded into the EXE as `NetworkMonitor.default_services.json`, so the installer can remain a single EXE. On first run, `ServiceEndpointStore` creates the user-profile default file from a file next to the application or from the embedded resource. Do not move default services back into a C# array.

The current working service list for Covid19, Промед, DIGIPAX, RIS App, ARM EDN, and Telemed is stored here:

```text
%AppData%\NetworkMonitor\service_endpoints.json
```

The file is managed by `ServiceEndpointStore`. If the file does not exist yet, it is created from `default_services.json`. Current defaults are: `Covid19` - `covid19.vologdamed.local;10.35.0.66`, `Промед` - `rmisvo.cifromed35.ru`, `DIGIPAX` - `10.0.5.67`, `RIS App` - `172.24.3.11`, `ARM EDN` - `edn.vologdamed.local`, `Telemed` - `10.35.0.99`. If the working file already exists, loading should use the saved list, add missing default rows, and fill empty standard-service addresses from the defaults file.

### Installation

The published EXE has two modes:

- if the filename contains `Setup`, `Install`, or `Installer`, installer mode starts;
- otherwise the regular application starts.

Installer mode copies the EXE to `%ProgramFiles%\NetworkMonitor\NetworkMonitor.exe`, creates a Start Menu shortcut, and registers uninstall information under `HKLM`.

### Build and Verification

Build check:

```powershell
dotnet build .\NetworkMonitor\NetworkMonitor.csproj --configuration Release
```

Publish:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Expected artifacts:

- `artifacts/NetworkMonitorSetup-x64.exe`
- `artifacts/NetworkMonitorSetup-x86.exe`

### Change Rules

- Do not use ping as the status criterion on the `Сеть` tab; availability there is determined by open TCP port `3389`.
- Do not add an external network utility dependency for ping on the `Сервисы` tab.
- Do not add an external `nmblookup` dependency; use the RDP certificate on `3389`, then `ping.exe -a <ip>`, `nbtstat.exe -A <ip>`, and the built-in UDP/137 fallback for names.
- Do not launch `ping -4` through cmd for service checks; use DNS IPv4 resolution in `ServiceChecker`.
- Do not run long operations on the UI thread.
- Do not bring back automatic full-network scans on the timer; the timer must check only the `Сервера` tab.
- Do not modify `%AppData%\NetworkMonitor\default_servers.json` from network scan results or automatic server discovery.
- Do not change the `manual_ips.json` format without updating `ManualIpStore` and the docs.
- Do not change the `server_ips.json` format without updating `ManualIpStore` and the docs.
- If server ports or hostname criteria change, update `README.md`.
- Run `dotnet build` after code changes.
