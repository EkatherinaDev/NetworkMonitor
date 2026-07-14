# Network Monitor / Мониторинг сети

## Русский

### Назначение

Network Monitor - Windows-приложение для контроля доступности устройств в локальной IPv4-сети. Программа автоматически проверяет известные серверы каждые 10 минут, показывает найденные устройства в таблице, выделяет вероятные серверы и позволяет вручную добавить или проверить конкретный IP-адрес. Полное сканирование всей сети запускается только вручную, чтобы снизить нагрузку на сеть.

### Как работает проверка IP

Приложение не запускает `cmd.exe`, `ping.exe`, `.bat` или `.ps1` для проверки доступности IP во вкладке `Сеть`. Доступность устройств в этой таблице определяется по открытым TCP-портам через `TcpClient`.

Алгоритм вкладки `Сеть`: найти IP, проверить открытые порты, получить имя, сгруппировать IP с одинаковым именем в одну строку. Если у IP найден хотя бы один проверяемый открытый порт, строка считается `В сети`. Если открытых портов нет, строка считается `Недоступен`.

NetBIOS-проверка имени аналогична `nmblookup -A <ip>`, но реализована внутри приложения на C#, без внешней утилиты `nmblookup`. Она нужна для получения имени сервера и группировки строк, а не как единственный критерий доступности.

`nmblookup` не запускается как внешний файл. Для проверки имени сервера приложение само отправляет NetBIOS Node Status request на UDP-порт 137 нужного IP-адреса и разбирает ответ.

### Как выбираются адреса для сканирования

При сканировании приложение читает активные сетевые интерфейсы Windows через `NetworkInterface.GetAllNetworkInterfaces()`. Для каждого IPv4-адреса и маски вычисляется подсеть, после чего формируется список IP-адресов для проверки портов.

Если подсеть слишком большая, приложение ограничивает перебор ближайшим `/24` диапазоном, чтобы не запускать слишком тяжелое сканирование. В таблицу автоматического сканирования попадают IP, у которых найден открытый проверяемый порт. Адреса, добавленные вручную, всегда проверяются и отображаются дополнительно, даже если открытых портов не найдено.

### Автоматическое и ручное сканирование

Автоматическая проверка запускается таймером WinForms каждые 10 минут, но проверяются только известные IP серверов. Список серверов пополняется после ручного полного сканирования сети и после ручной проверки IP, если устройство определено как сервер.

Кнопка `Сканировать всю сеть` запускает полный обход локальной сети вручную. Во время проверки интерфейс не блокируется: проверка портов выполняется параллельно, до 32 IP одновременно.

Двойной щелчок по строке таблицы проверяет выбранную строку. Если в строке несколько IP с одинаковым именем, проверяются все IP из этой строки. Кнопка `Проверить IP` проверяет адрес из поля ввода. Кнопка `Добавить` сохраняет IP в список ручных адресов и сразу проверяет его.

Правый клик по строке вкладки `Сеть` открывает контекстное меню: `Копировать`, `Редактировать`, `Удалить`, `Проверить`. Для объединенной строки редактирование, удаление и проверка применяются ко всем IP-адресам внутри этой строки.

Текст из таблиц можно копировать как в обычных Windows-приложениях: Ctrl+C копирует выбранную строку, а правый клик по ячейке открывает пункт `Копировать` для конкретного значения.

В нижней части окна есть `Журнал событий мониторинга`. В него пишутся запуск и завершение проверок, найденные серверы, ручные проверки, ошибки и изменения статуса устройств.

При проверке выбранной строки таблицы журнал пишет подробный результат по каждому IP из этой строки. Формат: сначала `Проверка <имя>:`, затем отдельные строки вида `10.0.5.68: В сети, порты: 80, 389` или `192.170.1.7: Недоступен, открытых портов нет`.

### Как определяется имя устройства

Для найденных IP программа сначала пытается получить имя через NetBIOS Node Status request по UDP/137, затем делает reverse DNS lookup через `Dns.GetHostEntryAsync`. Если DNS возвращает полное имя, в таблице показывается короткая часть до первой точки. Если имя определить не удалось, выводится `Неизвестное устройство`.

Если несколько IP-адресов возвращают одинаковое имя, они показываются в одной строке таблицы: `имя - IP-адреса - MAC-адреса - открытые порты - статус - ...`. IP без определенного имени не объединяются между собой, чтобы разные неизвестные устройства не смешивались в одну строку.

### Как определяется MAC-адрес

MAC-адрес берется из ARP-кэша Windows. Для этого приложение запускает системную команду:

```powershell
arp.exe -a
```

Затем вывод разбирается регулярным выражением. Это не используется для ping-проверки; `arp.exe` нужен только для отображения MAC-адреса уже известных соседних устройств.

### Как определяются серверы

Сервер определяется эвристически. Устройство считается вероятным сервером, если выполняется одно из условий:

- имя содержит признаки сервера: `server`, `srv`, `dc`, `sql`, `db`, `1c`, `ksc`, `mail`, `exchange`, `nas`, `storage`, `backup`, `terminal`, `rdp`, `web`, `app`;
- открыт один из проверяемых TCP-портов: `22`, `25`, `53`, `80`, `110`, `143`, `389`, `443`, `465`, `587`, `636`, `993`, `995`, `1433`, `1521`, `3306`, `3389`, `5432`, `8080`, `8443`.

Проверка портов выполняется через `TcpClient`, без запуска внешних утилит. Таймаут подключения к одному порту - 300 мс.

Вероятные серверы сортируются вверху таблицы и выделяются жирным шрифтом.

Статус `В сети` ставится только при наличии хотя бы одного открытого проверяемого TCP-порта. Это защищает от ситуации, когда зависший сервер продолжает отвечать на ICMP ping, но рабочие сетевые службы уже не отвечают.

### Где хранятся IP-адреса

IP-адреса, добавленные вручную, хранятся в JSON-файле профиля пользователя:

```text
%AppData%\NetworkMonitor\manual_ips.json
```

Файл содержит обычный массив строк:

```json
[
  "192.168.1.10",
  "192.168.1.20"
]
```

При сохранении адреса нормализуются, дубликаты удаляются, список сортируется по IP.

IP-адреса серверов, которые нужно проверять автоматически каждые 10 минут, хранятся отдельно:

```text
%AppData%\NetworkMonitor\server_ips.json
```

Этот файл также содержит JSON-массив строк. Он пополняется автоматически, когда приложение определяет устройство как сервер.

### Проверка сервисов

Во вкладке `Сервисы` есть отдельная проверка для:

- `Covid19`;
- `Промед`;
- `DIGIPAX`;
- `RIS App`;
- `ARM EDN`;
- `Telemed`.

При первом запуске создается начальный список сервисов из файла `default_services.json`:

- `Covid19` - `covid19.vologdamed.local;10.35.0.66`;
- `Промед` - `rmisvo.cifromed35.ru`;
- `DIGIPAX` - `10.0.5.67`;
- `RIS App` - `172.24.3.11`;
- `ARM EDN` - `edn.vologdamed.local`;
- `Telemed` - `10.35.0.99`.

В верхней части вкладки `Сервисы` есть строка быстрой проверки: `DNS-имя или IP` и кнопка `Проверить`.

Кнопка `Проверить` ищет введенный DNS/IP в списке сервисов. Если строка уже есть, приложение проверяет ее повторно. Если строки нет, приложение добавляет ее в `service_endpoints.json`, использует введенный DNS/IP как название строки и сразу проверяет доступность.

Таблица сервисов доступна только для просмотра. Чтобы изменить строку, нажмите по ней правой кнопкой мыши и выберите `Редактировать`, `Удалить`, `Сканировать` или `Копировать`. Отдельной кнопки сохранения нет: список сохраняется автоматически после добавления, редактирования или удаления.

В одной строке можно указать несколько DNS-имен или IP через `;`; приложение проверит их по очереди и покажет первый IPv4, который ответил на ping.

Дефолтный список сервисов хранится отдельно от кода:

```text
%AppData%\NetworkMonitor\default_services.json
```

Рабочий список сервисов, который пользователь редактирует в приложении, хранится отдельно:

```text
%AppData%\NetworkMonitor\service_endpoints.json
```

DNS-имя или IP сервиса не определяется автоматически сканированием сети: его нужно задать в таблице или загрузить из сохраненного файла. Автоматически определяется только IPv4 для заданного DNS-имени. Это программный аналог команды `ping -4 имя_сервиса`: приложение делает DNS-запрос через `Dns.GetHostAddressesAsync`, выбирает IPv4-адрес и затем проверяет его ping-запросом через `Ping.SendPingAsync`.

Все сервисы проверяются параллельно через `Parallel.ForEachAsync` с ограничением до 8 одновременных проверок. Это похоже на `Task.WhenAll`, но позволяет не создавать лишнюю нагрузку на сеть.

### Установка и удаление

Подробная инструкция для компьютеров пользователей находится в [INSTALL_RU.md](INSTALL_RU.md).

Готовые файлы находятся в папке `artifacts`:

- `NetworkMonitorSetup-x64.exe`
- `NetworkMonitorSetup-x86.exe`

Если EXE-файл содержит в имени `Setup`, `Install` или `Installer`, приложение запускается в режиме установки. Оно запрашивает права администратора, копирует себя в:

```text
%ProgramFiles%\NetworkMonitor\NetworkMonitor.exe
```

Также создается ярлык в общем меню Пуск и запись удаления в реестре Windows:

```text
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NetworkMonitor
```

Исходный код пользователям не передается. Пользователь получает один самодостаточный EXE-файл нужной разрядности.

### Сборка

Требуется .NET SDK 8 или новее с поддержкой Windows Desktop.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Скрипт собирает две self-contained single-file версии:

- `win-x64`
- `win-x86`

### Запуск для разработки

```powershell
dotnet run --project .\NetworkMonitor\NetworkMonitor.csproj
```

## English

### Purpose

Network Monitor is a Windows desktop application for checking device availability in a local IPv4 network. It automatically checks known servers every 10 minutes, displays discovered devices in a table, moves probable servers to the top, and allows users to add or check specific IP addresses manually. Full network scans are manual only to reduce network load.

### How IP Checks Work

The application does not run `cmd.exe`, `ping.exe`, `.bat`, or `.ps1` files to check IP availability on the `Сеть` tab. Device availability in this table is determined by open TCP ports through `TcpClient`.

The `Сеть` tab algorithm is: find IP addresses, check open ports, resolve names, then group IP addresses with the same name into one row. If an IP has at least one checked TCP port open, the row is marked online. If no checked port is open, it is marked unavailable.

The NetBIOS name check is equivalent to the idea of `nmblookup -A <ip>`, but it is implemented inside the C# application, without requiring the external `nmblookup` utility. It is used to obtain server names and group rows, not as the only availability criterion.

`nmblookup` is not started as an external executable. For server name verification, the application sends its own NetBIOS Node Status request to UDP port 137 of the target IP and parses the response.

### How Scan Targets Are Selected

During a scan, the application reads active Windows network interfaces by using `NetworkInterface.GetAllNetworkInterfaces()`. For each IPv4 address and subnet mask, it calculates the subnet and builds a list of IP addresses for port checks.

If a subnet is too large, enumeration is limited to the nearest `/24` range to avoid an overly heavy scan. Automatic scan results include IP addresses that have at least one checked TCP port open. Manually added IP addresses are always checked and displayed additionally, even when no checked port is open.

### Automatic and Manual Scanning

Automatic checking is started by a WinForms timer every 10 minutes, but only known server IP addresses are checked. The server list is populated after a manual full network scan and after a manual IP check when the device is detected as a server.

The `Сканировать всю сеть` button starts a full local network scan manually. The UI remains responsive because port checks run in parallel, up to 32 IP addresses at a time.

Double-clicking a table row checks that row. If the row contains several IP addresses with the same hostname, all IP addresses in that row are checked. The `Проверить IP` button checks the address from the input field. The `Добавить` button saves the IP to the manual list and checks it immediately.

Right-clicking a row on the `Сеть` tab opens a context menu: `Копировать`, `Редактировать`, `Удалить`, `Проверить`. For a grouped row, edit, delete, and check actions apply to all IP addresses inside that row.

The lower part of the window contains the monitoring event log. It records scan starts and finishes, discovered servers, manual checks, errors, and device status changes.

When a selected table row is checked, the event log records a detailed result for each IP address in that row. The format is `Проверка <name>:` followed by lines such as `10.0.5.68: В сети, порты: 80, 389` or `192.170.1.7: Недоступен, открытых портов нет`.

Table text can be copied like in regular Windows applications: Ctrl+C copies the selected row, and right-clicking a cell opens a `Копировать` item for that exact value.

### Hostname Detection

For discovered IP addresses, the application first tries a NetBIOS Node Status request over UDP/137, then performs a reverse DNS lookup by using `Dns.GetHostEntryAsync`. If DNS returns a full hostname, only the short name before the first dot is displayed. If no name can be resolved, the UI shows `Неизвестное устройство`.

If several IP addresses return the same hostname, they are displayed in one table row: `name - IP addresses - MAC addresses - open ports - status - ...`. IP addresses without a resolved hostname are not grouped together, so unrelated unknown devices are not mixed into one row.

### MAC Address Detection

MAC addresses are read from the Windows ARP cache. For this part only, the application starts the system command:

```powershell
arp.exe -a
```

The output is parsed with a regular expression. This is not used for ping checks; `arp.exe` is used only to display MAC addresses for known neighboring devices.

### Server Detection

Server detection is heuristic. A device is treated as a probable server when at least one condition is true:

- the hostname contains server-like tokens: `server`, `srv`, `dc`, `sql`, `db`, `1c`, `ksc`, `mail`, `exchange`, `nas`, `storage`, `backup`, `terminal`, `rdp`, `web`, `app`;
- one of the checked TCP ports is open: `22`, `25`, `53`, `80`, `110`, `143`, `389`, `443`, `465`, `587`, `636`, `993`, `995`, `1433`, `1521`, `3306`, `3389`, `5432`, `8080`, `8443`.

Port checks are performed through `TcpClient`, without external tools. The connection timeout for one port is 300 ms.

Probable servers are sorted to the top of the table and displayed in bold.

The `В сети` status is set only when at least one checked TCP port is open. This avoids treating a hung server as online just because it still replies to ICMP ping while its network services no longer respond.

### IP Storage

Manually added IP addresses are stored in the current user's profile:

```text
%AppData%\NetworkMonitor\manual_ips.json
```

The file contains a plain JSON string array:

```json
[
  "192.168.1.10",
  "192.168.1.20"
]
```

Before saving, addresses are normalized, duplicates are removed, and the list is sorted by IP.

Server IP addresses used by the 10-minute automatic check are stored separately:

```text
%AppData%\NetworkMonitor\server_ips.json
```

This file is also a JSON string array. It is updated automatically when the application identifies a device as a server.

### Service Checks

The `Сервисы` tab contains a separate check for:

- `Covid19`;
- `Промед`;
- `DIGIPAX`;
- `RIS App`;
- `ARM EDN`;
- `Telemed`.

On first run, the application creates the initial service list from `default_services.json`:

- `Covid19` - `covid19.vologdamed.local;10.35.0.66`;
- `Промед` - `rmisvo.cifromed35.ru`;
- `DIGIPAX` - `10.0.5.67`;
- `RIS App` - `172.24.3.11`;
- `ARM EDN` - `edn.vologdamed.local`;
- `Telemed` - `10.35.0.99`.

The top of the `Сервисы` tab has a quick check row: `DNS-имя или IP` and `Проверить`.

The `Проверить` button searches for the entered DNS/IP in the service list. If the row already exists, the application checks it again. If it does not exist yet, the application adds it to `service_endpoints.json`, uses the entered DNS/IP as the row name, and immediately checks availability.

The services table is read-only. To change a row, right-click it and choose `Редактировать`, `Удалить`, `Сканировать`, or `Копировать`. There is no separate save button: the list is saved automatically after add, edit, or delete actions.

One row may contain several DNS names or IP addresses separated by `;`; the application tries them in order and displays the first IPv4 address that replies to ping.

The default service list is stored separately from code:

```text
%AppData%\NetworkMonitor\default_services.json
```

The working service list edited by the user is stored separately:

```text
%AppData%\NetworkMonitor\service_endpoints.json
```

Service DNS names or IP addresses are not discovered automatically through subnet scanning: they must be entered in the table or loaded from the saved file. Only IPv4 resolution for a configured DNS name is automatic. This is the programmatic equivalent of `ping -4 service_name`: the application uses `Dns.GetHostAddressesAsync`, selects an IPv4 address, and then checks that IP with `Ping.SendPingAsync`.

All service checks run in parallel through `Parallel.ForEachAsync` with a limit of 8 concurrent checks. This is similar to `Task.WhenAll`, but keeps network load bounded.

### Installation and Uninstallation

Built executables are placed in the `artifacts` directory:

- `NetworkMonitorSetup-x64.exe`
- `NetworkMonitorSetup-x86.exe`

If the EXE filename contains `Setup`, `Install`, or `Installer`, the application starts in installer mode. It requests administrator rights and copies itself to:

```text
%ProgramFiles%\NetworkMonitor\NetworkMonitor.exe
```

It also creates a Start Menu shortcut and an uninstall registry entry:

```text
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NetworkMonitor
```

End users receive one self-contained EXE for the required architecture. Source code is not distributed to users.

### Build

.NET SDK 8 or newer with Windows Desktop support is required.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The script builds two self-contained single-file versions:

- `win-x64`
- `win-x86`

### Development Run

```powershell
dotnet run --project .\NetworkMonitor\NetworkMonitor.csproj
```
