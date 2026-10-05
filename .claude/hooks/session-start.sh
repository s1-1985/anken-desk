#!/bin/bash
# このリポジトリ(C#)を dotnet build / dotnet test できるように、
# Claude Code on the web のリモート環境でのみ .NET SDK をインストールする。
# ローカル(ユーザーPC)での実行や、既にSDKが入っている場合は何もしない(冪等)。
#
# ★Ubuntuのapt版 dotnet-sdk-8.0 は使わない。このリポジトリには net48 (WinForms)
#   ターゲットのプロジェクトが含まれるが、apt版は Microsoft.NET.Sdk.WindowsDesktop
#   のターゲットを含まないビルドで、`UseWindowsForms=true` のプロジェクトが
#   MSB4019(WindowsDesktop.targetsが見つからない)で失敗する。dotnet公式の
#   dotnet-install.sh が配る完全版SDKにはWindowsDesktopのターゲットが含まれており、
#   これならLinux上でもnet48のWinFormsプロジェクトをビルドできる
set -euo pipefail

INSTALL_DIR="/usr/local/share/dotnet"

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

if [ -x "$INSTALL_DIR/dotnet" ] && "$INSTALL_DIR/dotnet" --list-sdks 2>/dev/null | grep -q '^8\.'; then
  echo "export PATH=\"$INSTALL_DIR:\$PATH\"" >> "$CLAUDE_ENV_FILE"
  exit 0
fi

curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 8.0 --install-dir "$INSTALL_DIR"

echo "export PATH=\"$INSTALL_DIR:\$PATH\"" >> "$CLAUDE_ENV_FILE"
