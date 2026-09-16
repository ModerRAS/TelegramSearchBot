#!/bin/sh
# Self-test for the shared sensitive-content patterns: leaked shapes must match,
# redacted placeholders must not, and every leaked sample line must be caught.
# Samples are assembled from fragments so this script does not trip the hooks itself.
# Run: sh .githooks/selftest.sh
set -e
dir="$(dirname "$0")"
patterns="$dir/sensitive-patterns.txt"

if [ ! -f "$patterns" ]; then
    echo "selftest FAIL: $patterns missing"
    exit 1
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

{
    printf 'INF LLMAgent %s%s %s started\n' '-100' '1234567890' '<pid>'
    printf 'bot %s:%s\n' '123456789' 'AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsawX'
    printf 'key %s%s\n' 'sk-' 'abcdefghijklmnopqrstuvwxyz123456'
    printf 'github %s%s\n' 'ghp_' 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghij'
    printf 'aws %s%s\n' 'AKIA' 'IOSFODNN7EXAMPLE'
} > "$tmp/leaked.txt"

cat > "$tmp/clean.txt" <<'EOF'
09:59:38 INF  主进程：LLMAgent <chat_id> <pid>已启动
bot <token>
key <key>
EOF

leaked_total=5
leaked_hits="$(grep -cE -f "$patterns" "$tmp/leaked.txt" || true)"
if [ "$leaked_hits" -ne "$leaked_total" ]; then
    echo "selftest FAIL: caught $leaked_hits/$leaked_total leaked samples"
    exit 1
fi

if grep -qE -f "$patterns" "$tmp/clean.txt"; then
    echo "selftest FAIL: redacted sample was flagged"
    grep -nE -f "$patterns" "$tmp/clean.txt"
    exit 1
fi

echo "selftest OK: $leaked_hits/$leaked_total leaked samples caught, redacted sample clean"
