# CHART RUNNER — подача в App Store

Написано 2026-09-03. Что сделано в проекте, что делаешь только ты, и готовые тексты.

## Что уже в сборке

| требование Apple | статус |
|---|---|
| Bundle ID `com.mathewk.chartrunner`, Team `R36SPNVQUW`, автоподпись | ✅ `DeviceBuilder.BuildIos` |
| Версия 1.0.0, build 1 | ✅ там же |
| Иконка 1024 (из валидированного `toys/assets/icon.svg`) → все слоты | ✅ `Assets/ChartRunner/Icon/app-icon-1024.png` |
| Il2CPP Release, портрет, статус-бар скрыт, без «Development Build», без Unity-сплэша | ✅ |
| Только HTTPS (Binance API), ATS без исключений | ✅ |
| Дисклеймер «игра · вся валюта виртуальная · не финансовый совет» | ✅ на экране «Как играть» и в настройках |
| Офлайн-фолбэк (без сети играбельно) | ✅ встроенные 300 свечей BTC |
| Privacy manifest (UserDefaults) | ✅ Unity 6 генерирует `PrivacyInfo.xcprivacy` в UnityFramework |

Сборка → архив → .ipa:
```bash
cd unity && Unity -batchmode -quit -projectPath "$PWD" -buildTarget iOS -executeMethod ChartRunner.EditorTools.DeviceBuilder.BuildIos
tools/ios-archive.sh      # unity/build/ios-archive/ChartRunner.ipa
```

## Сделано через App Store Connect API (2026-09-03)

Запись приложения `Chart Runner` (id 6808117432, SKU chartrunner-ios), версия 1.0.0,
описания/ключевые слова/промо EN+RU, категория Игры → Гонки/Казуальные, возрастная анкета
(всё «нет»), Privacy Policy URL, App Privacy «Data Not Collected» (опубликован), цена
бесплатно (база USA), 175 территорий, контакт для ревью (из Keepframe), License Agreement
принят. Загрузка билда: `tools/ios-upload.sh` (API-ключ ZBJUHM8454 на маке).

Билд 1.0.0 (1) загружен (`tools/ios-upload.sh`), обработан (VALID) и привязан к версии.
Скриншоты 6.9" 1320×2868 (EN и RU по 6 штук) загружены из mac-сборки с `-superSize 3`
(`unity/build/store-en`, `store-ru`), состояние COMPLETE. Release type: AFTER_APPROVAL.

**2026-09-03 08:03 UTC — ОТПРАВЛЕНО НА РЕВЬЮ** (`node tools/asc-submit.cjs`): заявка
`ed327d52-ac5e-46d5-aa36-5fac35d3dcca`, состояние `WAITING_FOR_REVIEW`. Блокером был
незаполненный `contentRightsDeclaration` у приложения (Apple отвечал 409 «not in valid
state», причина — в `meta.associatedErrors`); выставлен `DOES_NOT_USE_THIRD_PARTY_CONTENT`,
как у остальных приложений аккаунта. Осталась пустая заявка `b33a83ea-…` в состоянии
READY_FOR_REVIEW без элементов (первый прогон) — можно отменить в web, на ревью не влияет.
Проверка: `cd ~/.appstoreconnect && node status.js`.

## Что можешь сделать только ты (нужны твои учётные данные Apple)

1. **App Store Connect → новое приложение**: имя `Chart Runner`, Bundle ID `com.mathewk.chartrunner`, SKU `chartrunner-ios`.
2. **Загрузка .ipa**: Transporter (перетащить файл) или Xcode → Organizer → Distribute.
3. **Privacy Policy URL** — обязателен (приложение ходит в сеть). Готовый текст ниже; файл
   `privacy.html` в корне репо, после мержа в `main` будет на
   `https://cryptomafioznik.github.io/cryptobill/privacy.html`.
4. **Скриншоты**: iPhone 6.7" (1290×2796) и 6.5" (1284×2778) — минимум 3. Снять с телефона
   (или взять из `unity/build/mac/Logs/shots/`, но там 430×932 — надо с устройства).
5. **Age rating**: анкета — везде «нет», кроме «Simulated Gambling: Infrequent/Mild»?
   **Нет**: в игре нет ставок на реальные деньги и нет покупки валюты — отвечай «None».
   «Unrestricted web access: No».
6. **App Privacy (Data collection)**: «Data Not Collected» — приложение не собирает данные,
   аналитики нет, аккаунтов нет. Запросы к Binance не содержат идентификаторов пользователя.
7. **Export compliance**: только HTTPS → «uses standard encryption, exempt» (ITSAppUsesNonExemptEncryption = NO — уже в Info.plist Unity).

## Тексты для App Store Connect

**Название:** Chart Runner
**Подзаголовок (30):** Гоняй по реальному крипто-графику
**Категория:** Games → Racing (вторичная: Arcade)

**Описание (RU):**
Оседлай настоящий график. Каждая трасса — живые свечи BTC, ETH, SOL, DOGE, PEPE и ещё
семи монет с биржи: подъёмы рынка становятся подъёмами, дампы — обрывами. Держи газ,
балансируй на заднем колесе, собирай монеты — и успей зафиксировать профит, пока сзади
не догнала волна ликвидации.

• 12 монет, у каждой своя волатильность = своя сложность
• Плечо ×2…×100: больше риск — быстрее дамп
• Лонг и шорт: зарабатывай и на росте, и на падении
• Путь трейдера: 20 исторических трасс — от тихого вторника до краха FTX и Black Thursday
• Рынок сегодня: одна трасса дня у всех, три попытки, стрик
• 7 байков, 4 скина, 7 апгрейдов, ранги от Креветки до Кита
• Живой синтвейв-саундтрек, реагирующий на скорость и опасность

Это игра. Вся валюта виртуальная. Не финансовый совет.

**Description (EN):**
Ride the real chart. Every track is live candles from the exchange — BTC, ETH, SOL, DOGE,
PEPE and seven more coins. Pumps become climbs, dumps become drops. Hold the throttle,
balance on the rear wheel, collect coins — and take profit before the liquidation wave
catches you from behind.

• 12 coins, each with its own volatility = its own difficulty
• Leverage ×2…×100: more risk, faster dump
• Long and short
• Trader's Path: 20 historical tracks, from a quiet Tuesday to the FTX crash and Black Thursday
• Market Today: one daily track for everyone, three tries, streaks
• 7 bikes, 4 skins, 7 upgrades, ranks from Shrimp to Whale
• Live synthwave soundtrack reacting to speed and danger

This is a game. All currency is virtual. Not financial advice.

**Ключевые слова (100):** bike,trials,crypto,chart,bitcoin,racing,motocross,stunt,wheelie,trading,arcade

**Что нового (1.0):** Первый релиз.

## Privacy Policy (текст `privacy.html`)

Chart Runner не собирает, не хранит и не передаёт персональные данные. Приложение не
требует аккаунта. Прогресс игры хранится только на устройстве. Для построения трасс
приложение запрашивает публичные котировки у Binance API (api.binance.com) по HTTPS;
запросы не содержат идентификаторов пользователя или устройства. Аналитика, реклама и
сторонние SDK отсутствуют. Вопросы: mathewpk4@gmail.com.

## Чего в v1.0 нет против сайта (сознательно)

- Флипы и FMX-трюки — нет в физике Unity (миссии «сальто» пропущены).
- Кнопки ШЕР/КЛИП на экране смерти — браузерные API.
- События памп-ралли/кит/флеш-крах — в исходнике они только в процедурном режиме «Отрыв»,
  которого в v1.0 нет (Забег на реальном графике «чистый» по решению ph4).
