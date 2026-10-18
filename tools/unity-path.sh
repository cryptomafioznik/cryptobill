#!/usr/bin/env bash
# Где лежит Unity-редактор. Подключать через `source`; даёт find_unity.
#
# С 2026-10-18 редакторы стоят на внешнем SSD (Unity Hub: install-path), нативные arm64.
# Порядок: $UNITY_PATH → версия из ProjectVersion.txt в корнях ниже, arm64 раньше Intel.
# Причина: два месяца сборки шли через Intel-редактор под Rosetta, и никто не видел, какой
# бинарь запущен, — поэтому find_unity печатает выбранный путь и архитектуру в stderr.

UNITY_ROOTS=("/Volumes/Projects SSD/games/Unity/Editor" "/Applications/Unity/Hub/Editor")

find_unity() {
  if [[ -n "${UNITY_PATH:-}" && -x "$UNITY_PATH" ]]; then echo "$UNITY_PATH"; return 0; fi
  local root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
  local version
  version="$(awk '/^m_EditorVersion:/ {print $2}' "$root/unity/ProjectSettings/ProjectVersion.txt" 2>/dev/null)"
  local fallback="" r c arch
  for r in "${UNITY_ROOTS[@]}"; do
    c="$r/$version/Unity.app/Contents/MacOS/Unity"
    [[ -x "$c" ]] || continue
    arch="$(file "$c" | grep -oE 'arm64|x86_64' | head -1)"
    if [[ "$arch" == "arm64" ]]; then
      echo "Unity: $c (arm64)" >&2; echo "$c"; return 0
    fi
    [[ -z "$fallback" ]] && fallback="$c"
  done
  if [[ -n "$fallback" ]]; then
    echo "Unity: $fallback (x86_64 под Rosetta — нативного нет; подключён ли SSD?)" >&2
    echo "$fallback"; return 0
  fi
  echo "Unity $version не найден в: ${UNITY_ROOTS[*]}. Подключи SSD или задай UNITY_PATH." >&2
  return 1
}
