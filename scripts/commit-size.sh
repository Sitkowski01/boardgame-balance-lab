#!/usr/bin/env bash
# Liczy linie w staged diffie (dodane + usuniete), pomijajac pliki generowane.
# Uzycie: commit-size.sh [limit]   — kod wyjscia 1, gdy limit przekroczony.
set -euo pipefail
LIMIT="${1:-${MAX_COMMIT_LINES:-200}}"
IGNORE='(^|/)(packages\.lock\.json|pnpm-lock\.yaml|\.gitignore|Migrations/.*\.Designer\.cs|Migrations/.*ModelSnapshot\.cs|Golden/.*\.html)$'   # pliki generowane (dotnet new gitignore, pnpm, dotnet ef, migawki testow)

# Najpierw do zmiennej: blad gita w podstawieniu procesu < <(...) byl cicho ignorowany i dawal "0 razem".
staged=$(git diff --cached --numstat) || { echo "git diff nie zadzialal" >&2; exit 2; }

total=0
while IFS=$'\t' read -r added deleted path; do
  [[ -z "$added" ]] && continue                    # nic nie jest w indeksie (pusta linia)
  [[ "$added" == "-" ]] && continue                 # plik binarny
  [[ "$path" =~ $IGNORE ]] && continue
  total=$((total + added + deleted))
  printf '%5d  %s\n' "$((added + deleted))" "$path"
done <<< "$staged"

printf -- '-----\n%5d  razem (limit %d)\n' "$total" "$LIMIT"
if (( total > LIMIT )); then
  echo "Commit za duzy — podziel go na mniejsze." >&2
  exit 1
fi
