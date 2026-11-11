# Epson iPrint 7.14.0 — протокол и сетевая часть (из decomp1)

Источник: `decomp1` = `epson.print` 7.14.0 (versionCode 7140000). Java-код не обфусцирован.
Разобрана нативная `lib/arm64-v8a/libescprlib.so`: экспортируемые символы, строки, ARM64-константы.

## 1. Архитектура

```
UI (epson.print.*)  ──►  EscprLib.java (com.epson.mobilephone.common.escpr, JNI через RegisterNatives)
                              │  init_driver, search_printer2, set_printer, start_job, start_page,
                              │  print_page, end_page, end_job, get_supported_media, getInkInfo, ...
                              ▼
                     libescprlib.so  =  Epson ESC/P-R SDK (API eps*)
                        ├─ поиск:     mdnsFind* (mDNS) + snmpFind* (SNMP broadcast)
                        ├─ задание:   raw* (9100) / lpr* (515) / ipp* (631) / usb*
                        ├─ статус:    SNMP (Printer-MIB + Epson MIB 1248) + "@BDC ST2"
                        ├─ обслуж.:   epsMakeMainteCmd → remote-mode команды
                        └─ растр:     ESC/P-R (pageStartJob/ColorRow, сжатие CompressBitImage/DeltaRow)
```

Почти вся логика протокола находится в нативной библиотеке, Java только вызывает её и рисует экраны.
По именам функций (`epsInitDriver`, `epsFindPrinter`, `epsStartJob`, `epsPrintBand`, `epsGetInkInfo`…)
это тот же API, что и в открытом драйвере **epson-inkjet-printer-escpr** для Linux (каталог `lib/`, около 18.8 тыс. строк C, лицензия GPL-2.0-or-later).
Там есть исходники ровно этого слоя, их можно взять за основу порта.

## 2. Порты (подтверждено по ARM64-коду)

| Назначение | Порт | Функция в .so |
|---|---|---|
| RAW / pdl-datastream | **TCP 9100** | `rawGetDefautiPort` → `mov w0,#9100` |
| LPR | **TCP 515** | `lprGetDefautiPort` |
| IPP (HTTP POST, `Content-Type: application/ipp`) | **TCP 631** | `ippGetDefautiPort` |
| SNMP (поиск broadcast + статус) | **UDP 161**, community `public` | `snmpOpenSocket`, `SnmpTransact` |
| mDNS | **UDP 5353** | `mdnsStartDiscovery` |

ENPC (UDP 3289) в библиотеке **не используется** (это исправление к прошлому анализу).

## 3. Поиск принтеров
- **mDNS**, типы сервисов: `_pdl-datastream._tcp.local`, `_printer._tcp.local` (LPR), `_ipps._tcp.local` (+ IPP), `_scanner._tcp.local`.
  Парсер свой (`ParseMdnsResponse`, `ParseTextData`), из TXT-записей проверяется, что устройство Epson (`isEpsonDevice`).
- **SNMP broadcast** на `255.255.255.255:161`: опрос sysObjectID и Epson MIB, затем IEEE-1284 Device ID (`MFG:` / `MDL:` / `CMD:`), где `CMD:` содержит `ESCPR`.
- **Ручной IP** (`prtProbePrinterByAddr`): тот же SNMP-запрос на конкретный адрес. Это и есть экран «manual ip» из Figma.
- Wi-Fi Direct / Simple AP: SSID `DIRECT-…`, `DIRECT-epson-setup-iPrint`. Для порта это не нужно.

## 4. SNMP OID
**Стандартные (Printer-MIB / Host-Resources):**
`1.3.6.1.2.1.25.3.2.1.3.1` (hrDeviceDescr), `…25.3.5.1.1.1/2.1` (hrPrinterStatus / DetectedErrorState),
`1.3.6.1.2.1.43.11.1.1.8.1 / 9.1` (prtMarkerSupplies MaxCapacity / Level — **уровни чернил**),
`…43.12.1.1.4.1` (colorant value — имя цвета), `…43.8.2.1.13.1` (input tray), `…43.15.1.1.5.1`,
`1.3.6.1.2.1.4.20.1.1` (IP), `1.3.6.1.2.1.2.2.1.6` (MAC).

**Epson private (enterprise 1248):** `1.3.6.1.4.1.1248.1.1.3.1.1.*`, `…1.1.3.1.29.*`, `…1.1.3.1.30.*`, `…1.2.2.1.1.1.1.1`
(в этом же дереве Epson отдаёт Device ID), `…1.2.2.44.1.1.2.1.*` (туннель команд через SNMP:
в OID закодированы байты, например `101.120 = "ex"`, `99.116 = "ct"`).
Полный список из 60+ OID находится в строках `.so`. Расшифровка каждого OID нужна при реализации.

## 5. Команды принтера
- **IEEE-1284.4 (D4/CBT)** с каналами `EPSON-CTRL` и `EPSON-DATA` используется для USB; по сети данные идут напрямую.
- **Статус:** `@BDC ST2` возвращает бинарный блок ST2 (`serAnalyzeStatus`: состояние, ошибки, чернила `clsParseCartridge`, ёмкость отработки `clsParseMainteBox`).
- **Обслуживание:** `@BDC PS` / `@BDC PM`, remote mode `REMOTE1PI` / `REMOTE1PP`, ответы `vi:04/05/08:…`, `ex:0000004D:…`, `ct:OK|NG|NA`, `rj:OK`.
  Head cleaning и nozzle check формируются функцией `epsMakeMainteCmd` и отправляются функцией `epsWrapperDoCmd`.
- **EJL** (`@EJL …`) используется только для лазерных ESC/Page. Для L805 не нужен.
- **Печать:** язык ESC/P-R (`ESCPR` в `CMD:` Device ID). Растр полосами, сжатие RLE / DeltaRow,
  поддерживается `epsSendJpegData` (JPEG passthrough на поддерживающих моделях).

## 6. Что из этого нужно для WP 8.1
| Функция приложения | Реализация |
|---|---|
| local search | mDNS (`DatagramSocket` + multicast 224.0.0.251:5353) и/или SNMP broadcast |
| manual ip | SNMP GET Device ID на IP:161 |
| ink level, maintenance box | SNMP prtMarkerSupplies **или** ST2 |
| head cleaning, nozzle check | remote-mode команда через 9100 (как `epsMakeMainteCmd`) |
| print photos / documents | рендер в RGB → ESC/P-R → TCP 9100 |

**Рекомендуемый путь:** собрать C-код ESC/P-R (из открытого epson-inkjet-printer-escpr или по этому .so)
в **Windows Runtime Component (C++/CX, ARM)**, а платформенный слой (`epsmpSocket/Send/Receive/SendTo` и т.д.)
реализовать через Winsock или WinRT-сокеты. Тогда UI на C#/XAML вызывает уже готовый API `eps*`.
Учти: лицензия GPL-2.0+ требует, чтобы приложение тоже было под GPL.
Манифест WP 8.1: capabilities `internetClientServer` и `privateNetworkClientServer`.

## 7. Не разобрано (файлы вложены глубже 7 папок, их не удалось передать)
`EscprLib.java` (точные сигнатуры JNI), `MaintainActivity.java`, `EpsonService.java` (порядок вызовов при печати),
`PrinterFinder.java`, `ActivityIpPrinterSetting.java`, `MediaInfo.java` (таблица бумаги и типов носителей).

## 8. Реализация в проекте WP 8.1 (2026-11-10)
Протокол перенесён на чистый C# (`iprint.WindowsPhone/Escpr`) по исходникам epson-inkjet-printer-escpr 1.8.10:
- `Snmp.cs`: SNMPv1 GET/GETNEXT, community `public`, UDP 161. Кодирование сверено с каноническим пакетом snmpget.
- `EpsonPrinter.cs`: поиск через broadcast GETNEXT `1.3.6.1.4.1.1248.1.2.2.1.1.1.1` (Device ID); статус через GET `…1248.1.2.2.44.1.1.2.1.115.116.1.0.1` («st» → `@BDC ST2`).
- `EscprJobWriter.cs`: поток ESC/P-R (RGB 360 dpi, RLE) через TCP 9100. **Побайтно совпадает** с выводом собранного Linux-фильтра Epson на 1 и 2 страницах A4. Отличаются только метка времени TI и счётчик `endp`. Геометрия A4 с полями и без, а также 10×15 без полей сверена с эталоном.
- `EscprCommands.Maintenance`: чистка головы и проверка дюз (порт `epsMakeMainteCmd`).
- Семантика страниц: `sttp` + `setn(осталось страниц)`, `endp(сколько страниц после текущей)`.

## 9. Счётчики отработки (памперс) Epson L805, только чтение
Получено из SNMP-обмена сервисной утилиты с принтером:
- запрос на чтение байта EEPROM: `1.3.6.1.4.1.1248.1.2.2.44.1.1.2.1` + `.124.124.7.0.67.1.65.190.160.<lo>.<hi>` («||», ключ `0x43 0x01`)
- ответ: `@BDC PS\r\nEE:AAAAVV;`, где `AAAA` — адрес, `VV` — значение
- основной счётчик: `0x1C, 0x1D, 0x102` (младший байт первым); дополнительный: `0x1E, 0x1F, 0x103`
- процент = значение / 175.73 (100 % ≈ 17573)
