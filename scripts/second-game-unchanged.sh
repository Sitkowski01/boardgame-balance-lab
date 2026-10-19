#!/bin/sh
# Etap 10: druga prawdziwa gra (Mozaika) weszla bez zmiany jednej linii poza gra, katalogiem i frontem.
# Porownuje etap 9 (przed Mozaika) z etapem 10 — albo z HEAD, dopoki tagu etap-10 jeszcze nie ma.
# Pozniejsze etapy moga juz zmieniac te projekty: dowod dotyczy tego, jak weszla nowa gra.
set -eu

if ! git rev-parse -q --verify "refs/tags/etap-9" >/dev/null; then
  echo "Brak tagu etap-9 (wypchnij go: git push origin etap-9)." >&2
  exit 2
fi

end=$(git rev-parse -q --verify "refs/tags/etap-10" >/dev/null && echo etap-10 || echo HEAD)
unchanged="src/BalanceLab.Engine src/BalanceLab.Bots src/BalanceLab.Simulation src/BalanceLab.Reporting \
  src/BalanceLab.Persistence src/BalanceLab.Multiplayer src/BalanceLab.Api src/BalanceLab.Cli"

# git diff --exit-code: 0 = bez zmian, 1 = sa zmiany, wiecej = blad samego gita (to nie to samo co zmiany).
code=0
# shellcheck disable=SC2086 # lista katalogow ma sie rozdzielic na argumenty
git diff --exit-code --stat etap-9 "$end" -- $unchanged || code=$?
case $code in
  0) echo "Od etap-9 do $end: silnik, boty, symulator, raport, dziennik, pokoje, serwer i CLI bez zmian." ;;
  1) echo "Od etap-9 do $end zmienil sie kod, ktory nowa gra miala zostawic w spokoju (wyzej)." >&2; exit 1 ;;
  *) echo "git diff nie zadzialal (kod $code)." >&2; exit "$code" ;;
esac
