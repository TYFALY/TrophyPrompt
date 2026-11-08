#!/bin/sh
# Builds a native pfdtool for macOS/Linux from source and drops it where
# TrophyPrompt.csproj picks it up: vendor/pfdtool/native/<rid>/pfdtool
#
# Source: https://github.com/bucanero/pfd_sfo_tools (port of flatz's pfdtool),
# pinned to a reviewed commit so builds are reproducible.
#
# Needs: git, make, and zig (any of: zig on PATH, or `pip install ziglang`).
#
# Usage: scripts/build-pfdtool.sh [rid...]
#   rid: linux-x64 linux-arm64 osx-x64 osx-arm64   (default: this machine)

set -eu

PFDTOOL_REPO="${PFDTOOL_REPO:-https://github.com/bucanero/pfd_sfo_tools.git}"
PFDTOOL_REF="${PFDTOOL_REF:-223f5360a6433f1afcaeab404790ba40000189e7}"

root="$(cd "$(dirname "$0")/.." && pwd)"
src="$root/.pfdtool-src"

if [ "$#" -eq 0 ]; then
	case "$(uname -s)-$(uname -m)" in
		Linux-x86_64) set -- linux-x64 ;;
		Linux-aarch64 | Linux-arm64) set -- linux-arm64 ;;
		Darwin-x86_64) set -- osx-x64 ;;
		Darwin-arm64) set -- osx-arm64 ;;
		*) echo "Unsupported host $(uname -s)-$(uname -m); pass a rid explicitly." >&2; exit 1 ;;
	esac
fi

if [ ! -d "$src/.git" ]; then
	git clone --quiet "$PFDTOOL_REPO" "$src"
fi
git -C "$src" fetch --quiet origin "$PFDTOOL_REF"
git -C "$src" checkout --quiet --detach "$PFDTOOL_REF"

if [ -z "${ZIG:-}" ]; then
	if command -v zig >/dev/null 2>&1; then
		ZIG=zig
	else
		ZIG="python3 -m ziglang"
	fi
fi
export ZIG

for rid in "$@"; do
	case "$rid" in
		linux-x64) platform=linux-amd64 ;;
		linux-arm64) platform=linux-arm64 ;;
		osx-x64) platform=macos-amd64 ;;
		osx-arm64) platform=macos-arm64 ;;
		*) echo "Unknown rid: $rid (Windows uses the bundled pfdtool.exe)" >&2; exit 1 ;;
	esac
	(cd "$src" && ./build-all.sh "$platform")
	out="$root/vendor/pfdtool/native/$rid"
	mkdir -p "$out"
	cp "$src/build/$platform/pfdtool" "$out/pfdtool"
	chmod +x "$out/pfdtool"
	echo "==> $out/pfdtool"
done
