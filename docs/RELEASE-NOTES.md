# Muse Desk 1.25.0 — Windows x64 + macOS Apple Silicon + Ubuntu amd64

> **Preview — предварительная версия.** Пометка Latest означает самый новый доступный выпуск, а не завершённую аппаратную приёмку. Ограничения подписи и совместимости описаны ниже.

Добавлена **Ubuntu-версия (amd64, DEB)** с общим интерфейсом и логикой клиента macOS: проекты и чаты, потоковые ответы, рассуждения, вложения, инструменты с подтверждениями, навигатор тем, копирование, экспорт и озвучивание.

DEB устанавливает системные зависимости через APT. Встроенный мастер проверяет Ubuntu, RAM, NVIDIA, драйвер и место на диске, затем скачивает Linux-движок и Glimmer с проверкой SHA-256. При недостаточной памяти модель не скачивается. Движок ограничен одной моделью.

Ubuntu: 22.04+, amd64; для автоматической установки Glimmer нужны 32 ГБ RAM, NVIDIA 24 ГБ VRAM, Compute Capability 7.0+, драйвер 550+ и 40 ГиБ места. AMD, Intel и CPU-only пока не входят в автоматическую установку Glimmer. [Инструкция Ubuntu](https://github.com/magamadovnurid/MuseDesk/blob/main/docs/UBUNTU.md). Реальная GPU-генерация на Ubuntu ещё требует аппаратной проверки.

## Выберите файл

| Компьютер | Файл |
|---|---|
| **Windows x64**, новая установка | **[Скачать установщик EXE](https://github.com/magamadovnurid/MuseDesk/releases/download/v1.25.0-preview.1/MuseDesk-Windows-x64-Setup.exe)** |
| Windows, только приложение без движка/модели | [Скачать ZIP](https://github.com/magamadovnurid/MuseDesk/releases/download/v1.25.0-preview.1/MuseDesk-Windows-x64-App.zip) |
| **macOS · Apple Silicon**, новая установка | **[Скачать DMG](https://github.com/magamadovnurid/MuseDesk/releases/download/v1.25.0-preview.1/MuseDesk-1.25.0-macOS-AppleSilicon.dmg)** |
| Mac, архив того же приложения | [Скачать ZIP](https://github.com/magamadovnurid/MuseDesk/releases/download/v1.25.0-preview.1/MuseDesk-1.25.0-macOS-AppleSilicon.zip) |
| **Ubuntu 22.04+ · amd64**, новая установка | **[Скачать DEB](https://github.com/magamadovnurid/MuseDesk/releases/download/v1.25.0-preview.1/MuseDesk-1.25.0-Ubuntu-amd64.deb)** |

Контрольные суммы всех пакетов — **SHA256SUMS.txt**. Исходники доступны стандартными архивами GitHub. Веса модели не включены в пакеты: они скачиваются мастером настройки.

## Ubuntu — новая платформа в 1.25.0

Скачайте DEB из таблицы и откройте его в системном установщике Ubuntu. Можно установить из терминала в папке скачивания:

```bash
sudo apt install ./MuseDesk-1.25.0-Ubuntu-amd64.deb
```

APT установит зависимости интерфейса, curl, zstd и espeak-ng; Electron уже включён в пакет. Node.js, Python, .NET и отдельная установка Ollama не нужны. После запуска Muse Desk встроенный мастер проверит систему и предложит установку Glimmer, если оборудование подходит. Требуется установленный драйвер NVIDIA; приложение не заменяет драйвер автоматически.

Профиль Ubuntu — Q4_K_M + Q8-кодировщик: около 19 ГБ весов и дополнительно около 1,4 ГБ архива движка. Учитывается память одной GPU; две карты по 12 ГБ не считаются картой на 24 ГБ. Ввод промпта включается после подтверждения готовности единственной выбранной модели.

Ubuntu использует общий клиент с macOS и наследует его возможности и текущие ограничения относительно расширенных функций Windows. [Установка и требования Ubuntu](https://github.com/magamadovnurid/MuseDesk/blob/main/docs/UBUNTU.md) · [Матрица функций общего клиента](https://github.com/magamadovnurid/MuseDesk/blob/main/docs/MACOS.md).

![Muse Desk на Ubuntu](https://raw.githubusercontent.com/magamadovnurid/MuseDesk/main/docs/images/ubuntu-overview.png)

*Снимок установленного DEB на Ubuntu 24.04. Диалог и статус модели — демонстрационные данные теста интерфейса.*

## Mac: настройка по параметрам вашего компьютера

Нужны macOS 14+ и Apple Silicon. Мастер считывает архитектуру, объединённую память и свободное место непосредственно на Mac. Для Glimmer: от 32 ГБ памяти и 40 ГиБ свободного места. На 32–48 ГБ выбирается Q4_K_M + Q8 кодировщик; от 64 ГБ — Q4_K_M + F16. На 24 ГБ большая модель автоматически не скачивается. Загрузка — около 19–21 ГБ.

В Mac-клиент перенесены проекты, вложенные чаты, потоковые ответы, рассуждение, даты, вложения, экспорт, подтверждаемые инструменты и последовательная загрузка моделей. Некоторые расширенные Windows-возможности ещё не перенесены: [матрица функций и установка Mac](https://github.com/magamadovnurid/MuseDesk/blob/main/docs/MACOS.md).

## Windows

Сохранены WinForms-интерфейс, проекты и чаты, управление готовностью модели и отдельный графический установщик. Для автоматической установки Glimmer нужны NVIDIA от 24 ГБ VRAM, RAM от 32 ГБ, Compute Capability 7.5+ и 40 ГиБ свободного места. [Инструкция Windows](https://github.com/magamadovnurid/MuseDesk/blob/main/docs/INSTALLATION.md).

## Предварительный статус

Публикация выпуска требует успешных проверок Windows, macOS и Ubuntu. Проверены Windows-приложение и установщик, ARM64-пакет macOS, установка DEB и интерфейс на Ubuntu 22.04 и 24.04. Настоящие движки Apple Silicon и Linux запускаются без весов; интерфейсы проверяются на синтетических диалогах. [Результаты проверок](https://github.com/magamadovnurid/MuseDesk/blob/main/docs/VALIDATION.md).

Полная генерация Glimmer на физических Mac с разным объёмом памяти и на NVIDIA под Ubuntu ещё не прошла аппаратную приёмку. Mac-приложение имеет ad-hoc подпись, **без Developer ID и нотариализации Apple**; Gatekeeper может потребовать ручное разрешение запуска. Windows EXE также пока без сертификата Authenticode. Это preview, а не обещание полной аппаратной совместимости.

## Компоненты и надёжность установки

- Проверка актуального выпуска движка на трёх платформах с проверенным резервным каталогом.
- Ubuntu: зависимости через APT, собственная проверка RAM/GPU/драйвера и Linux-движок.
- Windows: автоматическая установка совместимой .NET Framework 4.8.1 с проверкой подписи Microsoft и без автоматической перезагрузки.
- Выровнены путь установки и кнопка выбора папки; восстановление повреждённых загрузок и продолжение после прерывания.
- Проверка приватных файлов и секретов стала обязательным условием публикации.

[Подробности компонентов и ограничений](https://github.com/magamadovnurid/MuseDesk/blob/main/docs/COMPONENTS.md) · [Аудит публичного репозитория](https://github.com/magamadovnurid/MuseDesk/blob/main/docs/PRIVACY-AUDIT.md).
