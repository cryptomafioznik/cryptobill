#!/usr/bin/env bash
# Загрузка .ipa в App Store Connect через API-ключ (без пароля, ключ уже на маке).
# Использование: tools/ios-upload.sh   (после tools/ios-archive.sh)
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IPA="$ROOT/unity/build/ios-archive/ChartRunner.ipa"
[ -f "$IPA" ] || { echo "нет $IPA — сначала tools/ios-archive.sh"; exit 1; }
KEY=$(python3 -c "import json,os;print(json.load(open(os.path.expanduser('~/.appstoreconnect/config.json')))['keyId'])")
ISS=$(python3 -c "import json,os;print(json.load(open(os.path.expanduser('~/.appstoreconnect/config.json')))['issuerId'])")
xcrun altool --upload-app -f "$IPA" -t ios --apiKey "$KEY" --apiIssuer "$ISS" 2>&1 | grep -v "^$" | tail -8
