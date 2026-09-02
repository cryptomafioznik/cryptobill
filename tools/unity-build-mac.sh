#!/usr/bin/env bash
# Мак-сборка с ЧЕСТНОЙ проверкой результата.
#
# Зачем: 2026-09-02 лицензия Unity истекла, batchmode выходил без сборки, а я читал
# «BUILD RESULT: PASS» из СТАРОГО Logs/build-mac.txt и снимал кадры со старого бинаря.
# Час работы ушёл на проверку того, чего не существовало. Отчёт — не доказательство;
# доказательство — свежий бинарь.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
U="$ROOT/unity"
UNITY=/Applications/Unity/Hub/Editor/6000.3.20f1/Unity.app/Contents/MacOS/Unity
BIN="$U/build/mac/ChartRunner.app/Contents/MacOS/ChartRunner"
REPORT="$U/Logs/build-mac.txt"
LOG="$U/Logs/build-mac-last.log"

rm -f "$REPORT"
START=$(date +%s)
"$UNITY" -batchmode -quit -projectPath "$U" -buildTarget OSXUniversal \
  -executeMethod ChartRunner.EditorTools.DeviceBuilder.BuildMac -logFile "$LOG" >/dev/null 2>&1

if grep -q "No valid Unity Editor license" "$LOG"; then
  echo "СБОРКА НЕ ЗАПУСТИЛАСЬ: нет лицензии Unity — войди в Unity Hub"; exit 2; fi
if grep -q "error CS" "$LOG"; then
  echo "ОШИБКИ КОМПИЛЯЦИИ:"; grep "error CS" "$LOG" | sed 's/.*Assets\///' | sort -u | head -20; exit 3; fi
if [ ! -f "$REPORT" ] || ! grep -q "BUILD RESULT: PASS" "$REPORT"; then
  echo "СБОРКА ПРОВАЛЕНА (отчёт отсутствует или не PASS):"; tail -5 "$LOG"; exit 4; fi
MT=$(stat -f %m "$BIN" 2>/dev/null || echo 0)
if [ "$MT" -lt "$START" ]; then
  echo "СТАРЫЙ БИНАРЬ: $(date -r "$MT" '+%b %e %H:%M') — отчёт PASS, но файл не обновился"; exit 5; fi
echo "BUILD OK: бинарь $(date -r "$MT" '+%b %e %H:%M'), $(( $(date +%s) - START )) с"
