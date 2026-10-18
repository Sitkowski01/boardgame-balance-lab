# Balance Lab

Laboratorium balansu gier planszowych. Zasady gry podpina się jako plugin, boty (MCTS)
rozgrywają tysiące partii, a raport pokazuje, co jest niezbalansowane: przewagę miejsca
przy stole, dominujące strategie, za mocne i martwe karty oraz skutki zmian w zasadach.

> Projekt w budowie. Gotowe: kontrakt silnika z ukrytą informacją, deterministyczny generator,
> Czwórki (ConnectFour) i Klejnoty (gra karciana w stylu Splendora), boty: losowy, heurystyczny
> (z profilami strategii), MCTS i ISMCTS, symulator turniejów, liga z rankingiem Elo, porównanie
> wariantów zasad (A/B) i raport HTML.

## Architektura

```
src/BalanceLab.Engine              kontrakt IGame<TState, TMove>, IPlayer, Match, Pcg32, StableHash
src/BalanceLab.Games.ConnectFour   Czwórki na bitboardach (zna tylko silnik)
src/BalanceLab.Games.Klejnoty      Klejnoty: zasady, ukryte talie i rezerwacje, ocena pozycji
src/BalanceLab.Bots                RandomBot, GreedyBot, MctsBot, IsmctsBot (kontrakt, żadnej gry)
src/BalanceLab.Simulation          turnieje równoległe, statystyki kart, liga (Elo), porównanie A/B, raport JSON
src/BalanceLab.Reporting           raport HTML z wykresami SVG — zna tylko liczby z symulatora
src/BalanceLab.Cli                 simulate | league | compare — jedyne miejsce, które składa gry, boty i symulator
```

Gra to czyste funkcje na niemutowalnym stanie: `Setup(gracze, ziarno)`, `LegalMoves`, `Apply`
(nowy stan), `IsTerminal`, `Scores`, `Determinize` (widok gracza, niżej) i `Hash` — stabilny między procesami (FNV-1a), w przeciwieństwie
do `GetHashCode`. `Match` rozgrywa partię i nie ufa graczom: o legalności ruchu decyduje `Apply` gry.
Reguły zależności pilnuje NetArchTest.

Czwórki trzymają planszę w dwóch liczbach 64-bitowych (kolumna = 6 pól + pusty bit strażnika),
więc ruch to jedno dodawanie, a szukanie czwórki — cztery przesunięcia bitowe. Test różnicowy
porównuje bitboard z prostym sędzią na siatce po każdym ruchu setek losowych partii.

## Szybki start

```bash
dotnet run -c Release --project src/BalanceLab.Cli -- simulate   --game connectfour --bots random random --games 20000 --seed 1 --out report.json
```

```
ConnectFour: 20000 partii, ziarno 1, ukonczone 20000, remisy 55

Uczestnik            wygrane  95% przedzial     sredni wynik
random                 49.2%  48.5% - 49.9%     0.493
random#2               50.6%  49.9% - 51.3%     0.507

Miejsce przy stole   wygrane  95% przedzial     sredni wynik
1                      55.3%  54.6% - 56.0%     0.554
2                      44.4%  43.7% - 45.1%     0.446

Dlugosc partii (ruchy): srednio 21.3, mediana 21, 90% ponizej 31, od 7 do 42
```

To już jest wynik o balansie: przy losowej grze zaczynający wygrywa w Czwórkach 55% partii,
a przedział ufności wyklucza 50%. Boty: `random`, `mcts`, `mcts:ITERACJE` — po jednym na miejsce
przy stole (`greedy[:PROFIL]` i `ismcts[:ITERACJE]` też). `--games-out` zapisuje każdą partię (ziarno, miejsca, wynik), więc dowolną da się odtworzyć.

## Ukryta informacja i ISMCTS

Bot nigdy nie dostaje prawdziwego stanu. `Match` pokazuje mu `Determinize(stan, gracz, ziarno)`:
to, co gracz widzi, zostaje, a to, czego nie wie (kolejność talii, cudze karty wzięte w ciemno),
jest wylosowane od nowa. Bot nie podejrzy więc talii, nawet gdyby chciał — w tym widoku
prawdziwej talii po prostu nie ma.

Kontrakt wymaga, żeby determinizacja zależała **tylko od wiedzy obserwatora**. Test właściwości
sprawdza to wprost: determinizacja innego układu tego samego zbioru informacji daje bajt w bajt ten sam
stan. Haczyk, który ten test wyłapuje: karty trzeba posortować przed tasowaniem — inaczej wynik
zależałby od prawdziwej kolejności talii.

`IsmctsBot` (Information Set MCTS, Cowling i in. 2012) w każdej iteracji losuje inny układ ukrytych
kart, a wszystkie iteracje budują jedno drzewo indeksowane ruchami. Premia za eksplorację liczy się
od tego, ile razy ruch był *dostępny*, a nie od odwiedzin rodzica — w różnych układach legalne bywają
różne ruchy rywala. Na grze testowej z jedną ukrytą kartą (uproszczony poker Kuhna) dwa stany różniące się
tylko kartą rywala dają identyczne szukanie, a zwykły MCTS na prawdziwym stanie wybiera inaczej.

## Klejnoty

Gra karciana w stylu Splendora: żetony w pięciu kolorach i złoto, 90 kart kopalni na trzech poziomach
(każda daje stały rabat), patroni za zebrane rabaty, koniec po 15 punktach z dogrywaną rundą.
Mechanika jest jak w oryginale, ale nazwy i zestaw kart są własne: każdy wzór kosztu występuje w każdym
kolorze z obróconymi kolorami, więc żaden kolor nie jest z góry lepszy — to punkt odniesienia dla raportu.

- `Gems` trzyma sześć liczników w jednym `ulong` (bajt na rodzaj): dodanie zestawu to jedno dodawanie;
- płacenie automatyczne (kolory, a złoto tylko na brakujące) — boty nie wybierają między równoważnymi płatnościami;
- nadmiar ponad 10 żetonów oddaje się osobnymi ruchami tego samego gracza;
- gdy wszyscy po kolei spasują (nic innego nie jest legalne), partia się kończy zamiast kręcić w kółko;
- testy właściwości po każdym ruchu setek partii na 2–4 graczy: żetony, karty i patroni nie powstają
  ani nie znikają, każdy legalny ruch się wykonuje, tura przechodzi tylko z co najwyżej 10 żetonami.

`KlejnotyEvaluator` ocenia pozycję (punkty > rabaty > żetony, plus postęp ku najlepszej osiągalnej karcie
i patronom) dla `GreedyBot`, który patrzy jeden ruch do przodu. Siła (stałe ziarna, 2 graczy):
greedy wygrywa z losowym co najmniej 75% partii, ISMCTS(300 iteracji) z greedy co najmniej 60%
(progi testów siły; w pomiarach ok. 85% i 80%).

```bash
dotnet run -c Release --project src/BalanceLab.Cli -- simulate --game klejnoty --bots greedy ismcts:200 random --games 300
```

## Symulator turniejów

- **Rotacja miejsc** — w partii *i* uczestnik *k* siada na miejscu (*k* + *i*) mod *n*: każdy zaczyna
  równie często, więc przewaga pierwszego gracza nie przechyla porównania botów, a osobno ją mierzymy.
  Dlatego liczba partii musi być wielokrotnością liczby uczestników.
- **Średni wynik miejsca** (remis = pół wygranej) to uczciwa miara przewagi, gdy są remisy — sam odsetek
  wygranych liczy remis jak przegraną; brak przewagi to 1 / liczba graczy.
- **Wynik nie zależy od liczby wątków** — ziarno partii i każdego bota wyprowadzone jest z ziarna
  turnieju i numeru partii (`StableHash`), a każda partia zapisuje wynik w swojej komórce tablicy.
  Test porównuje raport z 1 wątku i ze wszystkich bajt w bajt.
- **Przedział Wilsona** zamiast `p ± 1,96·σ` — poprawny przy małej liczbie partii i odsetkach blisko 0 lub 100%.
- Partie przerwane limitem ruchów liczone są osobno i nie wchodzą do odsetków; błąd w partii zgłasza
  jej numer i ziarno, żeby dało się ją powtórzyć — przy kilku błędach zawsze ten o najniższym numerze,
  niezależnie od liczby wątków.
- CLI sprawdza parametry i katalogi wyjściowe przed startem. Kody wyjścia: 0 — sukces,
  1 — symulacja się nie udała, 2 — błędne parametry.
- Raport JSON zapisuje ziarna jako tekst — JavaScript obcina liczby powyżej 2^53.

## Raport balansu

Trzy pytania projektanta, trzy polecenia. Każde wypisuje podsumowanie w konsoli, a z `--html`
zapisuje samodzielną stronę z wykresami (SVG, bez JavaScriptu i zasobów z sieci, tryb jasny i ciemny).

**Czy jakaś prosta strategia dominuje?** `league` rozgrywa każdy profil z każdym. Profile to ta sama
ocena pozycji z innymi wagami: `wyrownany`, `punkty` (od razu drogie karty), `silnik` (najpierw tanie
rabaty), `patroni`, `zetony` (chomikowanie).

```bash
balance-lab league --game klejnoty --bots greedy:wyrownany greedy:punkty greedy:silnik greedy:patroni greedy:zetony --games 400 --html liga.html
```

```
Uczestnik                 Elo  sredni wynik
greedy:silnik          1622.8         0.675
greedy:patroni         1601.0         0.642
greedy:wyrownany       1556.7         0.572
greedy:punkty          1507.5         0.494
greedy:zetony          1212.0         0.113
```

Elo jest dopasowane do wszystkich partii naraz modelem Bradleya-Terry'ego (algorytm MM), a nie
liczone partia po partii — klasyczne Elo zależałoby od kolejności partii, której w symulacji nie ma.
Test sprawdza warunek największej wiarygodności: dla każdego gracza oczekiwana liczba punktów równa się
zdobytej. Każda para ma ziarno z własnego numeru, więc dopisanie bota nie zmienia partii pozostałych par.

**Które karty są martwe albo za mocne?** `simulate` liczy dla każdej karty, w jakiej części partii ktoś
ją kupił i jak często wygrał jej właściciel. Pierwsza wersja porównywała kartę ze średnią wszystkich kart
i oznaczyła 58 z 90 kart jako „za mocne” — same tanie karty z poziomu 1, które kupuje każdy, podczas gdy
drogie kupuje głównie ten, kto już prowadzi. Dlatego gra podaje grupę elementu (`IItemTracking.ItemGroup`,
tu: poziom karty), a karta jest oceniana tylko na tle swojej grupy i dopiero wtedy, gdy cały 95% przedział
odstaje o więcej niż 3 punkty procentowe:

```
Karty: wlasciciel przecietnej karty wygrywa (poziom 1 39.5%, poziom 2 33.4%, poziom 3 44.4%); odstaje 8 z 90
  za mocna   #51 poz.2 U 3 pkt, koszt U6              brana  26.7%, wlasciciel wygrywa 40.0%
  za slaba   #7 poz.1 W 1 pkt, koszt K4               brana  63.3%, wlasciciel wygrywa 34.7%
  martwa     #77 poz.3 U 5 pkt, koszt W7 U3           brana   4.8%, wlasciciel wygrywa 56.6%
  ...
```

To korelacja, nie dowód (silny gracz kupuje też dobre karty) — wynik mówi, co sprawdzić wariantem.

**Co jeśli zmienię zasady?** `compare` gra ten sam turniej na oryginale i na wariancie
(`--variant "card77.points=4; win=13"`: koszt i punkty kart, wymagania patronów, próg punktów).
Obie strony grają z tych samych ziaren, więc różnica bierze się ze zmiany zasad, a nie z innego
rozdania. Różnice odsetków mają przedział Newcombe'a (składa przedziały Wilsona; test odtwarza przykład
z jego pracy), gwiazdka = przedział nie obejmuje zera:

```
Klejnoty -> Klejnoty (card77.points=4): 6000 partii na strone, ziarno 1
miejsce 1            +0.3 pp (-1.4 - +2.0)
greedy:silnik        +0.7 pp (-1.1 - +2.5)
dlugosc partii       -0.1 ruchu (-0.3 - +0.2)
Karty zmienione istotnie: 1 z 90
```

Tu odpowiedź brzmi: punkt mniej na karcie #77 nie rusza balansu miejsc ani strategii.

Wykresy raportu HTML pilnują testy zatwierdzonych plików (`tests/BalanceLab.Reporting.Tests/Golden`):
strona ze stałych danych porównywana znak po znaku z zatwierdzoną. Celowa zmiana wyglądu to
`BALANCE_LAB_APPROVE=1 dotnet test`, obejrzenie strony i commit — diff pokazuje dokładnie, co się zmieniło.

## Bot MCTS

`MctsBot` to Monte Carlo Tree Search z wyborem UCT. Nie zna strategii żadnej gry — tylko zasady:
rozgrywa losowe partie od kandydujących ruchów i coraz częściej sprawdza te, które wygrywają
(UCB1: średni wynik + premia za rzadko sprawdzane ruchy). Każdy węzeł zbiera wynik gracza, który
wykonał prowadzący do niego ruch, więc ten sam kod działa dla 2, 3 i 4 graczy.

- budżet: limit iteracji (powtarzalny) i/lub limit czasu;
- logarytm w UCB liczony samymi +, ×, ÷ (`DeterministicMath`) — `Math.Log` korzysta z biblioteki
  systemu i potrafi różnić się ostatnim bitem między Windowsem a Linuksem, a wtedy ten sam turniej
  dałby inny raport na innym komputerze;
- `Search` zwraca statystyki wszystkich ruchów (odwiedziny, średni wynik) — przydadzą się w raporcie;
- testy siły ze stałymi ziarnami: idealna gra w Nim („zostaw wielokrotność 4”), wygrana w jednym
  ruchu i blokowanie groźby w Czwórkach, 200/200 wygranych z botem losowym, 1000 iteracji kontra
  50 iteracji: 59:1;
- test pokazujący, po co eksploracja: bez niej (c = 0) MCTS potrafi utknąć na przegrywającym ruchu
  z pewną siebie średnią 0,99.

## Determinizm

Każda partia musi dać się odtworzyć z ziarna — na tym opierają się powtórki z dziennika
ruchów i porównania wariantów zasad. Dlatego silnik ma własny generator
[PCG32](https://www.pcg-random.org/) zamiast `System.Random`:

- niemutowalny (`readonly record struct`) — jest częścią stanu gry, losowanie zwraca nowy generator;
- zgodny z implementacją referencyjną (test na wektorze z `pcg32-demo`);
- `NextInt(bound)` bez przesunięcia (odrzuca niepełną końcówkę zakresu 2^32);
- tasowanie Fishera-Yatesa, wyniki zamrożone testami — stara powtórka zawsze daje tę samą partię.

Test architektury pilnuje, że ani silnik, ani gry, ani boty nie sięgają po `System.Random`, zegar systemowy ani `Guid`.
(Jedyny wyjątek to opcjonalny limit czasu MCTS na `Stopwatch` — z nim wynik zależy od szybkości maszyny.)
To samo ziarno daje tę samą partię, a ruchy z zapisu odtwarzają stan o identycznym hashu.

## Wydajność

BenchmarkDotNet, 1000 losowań z przedziału [0, 6):

| Metoda | Czas | Alokacje |
|---|---:|---:|
| `System.Random.Next(6)` | 9,1 µs | 0 B |
| `Pcg32.NextInt(6)` | 2,1 µs | 0 B |
| `Pcg32.Shuffle` (90 kart) | 0,45 µs | 384 B |

Losowa partia Czwórek od pustej planszy do końca (playout, jak w MCTS): **0,56 µs**,
czyli ok. 1,8 mln partii na sekundę na rdzeń. Legalne ruchy nie alokują pamięci: dla każdej
z 128 masek wolnych kolumn lista jest zbudowana raz.

Ruch MCTS z pustej planszy Czwórek:

| Iteracje | Czas | Alokacje |
|---:|---:|---:|
| 1 000 | 0,54 ms | 139 KB |
| 10 000 | 6,4 ms | 1,4 MB |

czyli ok. 1,6–1,9 mln iteracji na sekundę na rdzeń; alokacje to głównie nowe węzły drzewa (jeden na iterację),
lista dzieci powstaje dopiero przy pierwszym rozwinięciu węzła.

Klejnoty: losowa partia na 2 graczy (pętla jak w symulacji MCTS) to ok. **100 µs**, czyli ok. 10 000
partii na sekundę na rdzeń (135 KB alokacji — niemutowalny stan z `ImmutableArray`; kandydat do
optymalizacji), a determinizacja w środku partii **1,1 µs** — ukrycie informacji kosztuje ISMCTS ok. 1% czasu iteracji.

Turniej 2000 partii MCTS(50) kontra bot losowy: 341 ms na 1 wątku, 66 ms na 16 wątkach
logicznych (5,1×). Skalowanie nie jest liniowe — ograniczają je rdzenie fizyczne i odśmiecanie pamięci
(MCTS tworzy węzeł na iterację).

## Testy

```bash
dotnet test                 # jednostkowe, właściwości (FsCheck), statystyczne, siła botów, architektura, e2e CLI
dotnet tool restore
dotnet stryker              # mutation testing silnika, próg 90%
dotnet stryker -f stryker-connectfour.json
dotnet stryker -f stryker-klejnoty.json
dotnet stryker -f stryker-bots.json
dotnet stryker -f stryker-simulation.json
dotnet stryker -f stryker-reporting.json
dotnet stryker -f stryker-cli.json
dotnet run -c Release --project benchmarks/BalanceLab.Benchmarks -- --filter '*'
```

Na Windowsie z Visual Studio 2022 Stryker może wybrać MSBuild bez .NET 10 — wtedy
`dotnet stryker --msbuild-path "C:\Program Files\dotnet\sdk\<wersja>\MSBuild.dll"`.

## Stack

.NET 10, System.CommandLine, System.Text.Json, xUnit, FsCheck, NetArchTest, Stryker.NET, BenchmarkDotNet.

## Licencja

MIT — [LICENSE](LICENSE).
