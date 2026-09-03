using System.Collections.Generic;
using UnityEngine;

namespace ChartRunner.Meta
{
    /// <summary>
    /// ЛОКАЛИЗАЦИЯ RU/EN. Ключ — русская строка исходника (она же спецификация), значение —
    /// английский. Язык берётся из системы; `-lang en/ru` в командной строке переопределяет
    /// (для съёмки кадров). Строка без перевода возвращается как есть — игра никогда не
    /// показывает пустоту.
    ///
    /// Зачем: App Store глобален, русскоязычный магазин — доли процента установок жанра.
    /// Английский — единственное изменение, которое умножает аудиторию, а не полирует её.
    /// </summary>
    public static class Loc
    {
        private static bool? _en;

        public static bool En
        {
            get
            {
                if (_en.HasValue) return _en.Value;
                var args = System.Environment.GetCommandLineArgs();
                for (var i = 0; i < args.Length - 1; i++)
                    if (args[i] == "-lang") { _en = args[i + 1].ToLowerInvariant().StartsWith("en"); return _en.Value; }
                var l = Application.systemLanguage;
                _en = !(l == SystemLanguage.Russian || l == SystemLanguage.Ukrainian || l == SystemLanguage.Belarusian);
                return _en.Value;
            }
        }

        public static string T(string ru)
        {
            if (!En) return ru;
            return Map.TryGetValue(ru, out var en) ? en : ru;
        }

        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            // ---- титул / поток ----
            { "гоняй по РЕАЛЬНОМУ крипто-графику", "ride the REAL crypto chart" },
            { "▶ ИГРАТЬ", "▶ PLAY" }, { "ЗАБЕГ · ", "RUN · " },
            { "≣ ТЕРМИНАЛ — монета · плечо · лонг/шорт", "≣ TERMINAL — coin · leverage · long/short" },
            { "⚑ ПУТЬ ТРЕЙДЕРА", "⚑ TRADER'S PATH" }, { "20 трасс · боссы-крахи", "20 tracks · crash bosses" },
            { "◷ РЫНОК СЕГОДНЯ", "◷ MARKET TODAY" }, { " · попытки ", " · tries " }, { " · стрик ", " · streak " },
            { "◆ ЗАКРЕПИТЬ", "◆ LOCK IN" }, { " → ◆ навсегда", " → ◆ forever" }, { "? КАК ИГРАТЬ", "? HOW TO PLAY" },
            { "рекорд ", "best " }, { " м   ·   лучший фикс $", " m   ·   best cash-out $" },
            { "▣ ГАРАЖ", "▣ GARAGE" }, { "▣ БАЙКИ", "▣ BIKES" }, { "⚙ НАСТРОЙКИ", "⚙ SETTINGS" },
            { "◆ ЗАКРЕПЛЕНО ◆+", "◆ LOCKED IN ◆+" }, { " (навсегда)", " (forever)" }, { " НОВЫЙ РАНГ: ", " NEW RANK: " },
            // ---- how to ----
            { "КАК ИГРАТЬ", "HOW TO PLAY" },
            { "ОСЕДЛАЙ РЕАЛЬНЫЙ ГРАФИК", "RIDE THE REAL CHART" }, { "едешь по живому графику крипто-монеты", "you ride a live crypto price chart" },
            { "ПЛЕЧО МНОЖИТ ДВИЖЕНИЕ", "LEVERAGE MULTIPLIES THE MOVE" }, { "цена вверх × плечо = позиция растёт", "price up × leverage = position grows" },
            { "ЗАФИКСЬ ДО ВОЛНЫ", "CASH OUT BEFORE THE WAVE" }, { "фиксь на пампе, пока волна не догнала", "take profit on the pump before the wave catches you" },
            { "УПРАВЛЕНИЕ", "CONTROLS" }, { "держи ГАЗ · НОС↑ = вилли · НОС↓ = прижать", "hold GAS · NOSE↑ = wheelie · NOSE↓ = tuck" },
            { "теряешь — только от волны/краша, не от цены", "you only lose to the wave or a crash — never to price" },
            { "$ — деньги заезда (риск)   ·   ◆ — гемы навсегда (ранг)", "$ — run money (at risk)   ·   ◆ — gems forever (rank)" },
            { "игра · вся валюта виртуальная · не финансовый совет", "a game · all currency is virtual · not financial advice" },
            { "▶ ВЫБРАТЬ МОНЕТУ", "▶ PICK A COIN" }, { "позже", "later" },
            // ---- терминал ----
            { "ВЫБЕРИ МОНЕТУ", "PICK A COIN" }, { "спарклайн = реальный график = превью трассы", "sparkline = real chart = track preview" },
            { "‹ НАЗАД", "‹ BACK" }, { "←  НАЗАД", "←  BACK" }, { "загрузка…", "loading…" },
            { "НИЗК", "LOW" }, { "СРЕД", "MID" }, { "ВЫС", "HIGH" }, { "ЭКСТРИМ", "EXTREME" },
            { "плечо · тап", "leverage · tap" }, { "▲ ЛОНГ", "▲ LONG" }, { "▼ ШОРТ", "▼ SHORT" },
            { "профит на пампе", "profit on pumps" }, { "профит на дампе", "profit on dumps" }, { "▶ ГОНКА", "▶ RACE" },
            { "ЗАГРУЖАЮ ", "LOADING " }, { "реальные свечи · 5 минут · Binance", "real candles · 5 min · Binance" },
            // ---- заезд ----
            { " м   ", " m   " }, { " км/ч", " km/h" }, { "м", "m" },
            { "$ ЗАФИКСИТЬ $", "$ CASH OUT $" }, { "$ ЗАБЕРИ $", "$ TAKE IT $" }, { "▲ ПИК! ЗАБЕРИ $", "▲ PEAK! TAKE $" },
            { "⚡ РЫВОК", "⚡ BOOST" }, { "ФИЛ: ", "FEEL: " }, { "ЦЕЛЬ ✓  +◆", "GOAL ✓  +◆" },
            { "$ ЗАФИКСИРОВАНО  +$", "$ CASHED OUT  +$" }, { "⚑ ГРАФИК ", "⚑ CHART " }, { " ПРОЙДЕН", " COMPLETE" },
            { "ВОЛАТИЛЬНОСТЬ ", "VOLATILITY " }, { " (нет связи — BTC)", " (offline — BTC)" }, { "▼ ШОРТ · ", "▼ SHORT · " },
            { "★ МИССИЯ ✓ «", "★ MISSION ✓ «" }, { "проедь 300м", "ride 300m" },
            { "ЗАФИКСИРУЙ профит кнопкой $", "CASH OUT profit with the $ button" },
            { "раскачай позицию до $40 — просто едь дальше", "grow your position to $40 — just keep riding" },
            { "ПАУЗА", "PAUSED" }, { "▶ ПРОДОЛЖИТЬ", "▶ RESUME" }, { "≡ МЕНЮ", "≡ MENU" },
            // ---- итог ----
            { "$ ПРОФИТ ЗАФИКСИРОВАН", "$ PROFIT CASHED OUT" }, { "ЛИКВИДИРОВАН ДАМПОМ", "LIQUIDATED BY THE DUMP" },
            { "ОПРОКИНУЛСЯ", "LOOPED OUT" }, { "ЧЕРЕЗ РУЛЬ", "OVER THE BARS" }, { "УПАЛ В ПРОПАСТЬ", "FELL INTO THE VOID" }, { "РАЗБИЛСЯ", "CRASHED" },
            { " — дерзко, рынок не прощает", " — bold; the market does not forgive" },
            { "волна дампа догнала — газуй раньше", "the dump wave caught you — gas earlier" },
            { "разбился — мягче приземляйся", "you crashed — land softer" }, { "надо было ЗАФИКСИТЬ", "should have CASHED OUT" },
            { "м  ·  рекорд ", "m  ·  best " }, { "осталось  $ ", "left  $ " }, { "цели заезда  +◆", "run goals  +◆" },
            { "◈ качай ЩИТ ОТ ВОЛНЫ — оторвёшься от дампа", "◈ upgrade WAVE SHIELD — outrun the dump" },
            { "◎ качай СЦЕПЛЕНИЕ — прощает кувырки", "◎ upgrade GRIP — forgives flips" },
            { "◎ качай СЦЕПЛЕНИЕ — мягче посадки", "◎ upgrade GRIP — softer landings" },
            { "⚙ качай ДВИЖОК — быстрее волны", "⚙ upgrade ENGINE — faster than the wave" },
            { "⚑ ВЫЗОВ ПРОЙДЕН ", "⚑ CHALLENGE DONE " }, { "⚑ ВЫЗОВ НЕ ПРОЙДЕН — ещё разок", "⚑ CHALLENGE FAILED — one more go" },
            { "РЫНОК СЕГОДНЯ · попытка ", "MARKET TODAY · try " }, { "/3 · лучшее $", "/3 · best $" }, { " за стрик", " streak bonus" },
            { "↻ ЕЩЁ РАЗ", "↻ AGAIN" }, { "3/3 — завтра новая трасса", "3/3 — new track tomorrow" },
            { "нет связи — дейли недоступен", "offline — daily unavailable" },
            // ---- гараж ----
            { "▣ качаешь: ", "▣ upgrading: " }, { "  до ", "  to " }, { "  ВЕРШИНА", "  TOP RANK" },
            { "пока без бонуса", "no bonus yet" }, { "след. ур: ", "next: " }, { "МАКС", "MAX" }, { "▸ КУПИТЬ", "▸ BUY" }, { "мало $", "not enough $" },
            { "ДВИЖОК", "ENGINE" }, { "СЦЕПЛЕНИЕ", "GRIP" }, { "ПОДВЕСКА", "SUSPENSION" }, { "СИЛА PUMP", "PUMP POWER" },
            { "ЩИТ ОТ ВОЛНЫ", "WAVE SHIELD" }, { "ВОЗД.КОНТРОЛЬ", "AIR CONTROL" }, { "МАГНИТ", "MAGNET" },
            { "% к скорости", "% speed" }, { "% хватка (подъём+посадка)", "% grip (climb+landing)" }, { "% мягкая посадка", "% softer landing" },
            { "% длина рывка", "% boost length" }, { "% скорость дампа", "% dump speed" }, { "% верчение", "% rotation" }, { "% радиус сбора", "% pickup radius" },
            // ---- байки ----
            { "СКОР", "SPEED" }, { "ХВАТ", "GRIP" }, { "⚙ прокачка этого байка: ", "⚙ this bike's upgrades: " }, { " · качай в ГАРАЖЕ", " · upgrade in GARAGE" },
            { "✓ ВЫБРАН", "✓ SELECTED" }, { "⊘ РАНГ ", "⊘ RANK " }, { "РАЗБЛОКИРОВАТЬ  $", "UNLOCK  $" },
            { "закрепляй $→◆ до ранга ", "lock in $→◆ up to rank " }, { "СТОК", "STOCK" }, { "надеть", "equip" }, { "сначала разблокируй байк", "unlock the bike first" },
            { "ВЕЛИК", "BICYCLE" }, { "МОПЕД", "MOPED" }, { "СКУТЕР", "SCOOTER" }, { "ЭНДУРО 125", "ENDURO 125" }, { "КРОСС 250", "MOTOCROSS 250" },
            { "МОТАРД 450", "MOTARD 450" }, { "СУПЕРБАЙК 650", "SUPERBIKE 650" },
            { "учебка · медленный, лёгкий", "trainer · slow, light" }, { "дворовый · цепкий, шустрее", "backyard · grippy, quicker" },
            { "стабильный · прощает посадки", "stable · forgives landings" }, { "резвый универсал", "lively all-rounder" },
            { "сбалансированный зверь", "balanced beast" }, { "тяга-монстр · нервный", "torque monster · twitchy" }, { "быстрый + вкопанный", "fast + planted" },
            { "ЗОЛОТО", "GOLD" }, { "КАРБОН", "CARBON" }, { "НЕОН", "NEON" }, { "СТЕЛС", "STEALTH" },
            { "КРЕВЕТКА", "SHRIMP" }, { "КРАБ", "CRAB" }, { "РЫБА", "FISH" }, { "ДЕЛЬФИН", "DOLPHIN" }, { "АКУЛА", "SHARK" }, { "КИТ", "WHALE" },
            // ---- путь ----
            { "ранг ", "rank " }, { "ВЫЗОВ ДНЯ", "DAILY CHALLENGE" }, { "ВЫЗОВ ДНЯ · ", "DAILY CHALLENGE · " },
            { "выполнен — завтра новый", "done — new one tomorrow" }, { "  ·  раз в день", "  ·  once a day" },
            { "Заработать на ", "Earn on " }, { "Доехать: ", "Reach the end: " }, { " · ДЕНЬ", " · DAILY" },
            { "Тихий вторник", "Quiet Tuesday" }, { "Тихий", "Quiet" }, { "Штиль", "Calm" }, { "Прогрев", "Warm-up" }, { "Дрейф", "Drift" },
            { "Раскачка", "Swing" }, { "Сползание", "Slide" }, { "Сполз.", "Slide" }, { "Ралли", "Rally" }, { "Импульс", "Impulse" },
            { "Боковик", "Sideways" }, { "Памп +11%", "Pump +11%" }, { "Памп+11", "Pump+11" }, { "КРАХ FTX", "FTX CRASH" }, { "Качели", "Seesaw" },
            { "Памп +9%", "Pump +9%" }, { "Памп+9", "Pump+9" }, { "Бычий шторм", "Bull Storm" }, { "Шторм", "Storm" },
            { "АВГУСТ-ОБВАЛ", "AUGUST CRASH" }, { "АВГ −16%", "AUG −16%" }, { "DOGE-безумие", "DOGE mania" }, { "PEPE: ланч", "PEPE: launch" }, { "BONK обвал", "BONK crash" },
            { "BTC · сен 2019", "BTC · Sep 2019" }, { "ETH · май 2023", "ETH · May 2023" }, { "DOGE · фев 2024", "DOGE · Feb 2024" }, { "BTC · янв 2022", "BTC · Jan 2022" },
            { "SOL · сен 2023", "SOL · Sep 2023" }, { "ETH · авг 2022", "ETH · Aug 2022" }, { "BTC · ноя 2024", "BTC · Nov 2024" }, { "SOL · ноя 2021", "SOL · Nov 2021" },
            { "XRP · дек 2021", "XRP · Dec 2021" }, { "LINK · авг 2021", "LINK · Aug 2021" }, { "BNB · июн 2021", "BNB · Jun 2021" }, { "AVAX · ноя 2021", "AVAX · Nov 2021" },
            { "WIF · мар 2024", "WIF · Mar 2024" }, { "ETH · май 2021", "ETH · May 2021" }, { "DOGE · май 2021", "DOGE · May 2021" }, { "PEPE · май 2023", "PEPE · May 2023" },
            // ---- миссии ----
            { "Проехать ", "Ride " }, { "Собрать $", "Collect $" }, { "Разгон до ", "Reach " }, { "Позиция $", "Position $" },
            // ---- настройки ----
            { "ЗВУК: ВКЛ", "SOUND: ON" }, { "ЗВУК: ВЫКЛ", "SOUND: OFF" }, { "тап — переключить", "tap to switch" },
            { "✕ СБРОСИТЬ ПРОГРЕСС", "✕ RESET PROGRESS" }, { "✓ ПРОГРЕСС ОБНУЛЁН", "✓ PROGRESS RESET" },
            { "CHART RUNNER v1.0\nигра · вся валюта виртуальная · не финансовый совет\nкотировки: Binance API", "CHART RUNNER v1.0\na game · all currency is virtual · not financial advice\nquotes: Binance API" },
            { "БАЛАНС", "BALANCE" }, { "СРЕДНЕ", "MEDIUM" }, { "ОСТРО", "SHARP" },
            // ---- сок ----
            { "САЛЬТО ×", "FLIP ×" }, { "BIG AIR  +$", "BIG AIR  +$" }, { "CLEAN +$2", "CLEAN +$2" },
            { "ПЕРВЫЙ ФЛИП", "FIRST FLIP" }, { "3 ФЛИПА ЗА ЗАБЕГ", "3 FLIPS IN A RUN" }, { "1000м ЗА ЗАБЕГ", "1000m IN A RUN" },
            { "БАНК $10K", "BANK $10K" }, { "ФИКС НА ПЛЕЧЕ ×50+", "CASH-OUT AT ×50+ LEVERAGE" }, { "ЗАРАБОТАЛ НА ПАДЕНИИ", "PROFITED ON A DUMP" },
            { "ПЕРВЫЙ СКИН", "FIRST SKIN" }, { "СТРИК 3 ДНЯ", "3-DAY STREAK" }, { "ОГРОМНЫЙ ПОЛЁТ", "HUGE AIR" },
            { "$ ПОЗИЦИЯ РАСТЁТ — жми «ЗАФИКСИТЬ» вверху: банкуй профит, пока ДАМП не догнал", "$ POSITION IS GROWING — tap CASH OUT up top: bank profit before the DUMP catches you" },
            { "◀ ДАМП НАСТИГАЕТ — ГАЗУЙ, НЕ ГЛОХНИ", "◀ THE DUMP IS CLOSING IN — GAS, DON'T STALL" },
            { "▲ КРУТО? ОТПУСТИ ГАЗ — НЕ ОПРОКИНЕШЬСЯ", "▲ TOO STEEP? EASE OFF THE GAS — YOU WON'T LOOP" },
            { "✦ В ВОЗДУХЕ: НОС↓ К СКЛОНУ ДЛЯ РОВНОЙ ПОСАДКИ", "✦ IN THE AIR: NOSE↓ TOWARD THE SLOPE FOR A CLEAN LANDING" },
            { "$ ДОЛИВАЕШЬСЯ ПО ХОДУ: дальше едешь = крупнее позиция · монетки доливают быстрее", "$ YOU KEEP BUYING IN: the further you ride, the bigger the position · coins add faster" },
            { "▲ ЦЕНА НА ПИКЕ — фиксируй ЗДЕСЬ = больше $ («продай на верхах»)", "▲ PRICE AT A PEAK — cash out HERE = more $ (sell the top)" },
            { "⚡ PUMP ЗАРЯЖЕН — жми кнопку слева", "⚡ PUMP CHARGED — tap the button on the left" },
            { "ДОСТИЖЕНИЕ", "ACHIEVEMENT" },
            { "ГАЗ", "GAS" }, { "ТОРМОЗ", "BRAKE" }, { "НОС↑", "NOSE↑" }, { "НОС↓", "NOSE↓" },
        };
    }
}
