# Network Monitor / Мониторинг сети

## Русский

### Назначение

Network Monitor - Windows-приложение для контроля доступности устройств в локальной IPv4-сети. Программа автоматически проверяет серверы из отдельного файла `default_servers.json` каждые 10 минут, показывает найденные устройства в таблице, выделяет вероятные серверы и позволяет вручную добавить или проверить конкретный IP-адрес. Полное сканирование всей сети запускается только вручную, чтобы снизить нагрузку на сеть.

### Вкладка `Сервера`

Вкладка `Сервера` расположена первой и открывается сразу при запуске приложения. Она предназначена для фиксированного списка важных серверов, который не зависит от результатов сканирования сети.

При первом запуске приложение создает файл:

```text
%AppData%\NetworkMonitor\default_servers.json
```

В публичном репозитории список реальных серверов не хранится. Если рядом с приложением есть локальный `default_servers.json`, он используется как шаблон при первом запуске; если шаблона нет, пользователь заполняет список через контекстное меню вкладки.

Этот файл не перезаписывается после сканирования сети и не пополняется автоматически. Его можно изменить только вручную в профиле пользователя или через контекстное меню вкладки `Сервера`: `Копировать`, `Добавить`, `Редактировать`, `Удалить`, `Проверить`.

Проверка сервера выполняется по всем IP в строке. Для каждого IP приложение отправляет ICMP ping через `Ping.SendPingAsync` и пытается получить RDP-сертификат на TCP-порту `3389`. Строка считается `В сети`, если хотя бы один IP отвечает на ping и отдает сертификат `3389`. В журнал событий пишется подробный результат по каждому IP.

### Как работает проверка IP

Приложение не запускает `cmd.exe`, `ping.exe`, `.bat` или `.ps1` для проверки доступности IP во вкладке `Сеть`. Доступность устройств в этой таблице определяется по открытым TCP-портам через `TcpClient`.

Алгоритм вкладки `Сеть`: найти IP, проверить открытые порты, получить имя, сгруппировать IP с одинаковым именем в одну строку. Строка считается `В сети`, если у IP открыт TCP-порт `3389`. Остальные открытые порты показываются как детали, но не являются критерием статуса.

Название сервера программа сначала пытается получить из RDP-сертификата на TCP-порту `3389`. Сертификат читается через RDP Negotiation и TLS-подключение, без входа на сервер. Имя нужно для группировки строк, а не как критерий доступности.

Если сертификат не удалось прочитать или в нем нет имени, приложение пробует `ping.exe -a <ip>`, затем `nbtstat.exe -A <ip>`, затем встроенный запасной NetBIOS Node Status request на UDP-порт 137 нужного IP-адреса, затем reverse DNS.

### Как выбираются адреса для сканирования

При сканировании приложение читает активные сетевые интерфейсы Windows через `NetworkInterface.GetAllNetworkInterfaces()`. Для каждого IPv4-адреса и маски вычисляется подсеть, после чего формируется список IP-адресов для проверки портов.

Если подсеть слишком большая, приложение ограничивает перебор ближайшим `/24` диапазоном, чтобы не запускать слишком тяжелое сканирование. В таблицу результатов сканирования попадают IP, у которых найден открытый проверяемый порт. Адреса, добавленные вручную, всегда проверяются и отображаются дополнительно, даже если открытых портов не найдено.

### Автоматическое и ручное сканирование

Автоматическая проверка запускается таймером WinForms каждые 10 минут, но проверяются только серверы из вкладки `Сервера`. Сканирование всей сети по таймеру не запускается.

Кнопка `Сканировать всю сеть` запускает полный обход локальной сети вручную. Во время проверки интерфейс не блокируется: проверка портов выполняется параллельно, до 32 IP одновременно.

Двойной щелчок по строке таблицы проверяет выбранную строку. Если в строке несколько IP с одинаковым именем, проверяются все IP из этой строки. Кнопка `Проверить IP` проверяет адрес из поля ввода. Кнопка `Добавить` сохраняет IP в список ручных адресов и сразу проверяет его.

Правый клик по строке вкладки `Сеть` открывает контекстное меню: `Копировать`, `Редактировать`, `Удалить`, `Проверить`. Для объединенной строки редактирование, удаление и проверка применяются ко всем IP-адресам внутри этой строки.

Текст из таблиц можно копировать как в обычных Windows-приложениях: Ctrl+C копирует выбранную строку, а правый клик по ячейке открывает пункт `Копировать` для конкретного значения.

В нижней части окна есть `Журнал событий мониторинга`. В него пишутся запуск и завершение проверок, найденные серверы, ручные проверки, ошибки и изменения статуса устройств.

При проверке выбранной строки таблицы журнал пишет подробный результат по каждому IP из этой строки. Формат: сначала `Проверка <имя>:`, затем отдельные строки вида `192.168.1.10: В сети, порты: 80, 389, 3389`, `192.168.1.11: Недоступен, порты: 80, 443` или `192.168.1.12: Недоступен, открытых портов нет`.

### Как определяется имя устройства

Для найденных IP программа сначала пытается получить имя из RDP-сертификата на порту `3389`. Если имя не найдено, используется `ping.exe -a <ip>`, затем `nbtstat.exe -A <ip>` с выбором NetBIOS-записи `<20>` или `<00>`, затем встроенный NetBIOS Node Status request по UDP/137, затем reverse DNS lookup через `Dns.GetHostEntryAsync`. Если DNS возвращает полное имя, в таблице показывается короткая часть до первой точки. Если имя определить не удалось, выводится `Неизвестное устройство`.

Если несколько IP-адресов возвращают одинаковое имя, они показываются в одной строке таблицы: `имя - IP-адреса - MAC-адреса - открытые порты - статус - ...`. IP без определенного имени не объединяются между собой, чтобы разные неизвестные устройства не смешивались в одну строку.

### Как определяется MAC-адрес

MAC-адрес берется из ARP-кэша Windows. Для этого приложение запускает системную команду:

```powershell
arp.exe -a
```

Затем вывод разбирается регулярным выражением. Это не используется для ping-проверки; `arp.exe` нужен только для отображения MAC-адреса уже известных соседних устройств. После проверки портов ARP-кэш читается повторно, чтобы заполнить MAC, который появился после сетевого подключения. Если IP находится за маршрутизатором, реальный MAC удаленного сервера может быть недоступен.

### Как определяются серверы

Сервер определяется эвристически. Устройство считается вероятным сервером, если выполняется одно из условий:

- имя содержит признаки сервера: `server`, `srv`, `dc`, `sql`, `db`, `1c`, `ksc`, `mail`, `exchange`, `nas`, `storage`, `backup`, `terminal`, `rdp`, `web`, `app`;
- открыт один из характерных серверных TCP-портов: `25`, `53`, `110`, `143`, `389`, `465`, `587`, `636`, `993`, `995`, `1433`, `1521`, `3306`, `5432`.

Порты `80`, `443`, `3389`, `22`, `8080` и `8443` отображаются в таблице, но сами по себе не превращают устройство в тип `Сервер`, потому что они часто встречаются и на обычных устройствах.

Проверка портов выполняется через `TcpClient`, без запуска внешних утилит. Таймаут подключения к одному порту - 300 мс, для порта `3389` - 1000 мс.

Вероятные серверы сортируются вверху таблицы и выделяются жирным шрифтом.

Статус `В сети` ставится только при открытом TCP-порте `3389`. Это защищает от ситуации, когда зависший сервер продолжает отвечать на ICMP ping или второстепенные службы, но RDP-служба уже не отвечает.

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

Фиксированный список серверов для первой вкладки и автоматической проверки хранится отдельно:

```text
%AppData%\NetworkMonitor\default_servers.json
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

Сканирование сети не меняет этот файл. Приложение сохраняет его только после действий пользователя во вкладке `Сервера`: добавления, редактирования или удаления строки.

IP-адреса серверов, которые были найдены или отмечены во вкладке `Сеть`, хранятся отдельно:

```text
%AppData%\NetworkMonitor\server_ips.json
```

Этот файл также содержит JSON-массив строк. Он может пополняться автоматически, когда вкладка `Сеть` определяет устройство как сервер, но он не перезаписывает `default_servers.json`.

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

Network Monitor is a Windows desktop application for checking device availability in a local IPv4 network. It automatically checks servers from the separate `default_servers.json` file every 10 minutes, displays discovered devices in a table, moves probable servers to the top, and allows users to add or check specific IP addresses manually. Full network scans are manual only to reduce network load.

### `Сервера` Tab

The `Сервера` tab is placed first and opens when the application starts. It is intended for a fixed list of important servers that is independent from network scan results.

On first run, the application creates:

```text
%AppData%\NetworkMonitor\default_servers.json
```

The public repository does not store the real server list. If a local `default_servers.json` file exists next to the application, it is used as the first-run template; otherwise, the user fills the list through the tab context menu.

This file is not overwritten after network scans and is not populated automatically. It can be changed only manually in the user profile or through the `Сервера` tab context menu: `Копировать`, `Добавить`, `Редактировать`, `Удалить`, `Проверить`.

Server checking is performed for every IP address in the row. For each IP, the application sends an ICMP ping through `Ping.SendPingAsync` and tries to read the RDP certificate on TCP port `3389`. A row is marked online when at least one IP replies to ping and provides a `3389` certificate. The event log records a detailed result for each IP address.

### How IP Checks Work

The application does not run `cmd.exe`, `ping.exe`, `.bat`, or `.ps1` files to check IP availability on the `Сеть` tab. Device availability in this table is determined by open TCP ports through `TcpClient`.

The `Сеть` tab algorithm is: find IP addresses, check open ports, resolve names, then group IP addresses with the same name into one row. A row is marked online only when TCP port `3389` is open. Other open ports are displayed as details, but they are not the status criterion.

Server names are first read from the RDP certificate on TCP port `3389`. The certificate is read through RDP Negotiation and TLS without logging in to the server. The name is used for grouping rows, not as an availability criterion.

If the certificate cannot be read or contains no usable name, the application tries `ping.exe -a <ip>`, then `nbtstat.exe -A <ip>`, then falls back to its built-in NetBIOS Node Status request over UDP/137, then reverse DNS.

### How Scan Targets Are Selected

During a scan, the application reads active Windows network interfaces by using `NetworkInterface.GetAllNetworkInterfaces()`. For each IPv4 address and subnet mask, it calculates the subnet and builds a list of IP addresses for port checks.

If a subnet is too large, enumeration is limited to the nearest `/24` range to avoid an overly heavy scan. Automatic scan results include IP addresses that have at least one checked TCP port open. Manually added IP addresses are always checked and displayed additionally, even when no checked port is open.

### Automatic and Manual Scanning

Automatic checking is started by a WinForms timer every 10 minutes, but only servers from the `Сервера` tab are checked. Full network scans are never started by the timer.

The `Сканировать всю сеть` button starts a full local network scan manually. The UI remains responsive because port checks run in parallel, up to 32 IP addresses at a time.

Double-clicking a table row checks that row. If the row contains several IP addresses with the same hostname, all IP addresses in that row are checked. The `Проверить IP` button checks the address from the input field. The `Добавить` button saves the IP to the manual list and checks it immediately.

Right-clicking a row on the `Сеть` tab opens a context menu: `Копировать`, `Редактировать`, `Удалить`, `Проверить`. For a grouped row, edit, delete, and check actions apply to all IP addresses inside that row.

The lower part of the window contains the monitoring event log. It records scan starts and finishes, discovered servers, manual checks, errors, and device status changes.

When a selected table row is checked, the event log records a detailed result for each IP address in that row. The format is `Проверка <name>:` followed by lines such as `192.168.1.10: В сети, порты: 80, 389, 3389`, `192.168.1.11: Недоступен, порты: 80, 443`, or `192.168.1.12: Недоступен, открытых портов нет`.

Table text can be copied like in regular Windows applications: Ctrl+C copies the selected row, and right-clicking a cell opens a `Копировать` item for that exact value.

### Hostname Detection

For discovered IP addresses, the application first tries to read the name from the RDP certificate on port `3389`. If no name is found, it uses `ping.exe -a <ip>`, then `nbtstat.exe -A <ip>` and selects the NetBIOS `<20>` or `<00>` entry, then uses the built-in NetBIOS Node Status request over UDP/137, then performs a reverse DNS lookup by using `Dns.GetHostEntryAsync`. If DNS returns a full hostname, only the short name before the first dot is displayed. If no name can be resolved, the UI shows `Неизвестное устройство`.

If several IP addresses return the same hostname, they are displayed in one table row: `name - IP addresses - MAC addresses - open ports - status - ...`. IP addresses without a resolved hostname are not grouped together, so unrelated unknown devices are not mixed into one row.

### MAC Address Detection

MAC addresses are read from the Windows ARP cache. For this part only, the application starts the system command:

```powershell
arp.exe -a
```

The output is parsed with a regular expression. This is not used for ping checks; `arp.exe` is used only to display MAC addresses for known neighboring devices. After port checks, the ARP cache is read again to fill MAC addresses that appeared after network connections. If an IP address is behind a router, the remote server's real MAC address may not be available.

### Server Detection

Server detection is heuristic. A device is treated as a probable server when at least one condition is true:

- the hostname contains server-like tokens: `server`, `srv`, `dc`, `sql`, `db`, `1c`, `ksc`, `mail`, `exchange`, `nas`, `storage`, `backup`, `terminal`, `rdp`, `web`, `app`;
- one of the distinctive server TCP ports is open: `25`, `53`, `110`, `143`, `389`, `465`, `587`, `636`, `993`, `995`, `1433`, `1521`, `3306`, `5432`.

Ports `80`, `443`, `3389`, `22`, `8080`, and `8443` are displayed in the table, but they do not by themselves turn a row into type `Сервер`, because they are also common on ordinary devices.

Port checks are performed through `TcpClient`, without external tools. The connection timeout is 300 ms for regular ports and 1000 ms for port `3389`.

Probable servers are sorted to the top of the table and displayed in bold.

The `В сети` status is set only when TCP port `3389` is open. This avoids treating a hung server as online just because it still replies to ICMP ping or secondary services while RDP no longer responds.

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

The fixed server list for the first tab and the 10-minute automatic check is stored separately:

```text
%AppData%\NetworkMonitor\default_servers.json
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

Network scans do not change this file. The application saves it only after explicit user actions on the `Сервера` tab: add, edit, or delete.

Server IP addresses found or marked on the `Сеть` tab are stored separately:

```text
%AppData%\NetworkMonitor\server_ips.json
```

This file is also a JSON string array. It may be updated automatically when the `Сеть` tab identifies a device as a server, but it does not overwrite `default_servers.json`.

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
