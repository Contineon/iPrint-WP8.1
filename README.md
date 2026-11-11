# iPrint for Windows Phone 8.1

Print photos and documents on Epson inkjet printers over Wi‑Fi from a Windows Phone 8.1 device —
an unofficial port of the idea behind Epson iPrint, written from scratch in C#/XAML.

> Not affiliated with, endorsed by, or supported by Seiko Epson Corporation.
> "Epson" and "iPrint" are trademarks of their respective owners.

## Features

- **Find printers** on the local network (SNMP broadcast) or add one by IP address
- **Print photos** — preview that matches the print, rotate, borderless or with borders,
  paper size / media type / quality / source / colour / sharpness
- **Print documents** — plain‑text files (`.txt`, `.log`, `.csv`, `.md`, …, UTF‑8 or Windows‑1251)
  and images; page range and copies
- **Printer status** — ready / busy / error, ink levels (on printers that report them)
- **Waste ink counters** — state for all models, exact percentages for Epson L805
- **Maintenance** — nozzle check and head cleaning
- Light and dark phone themes, Metro (Modern UI) design

Tested with an **Epson L805**. Other Epson printers whose IEEE‑1284 device ID lists `ESCPR`
in the command set should work, but are untested.

## How it works

The app talks to the printer directly, without drivers or cloud services:

| Task | Protocol |
|---|---|
| Discovery, status (`@BDC ST2`), EEPROM counters | SNMP v1, UDP 161, Epson private MIB `1.3.6.1.4.1.1248` |
| Print job, nozzle check, head cleaning | ESC/P‑R raster over RAW TCP 9100 |

The ESC/P‑R job generator (`Escpr/EscprJobWriter.cs`) is a C# port of the relevant parts of
Epson's open‑source **ESC/P‑R driver for Linux 1.8.10**. Its output was verified byte‑for‑byte
against the original driver. Protocol notes: [`docs/PROTOCOL.md`](docs/PROTOCOL.md).

## Building

- Visual Studio 2015 with *Tools for Universal Windows Apps* (Windows Phone 8.1 SDK)
- Open `iprint.sln`, select **ARM** and **Device**, then Build → Deploy
- A developer‑unlocked Windows Phone 8.1 / Windows 10 Mobile device (or the WP 8.1 emulator)

## Project layout

```
iprint/iprint.Shared/        App.xaml (styles, themes), App.xaml.cs (navigation, back key, file picker)
iprint/iprint.WindowsPhone/
  Escpr/                     SNMP client, device ID, ST2 status parser, ESC/P-R commands and job writer
  Imaging/                   band-wise photo rasterizer, preview renderer, text page renderer
  Models/                    settings, print session and job runner
  *.xaml                     pages: main pivot, search, manual IP, maintenance, previews, settings, printing
docs/PROTOCOL.md             protocol analysis
```

## License

GPL‑2.0‑or‑later — see [`LICENSE`](LICENSE). The printing engine is derived from the
Epson ESC/P‑R driver for Linux (© Seiko Epson Corporation, GPL), so the whole app is
distributed under the same terms.

## Author

Arsenii Contineon — [hello.contineon.org](https://hello.contineon.org)

---

### Коротко по-русски

Неофициальный порт Epson iPrint для Windows Phone 8.1: поиск принтера по Wi‑Fi, печать фото и
текстовых документов, статус, счётчики памперса (проценты для L805), проверка дюз и прочистка.
Работает напрямую с принтером по SNMP и RAW 9100, без драйверов. Проверено на Epson L805.
Лицензия GPL‑2.0‑or‑later.
