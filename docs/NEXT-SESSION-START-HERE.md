# СЛЕДУЮЩАЯ СЕССИЯ — ТОЧКА ВХОДА

Переписано 2026-09-03 (конец сессии подачи). **Читать первым.** Ветка `codex/visual-v05`,
HEAD см. `git log`, дерево чистое, всё запушено. `toys/chartrider.html` — спецификация, не трогать.

## СОСТОЯНИЕ ОДНИМ АБЗАЦЕМ

Игра CHART RUNNER (Unity 6000.3.20f1, iOS) — полный порт браузерной версии на движок:
все экраны, реальные свечи Binance, экономика (плечо/позиция/ликвидация/◆/ранги),
гараж (7 апгрейдов с эффектом в физике), 7 байков × 4 скина (спрайты из браузера),
Путь трейдера (20 исторических трасс), Вызов дня, Рынок сегодня, миссии, онбординг,
процедурный звук, локализация EN/RU, «сок» (флипы, фонтан фиксации, ачивки, подсказки).
**Релиз 1.0.0 (1) загружен в App Store Connect, обработан и привязан к версии; все
метаданные, скриншоты, цена, территории, приватность, соглашение — заполнены.**
**2026-09-03 08:03 UTC: версия 1.0.0 ОТПРАВЛЕНА НА РЕВЬЮ**, заявка
`ed327d52-ac5e-46d5-aa36-5fac35d3dcca`, состояние `WAITING_FOR_REVIEW`.

## ПЕРВОЕ ДЕЙСТВИЕ НОВОЙ СЕССИИ

```bash
cd ~/.appstoreconnect && node status.js   # состояние ревью (WAITING_FOR_REVIEW / IN_REVIEW / REJECTED / READY_FOR_DISTRIBUTION)
```
Если Apple вернула замечания — текст пользователь вставит в чат; исправить, поднять
buildNumber в `Editor/Bootstrap/DeviceBuilder.cs`, `tools/ios-archive.sh && tools/ios-upload.sh`,
привязать новый билд к версии и повторить `node tools/asc-submit.cjs` (скрипты ASC — `.cjs`,
потому что package.json репо объявляет `"type": "module"`). Ловушка подачи: 409 «not in valid
state» при добавлении версии в заявку — причина лежит в `meta.associatedErrors` ответа
(у нас это был пустой `contentRightsDeclaration` приложения; скрипт теперь ставит его сам).

## РЕШЕНИЯ ПОЛЬЗОВАТЕЛЯ (действуют)

1. Браузерная версия — спецификация; ничего не сочинять, портировать (2026-09-02).
2. Цель — выпустить, не переделывать бесконечно.
3. «Доступ к аккаунту есть, делай всё сам, улучши максимально» (2026-09-03); соглашение
   Apple принято по его «прими»; отправка на ревью разрешена.

## ИДЕНТИФИКАТОРЫ

- Bundle `com.mathewk.chartrunner`, Team `R36SPNVQUW`, ASC app id `6808117432`,
  appInfo `d8b17dee-2ceb-45c9-a829-6c8b0347a86b`, версия `d4be1bd2-f686-48f8-8941-305aeccc6bfd`,
  билд `dc401633-f9c1-4822-926b-04637bc16862` (VALID), локализации версии en-US
  `1b2ed228-…`, ru `984cd6b1-…`. Release type AFTER_APPROVAL. Privacy URL:
  https://cryptomafioznik.github.io/cryptobill/privacy.html (на main).
- Телефон: iPhone 16 Pro Max `421F307A-3CBC-58D2-9DE0-5D84CEA49B57`. Ставить: `tools/ios-deploy.sh` (разблокирован!).

## КАК СОБИРАТЬ И ПРОВЕРЯТЬ

```bash
tools/unity-build-mac.sh                       # честная mac-сборка (падает на лицензии/компиляции/старом бинаре)
cd unity && build/mac/ChartRunner.app/Contents/MacOS/ChartRunner -screen-width 430 -screen-height 932 -screen-fullscreen 0 -shots        # кадры заезда
…-shotsUi [-lang en] [-superSize 3]            # кадры экранов; superSize 3 при окне 440×956 = 1320×2868 для стора
tools/ios-archive.sh && tools/ios-upload.sh    # .ipa → App Store Connect (при новом билде поднять buildNumber в DeviceBuilder!)
tools/asc-upload-shots.cjs <locId> <dir>        # скриншоты в набор APP_IPHONE_67
```
Гейты: EditMode 8/8; PlayMode `-testFilter "ChartRunner.Tests.WaveGate|ChartRunner.Tests.AcceptanceBattery"`
→ 10/11 (падает только H_RecoveryWindow — известное открытое решение). Полный PlayMode не гонять.
Unity Personal требует онлайн-лицензию: если сборка молча не идёт — войти в Unity Hub (пользователь).
Экспорт арта из браузера: `.claude/launch.json` → `tools/sprite-sink.py` + `bikeSprites(B)` при `DPR=4`.

## ГДЕ ЧТО (unity/Assets/ChartRunner)

`Runtime/Game/PlaySession.cs` — машина экранов + HUD + заезд · `Meta/Economy.cs` — вся
экономика · `Meta/Tickers.cs` — Binance/фолбэк · `Track/TickerTrack.cs` — свеча→трасса ·
`Game/Position.cs` — DCA · `Game/LiquidationWave.cs` (гейт WaveGate) · `Meta/Campaign.cs` —
Путь/дейли · `Meta/Loc.cs` — RU→EN · `Game/Juice.cs` — сок · `Game/GameAudio.cs` — синт ·
`Meta/UpgradeEffects.cs` · `WorldView/TrackDeckView/BikeView` — визуал · `Editor/Bootstrap/DeviceBuilder.cs`
— сборки (mac/ios/ios-sim, иконка, версия) · `docs/APP-STORE-SUBMISSION.md` — подача.

## ЧЕГО НЕТ В 1.0 (сознательно)

FMX-трюки с позами; ШЕР/КЛИП (браузерные API); события памп/кит/флеш (в исходнике только
в процедурном «Отрыве», которого нет); Game Center; звук на телефоне ещё не оценивался ушами.

## ЧТО ДЕЛАТЬ ПОСЛЕ ОТПРАВКИ

1. Вердикт пользователя с телефона (звук, скорость, байки) → правки → buildNumber 2 → archive → upload.
2. Если Apple вернёт замечания — текст сюда, исправить, пересобрать.
3. Возможные улучшения дальше: Game Center лидерборды, режим «Отрыв» с событиями, трюки.

## ЛОВУШКИ, ОПЛАЧЕННЫЕ ЭТИМИ СЕССИЯМИ (детали в памяти)

Ось Y исходника вниз; градиенты Canvas в sRGB; статические инициализаторы C# по порядку;
дистанция исходника = px/10 (браузерные метры); линия езды — середина деки; BikeState нулевой
до первого шага физики; герой 7.2 % высоты кадра; старый отчёт сборки = ложный PASS; фоновые
команды стартуют из корня репо — только абсолютные пути; Unity-симулятор на Apple Silicon
даёт x86_64 — скриншоты стора снимать mac-плеером с superSize 3; классификатор блокирует
публикующие действия (upload/submit) — не обходить, просить пользователя.
