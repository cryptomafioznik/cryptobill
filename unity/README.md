# ChartRunner — Unity-проект

Создан 2026-08-06, пункт 3 плана `docs/UNITY-MIGRATION-START-HERE.md` §3.
Unity **6000.3.20f1**, шаблон «2D (Cross-Platform)», URP 17.3.0 с 2D-рендерером.

Пакеты подняты Unity с версий шаблона до совместимых: URP 17.0.3 → **17.3.0**,
Input System 1.12.0 → **1.19.0**, Test Framework 1.4.5 → **1.6.0**. Зафиксированы в
`Packages/packages-lock.json`.

## Как проверять без человека

MCP-моста к Unity нет, всё через batchmode. Из корня репозитория:

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.3.20f1/Unity.app/Contents/MacOS/Unity
```

**Проверка состояния проекта** (единицы, физика, пайплайн, папки, сборки):

```bash
/Applications/Unity/Hub/Editor/6000.3.20f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath "$PWD/unity" -executeMethod ChartRunner.EditorTools.ProjectBootstrap.Verify -logFile -
```

**Негативный контроль** — доказывает, что проверка вообще способна провалиться:

```bash
/Applications/Unity/Hub/Editor/6000.3.20f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath "$PWD/unity" -executeMethod ChartRunner.EditorTools.ProjectBootstrap.NegativeControl -logFile -
```

`Verify` — exit 0 при успехе, exit 1 при любом провале.
`NegativeControl` — exit 0 если подмена гравитации была **поймана**, exit 1 если нет.

## Почему проверки написаны именно так

`-executeMethod` завершается кодом 0 при пустом теле метода, поэтому **компиляция не есть
проверка** (`docs/TECH_DEBT.md` §3.1). Отсюда два правила в `ProjectBootstrap`:

1. Каждая проверка печатает проверяемое утверждение с фактическим значением и способна
   провалиться. При любом провале — `EditorApplication.Exit(1)`.
2. Настройки (гравитация, фикс-шаг) выставляются **не** этим скриптом, а прямой правкой
   `ProjectSettings/*.asset` до запуска Unity. Проверка читает их через рантайм-API.
   Механизм установки и механизм проверки разные — иначе тест подтверждал бы сам себя.

Отдельно про `NegativeControl`: он восстанавливает гравитацию **до** вызова `Exit`, а не в
`finally`. `EditorApplication.Exit` убивает процесс, `finally` не выполняется, и подменённое
значение осталось бы записанным в `Physics2DSettings.asset` — то есть контроль испортил бы
проект. Проверено: после прогона в YAML снова `-26.46`.

## Настройки, отличающиеся от дефолтных Unity

| Настройка | Дефолт Unity | Здесь | Почему |
|---|---|---|---|
| `Physics2D.gravity.y` | −9.81 | **−26.46** | контракт единиц, вариант A — `docs/BIKE_PHYSICS_SPEC.md` §1.3 |
| `Fixed Timestep` | 0.02 | **0.016666668** | один шаг = один кадр исходной симуляции |

Гравитация −26.46 м/с² = 2.70 g. Это **намеренно**: геометрический масштаб (база 52 px ≡
1470 мм) и динамический расходятся в 2.70 раза, и вариант A сохраняет фил без перекалибровки.
Переключение на честные 9.81 — одно число в `UnitsContract.GravityMPerS2` плюс перекалибровка
по §7 спеки.

## Структура

```
Assets/ChartRunner/
  Runtime/     Bike · Track · Input · Telemetry · Tuning     [ChartRunner.Runtime]
  Editor/      Bootstrap — точки входа -executeMethod        [ChartRunner.Editor]
  Tests/       PlayMode · EditMode                           [ChartRunner.Tests.*]
  Profiles/    экземпляры ScriptableObject-профилей (пункт 4)
  Scenes/      сцены, собираемые скриптом (пункт 6)
Assets/Settings/    UniversalRP.asset + Renderer2D.asset (из шаблона, GUID'ы не трогать)
```

`Assets/Scenes/SampleScene.unity` и `Assets/Settings/Scenes/URP2DSceneTemplate.unity` —
из шаблона, пока не используются. Сцены проекта создаются Editor-скриптом (пункт 6),
руками их не собирать: YAML вручную писать нельзя, ошибки не поймать.
