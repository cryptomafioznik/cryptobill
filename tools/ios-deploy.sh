#!/usr/bin/env bash
# Собирает Xcode-проект, сделанный Unity, и ставит его на телефон.
#
# Порядок и флаги не декоративны:
#   -allowProvisioningUpdates   — Xcode сам заводит профиль под устройство, иначе нужен
#                                 ручной заход в портал разработчика на каждое новое устройство.
#   -allowProvisioningDeviceRegistration + -destination id=UDID — регистрирует ИМЕННО этот
#                                 телефон в профиле; с generic/platform=iOS профиль остаётся старым,
#                                 и установка падает 0xe8008012 «profile cannot be installed on this device».
#   -derivedDataPath            — фиксированный, чтобы путь к .app был предсказуем и чтобы
#                                 повторные сборки переиспользовали кэш, а не начинали с нуля.
#   CODE_SIGN_STYLE=Automatic   — подпись задаётся здесь, а не в проекте: Unity перегенерирует
#                                 проект при каждой полной сборке и настройку в нём затрёт.
#
# Использование:  tools/ios-deploy.sh [udid]   (CONFIG=Debug для отладочной сборки; по умолчанию Release — как в сторе)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$ROOT/unity/build/ios/Unity-iPhone.xcodeproj"
DD="$ROOT/unity/build/ios-dd"
TEAM="R36SPNVQUW"
BUNDLE="com.mathewk.chartrunner"

if [ ! -d "$PROJ" ]; then
  echo "НЕТ Xcode-проекта: $PROJ" >&2
  echo "Сначала: Unity -executeMethod ChartRunner.EditorTools.DeviceBuilder.BuildIos" >&2
  exit 1
fi

UDID="${1:-}"
if [ -z "$UDID" ]; then
  UDID="$(xcrun devicectl list devices 2>/dev/null \
    | awk '/available \(paired\)/ {print $3; exit}')"
fi
if [ -z "$UDID" ]; then
  echo "Не найдено спаренного устройства. Подключи телефон и разблокируй его." >&2
  exit 1
fi
echo "устройство: $UDID"

echo "== xcodebuild =="
xcodebuild \
  -project "$PROJ" \
  -scheme Unity-iPhone \
  -configuration "${CONFIG:-Release}" \
  -destination "id=$UDID" \
  -derivedDataPath "$DD" \
  -allowProvisioningUpdates \
  -allowProvisioningDeviceRegistration \
  DEVELOPMENT_TEAM="$TEAM" \
  CODE_SIGN_STYLE=Automatic \
  build 2>&1 | tail -25

# PRODUCT_BUNDLE_IDENTIFIER здесь НЕ передаётся намеренно. Настройка в командной строке
# xcodebuild применяется ко ВСЕМ таргетам проекта, поэтому UnityFramework.framework получал
# тот же идентификатор, что и приложение, и установка падала:
#   «The parent bundle has the same identifier as sub-bundle .../UnityFramework.framework»
#   (MIInstallerErrorDomain 57, DuplicateIdentifier).
# Идентификаторы по таргетам расставляет сам Unity из PlayerSettings — см. DeviceBuilder.

APP="$(find "$DD/Build/Products" -maxdepth 2 -name "*.app" -type d | head -1)"
if [ -z "$APP" ]; then
  echo "Сборка не дала .app" >&2
  exit 1
fi
echo "собрано: $APP"

echo "== установка =="
xcrun devicectl device install app --device "$UDID" "$APP"

echo "== запуск =="
xcrun devicectl device process launch --device "$UDID" "$BUNDLE" || \
  echo "Приложение установлено; запусти его с домашнего экрана."
