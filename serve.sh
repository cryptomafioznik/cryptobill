#!/usr/bin/env bash
# CRYPTOBILL — сервер для плейтеста на телефоне.
# Запуск:  ./serve.sh            (порт 4173)
#          PORT=4174 ./serve.sh  (другой порт, если 4173 занят)
# Раздаёт весь проект по локальной сети. Открой напечатанный URL на телефоне
# (телефон и Мак — в одной Wi-Fi). Стоп — Ctrl+C.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PORT="${PORT:-4173}"

# --- LAN-IP этого Мака: Wi-Fi en0 → en1 → интерфейс маршрута по умолчанию ---
ip="$(ipconfig getifaddr en0 2>/dev/null || true)"
[ -z "$ip" ] && ip="$(ipconfig getifaddr en1 2>/dev/null || true)"
if [ -z "$ip" ]; then
  dev="$(route -n get default 2>/dev/null | awk '/interface:/{print $2}' || true)"
  [ -n "$dev" ] && ip="$(ipconfig getifaddr "$dev" 2>/dev/null || true)"
fi
[ -z "$ip" ] && ip="???.???.???.???"

url="http://${ip}:${PORT}/"

printf '\n'
printf '════════════════════════════════════════════════\n'
printf '  CRYPTOBILL — сервер запущен\n'
printf '  папка:  %s\n' "$ROOT"
printf '  порт:   %s\n' "$PORT"
printf '\n'
printf '  📱 НА ТЕЛЕФОНЕ (та же Wi-Fi) открой:\n'
printf '       %s\n' "$url"
printf '       (index.html = симлинк на toys/chartrider.html)\n'
printf '\n'
printf '  💻 На этом Маке:  http://localhost:%s/\n' "$PORT"
printf '  ⛔ Стоп:          Ctrl+C\n'
printf '════════════════════════════════════════════════\n'
if command -v qrencode >/dev/null 2>&1; then
  printf '  Скан QR телефоном:\n'
  qrencode -t ANSIUTF8 "$url"
else
  printf '  (для QR-кода: brew install qrencode)\n'
fi
printf '\n'

cd "$ROOT"
exec python3 -m http.server "$PORT" --bind 0.0.0.0
