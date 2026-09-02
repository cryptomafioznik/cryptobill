#!/usr/bin/env bash
# Архив для App Store: Xcode-проект Unity → .xcarchive → .ipa (method app-store-connect).
# Загрузку в App Store Connect делает пользователь (Transporter / Xcode Organizer /
# `xcrun altool --upload-app` с его API-ключом) — нужны его учётные данные Apple.
#
# Использование: tools/ios-archive.sh   → unity/build/ios-archive/ChartRunner.ipa
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$ROOT/unity/build/ios/Unity-iPhone.xcodeproj"
OUT="$ROOT/unity/build/ios-archive"
TEAM="R36SPNVQUW"
[ -d "$PROJ" ] || { echo "НЕТ Xcode-проекта — сначала DeviceBuilder.BuildIos"; exit 1; }
mkdir -p "$OUT"

cat > "$OUT/ExportOptions.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>method</key><string>app-store-connect</string>
  <key>teamID</key><string>$TEAM</string>
  <key>signingStyle</key><string>automatic</string>
  <key>uploadSymbols</key><true/>
  <key>destination</key><string>export</string>
</dict></plist>
PLIST

echo "== archive =="
xcodebuild -project "$PROJ" -scheme Unity-iPhone -configuration Release \
  -destination "generic/platform=iOS" -archivePath "$OUT/ChartRunner.xcarchive" \
  -allowProvisioningUpdates DEVELOPMENT_TEAM="$TEAM" CODE_SIGN_STYLE=Automatic \
  archive 2>&1 | tail -6

echo "== export ipa =="
xcodebuild -exportArchive -archivePath "$OUT/ChartRunner.xcarchive" \
  -exportOptionsPlist "$OUT/ExportOptions.plist" -exportPath "$OUT" \
  -allowProvisioningUpdates 2>&1 | tail -6

ls -la "$OUT"/*.ipa 2>/dev/null && echo "IPA готов: $OUT — загрузить через Transporter или Xcode Organizer"
