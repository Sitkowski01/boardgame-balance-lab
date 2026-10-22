# Balance Lab

Laboratorium balansu gier planszowych. Zasady gry podpina się jako plugin, boty (MCTS)
rozgrywają tysiące partii, a raport pokazuje, co jest niezbalansowane: przewagę miejsca
przy stole, dominujące strategie, za mocne i martwe karty oraz skutki zmian w zasadach.

**Polski** · [English](README.en.md)

Trzy gry: Czwórki (ConnectFour), Klejnoty (gra karciana w stylu Splendora) i Mozaika (kafelki w stylu Azula);
boty: losowy, heurystyczny (z profilami strategii), MCTS i ISMCTS; symulator turniejów, liga z rankingiem Elo,
porównanie wariantów zasad (A/B), raport HTML, dziennik partii w PostgreSQL z powtórkami i migawkami, serwer gry
na żywo (SignalR) i front w React: lobby, plansze, odtwarzacz powtórek i laboratorium balansu z wykresami.

![Stół Klejnotów: rynek kart, bank żetonów i gracze](docs/screens/klejnoty.png)

![Partia Czwórek z botem MCTS](docs/game.gif)

| Lobby | Mozaika | Laboratorium | Telefon, tryb ciemny |
|---|---|---|---|
| ![Lobby](docs/screens/lobby.png) | ![Mozaika](docs/screens/mozaika.png) | ![Laboratorium](docs/screens/lab.png) | ![Telefon](docs/screens/phone-dark.png) |

```bash
docker compose up        # http://localhost:8080 — lobby, stoły, powtórki i laboratorium
```

Jeden obraz: serwer ASP.NET Core (hub SignalR + REST) serwuje też zbudowany front, obok PostgreSQL na dziennik partii.

## Architektura

```
src/BalanceLab.Engine              kontrakt IGame<TState, TMove>, IPlayer, Match, Pcg32, StableHash
src/BalanceLab.Games.ConnectFour   Czwórki na bitboardach (zna tylko silnik)
src/BalanceLab.Games.Klejnoty      Klejnoty: zasady, ukryte talie i rezerwacje, ocena pozycji
src/BalanceLab.Games.Mozaika       Mozaika: warsztaty, rzędy wzoru, ściana, worek z losowaniem, ocena pozycji
src/BalanceLab.Bots                RandomBot, GreedyBot, MctsBot, IsmctsBot (kontrakt, żadnej gry)
src/BalanceLab.Simulation          turnieje równoległe, statystyki kart, liga (Elo), porównanie A/B, raport JSON
src/BalanceLab.Reporting           raport HTML z wykresami SVG — zna tylko liczby z symulatora
src/BalanceLab.Persistence         dziennik partii (EF Core + PostgreSQL): ruchy tylko dopisywane, migawki, weryfikacja
src/BalanceLab.Catalog             katalog gier i botów (opis tekstem, np. mcts:300) dla CLI i serwera
src/BalanceLab.Multiplayer         pokoje gry (serwer jest sędzią), lobby, archiwum partii — bez sieci
src/BalanceLab.Experiments         laboratorium: kolejka eksperymentów (turniej, liga, A/B) z limitami i postępem — bez sieci
src/BalanceLab.Api                 hub SignalR /hubs/game + REST /api — sam transport, bez zasad gier
src/BalanceLab.Cli                 simulate | league | compare | play | replay
web/                               React + TypeScript: lobby, plansze, powtórki (Vite, SignalR)
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

## Mozaika — druga gra bez zmian w silniku

Gra kafelkowa w stylu Azula: gracze biorą z warsztatów wszystkie kafelki jednego koloru (reszta idzie na
środek), układają je w rzędach wzoru, a pełne rzędy na koniec rundy przenoszą po kafelku na ścianę 5×5,
punktowaną za sąsiedztwo. Nadmiar spada na podłogę i kosztuje punkty; kto pierwszy sięgnie na środek, bierze
znacznik pierwszego gracza. Gra kończy się po rundzie, w której ktoś zapełnił rząd ściany (premie: rząd +2,
kolumna +7, kolor +10). Nazwy kolorów i wygląd są własne.

Mozaika jest testem architektury: weszła jako nowy projekt, jeden wpis w `GameCatalog` i plansza we froncie.
Silnik, boty, symulator, raport, dziennik, pokoje, serwer i CLI nie zmieniły się ani o linię — pilnuje tego
zadanie CI `second-game` (`scripts/second-game-unchanged.sh`: `git diff --exit-code etap-9 etap-10` na tych
katalogach). Raport balansu, liga, porównanie A/B, dziennik z powtórkami i gra na żywo działają dla niej od razu.

- ukryta jest tylko kolejność losowania z worka; stan trzyma ziarno rozdań, a każda runda losuje z własnego
  strumienia `Pcg32.Create(ziarno, runda)` — dlatego `Determinize` podmienia samo ziarno, a migawka w dzienniku
  to zwykły JSON bez stanu generatora;
- `MozaikaVariant`: `tiles=N; floor=1,2,2,3; row=N; column=N; color=N` (np. `--variant "column=5"`);
- elementami dla raportu są pola ściany pogrupowane po kolorze — raport pokazuje, czy któreś pole daje
  wyraźnie więcej wygranych niż inne pola tego samego koloru;
- testy właściwości: 100 kafelków nigdy nie powstaje ani nie znika (worek + pudełko + stół + rzędy + podłogi
  + ściany), znacznik jest dokładnie w jednym miejscu, powtórka ruchów daje ten sam hash.

Ciekawostka z testów siły: w Mozaice prosty `GreedyBot` (ocena pozycji z `MozaikaEvaluator`, profile
`kolumny`, `kolory`, `ostrozny`) wygrywa z ISMCTS(300) — losowe dogrywki do końca partii są tu za słabą
oceną, bo punkty przychodzą dopiero na koniec rundy. MCTS i ISMCTS biją za to bota losowego.

```bash
dotnet run -c Release --project src/BalanceLab.Cli -- league --game mozaika --bots greedy greedy:kolumny greedy:ostrozny --games 300 --html mozaika.html
```

Po etapie 10 raport nazywa elementy tak, jak podaje gra (`IItemTracking.Noun`): „martwa karta” w Klejnotach,
„martwe pole ściany” w Mozaice. Zmienione dopiero w etapie 11, żeby nie psuć dowodu „bez zmian”.

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

## Dziennik partii i powtórki

Partia w bazie to nagłówek (gra, wariant, ziarno, gracze) i ruchy — nic więcej nie trzeba, bo cała
losowość siedzi w stanie gry. Obok każdego ruchu leży hash stanu po nim, a co 20 ruchów (i na końcu
partii) migawka całego stanu.

```bash
export BALANCE_LAB_DB="Host=localhost;Username=postgres;Password=...;Database=balance_lab"
balance-lab play   --game klejnoty --bots greedy ismcts:30 random --seed 7
balance-lab replay --match <id> --at 45
```

```
Partia 01a14fc3-...: Klejnoty, ziarno 7, gracze: greedy, ismcts:30, random
Status: Finished, ruchow: 106; wyniki: 1, 0, 0
    1. take:WRK         a17052d3f3eae9a6
    2. take:WGR         f718dc631c4f6861
    3. reserve:73       c913f3b569131d2a
  ...
Stan po ruchu 45: hash 304b63b872bcd647
Weryfikacja: zgodna (106 ruchow, 6 migawek)
```

- **Ruchy jako tekst należący do gry** (`IMoveCodec`: `take:WRK`, `buy:17`, `deck:2`), a nie JSON z
  serializatora — zmiana nazwy pola w C# nie unieważnia zapisanych partii. Migawka (`IStateCodec`) to JSON
  w kolumnie `jsonb`, czytelny prosto z bazy (Czwórki zapisują planszę w wierszach `..XO...`).
- **Tylko dopisywanie pilnuje baza**: trigger odrzuca `UPDATE`, `DELETE` i `TRUNCATE` na ruchach i migawkach,
  a zamkniętej partii nie da się zmienić ani usunąć — także ręcznym SQL-em.
- **Dwa zapisy tego samego ruchu**: dopisujący podaje, ile ruchów widział; licznik ruchów w nagłówku jest
  znacznikiem współbieżności, a klucz (partia, numer ruchu) drugą zaporą. Z ośmiu równoległych prób
  przechodzi dokładnie jedna, reszta dostaje `JournalConflictException` — pod serwer gry w czasie rzeczywistym.
- **Stan po ruchu N** = najbliższa wcześniejsza migawka + najwyżej 19 ruchów powtórzonych przez zasady gry.
  Migawka jest sprawdzana z łańcuchem hashy ruchów, nie tylko z własnym hashem — podmieniona para
  (stan, hash) nie przejdzie jako prawdziwa.
- **Weryfikacja** powtarza całą partię od rozdania i wskazuje pierwszy ruch, na którym powtórka rozchodzi się
  z dziennikiem. To też alarm na zmianę zasad gry: stare partie przestają się zgadzać od konkretnego ruchu.
  Odczyt idzie w jednej transakcji REPEATABLE READ, więc równoległe dopisywanie nie daje fałszywych rozjazdów.

Testy na prawdziwym PostgreSQL (Testcontainers; każdy test dostaje kopię bazy-szablonu) psują dziennik
z pominięciem triggerów — zmieniony hash, inny legalny ruch, luka w numeracji, podmieniona migawka,
zły wynik w nagłówku — i sprawdzają, że każda zmiana jest wykryta we właściwym miejscu.

## Laboratorium balansu (dashboard)

`/lab` w przeglądarce: gra, rodzaj eksperymentu, boty (z profilami strategii danej gry), liczba partii, ziarno
i wariant zasad. Serwer liczy eksperyment w tle, strona pokazuje postęp i wyniki pod własnym adresem
(`/lab/{id}` — da się odświeżyć albo wysłać komuś link).

- **Turniej:** przewaga miejsca przy stole i siła botów z 95% przedziałami (▲▼, gdy cały przedział leży po jednej
  stronie uczciwego udziału), wykres punktowy elementów gry — za mocne, za słabe i martwe karty albo pola.
- **Liga:** ranking Elo względem 1500 i macierz „kto bije kogo” (skala rozbieżna z wartością w każdej komórce).
- **Co jeśli:** różnice wariant − oryginał z przedziałami, liczone z tych samych rozdań; istotne zmiany wyraźne,
  w granicach przypadku — przygaszone.

Każdy wykres ma podpowiedź po najechaniu i przy fokusie z klawiatury oraz tabelę z liczbami; oceny niosą znak
i słowo, nie tylko kolor; kolory mają osobne wartości dla trybu ciemnego. Wykres punktowy rysuje się w
szerokości ekranu, więc na telefonie napisy mają normalną wielkość.

Pod spodem `BalanceLab.Experiments`: zamówienie jest sprawdzane od razu (zły bot albo wariant to 400 z powodem),
czeka w kolejce i liczy się na połowie rdzeni, jeden eksperyment naraz; limity partii, botów i iteracji MCTS,
pełna kolejka to 503. Wynik jest dokładnie ten sam co z CLI dla tych samych botów i ziarna (test porównuje JSON).
Ocena elementów (`ItemVerdict`) jest liczona w symulatorze i trafia do raportu — przeglądarka, HTML i CLI oceniają
tak samo.

```bash
# serwer uruchomiony jak w „Gra na żywo” niżej (ASPNETCORE_URLS=http://localhost:5080; bez tego dotnet run słucha na :5000)
curl -X POST localhost:5080/api/experiments -H 'Content-Type: application/json' \
  -d '{"game":"mozaika","kind":"League","bots":["greedy","greedy:kolumny","random"],"games":40,"seed":"1"}'
curl localhost:5080/api/experiments/{id}     # status, postęp (done/total) i wynik
```

## Gra na żywo (SignalR)

```bash
export BALANCE_LAB_DB="Host=localhost;Username=postgres;Password=...;Database=balance_lab"
export ASPNETCORE_URLS=http://localhost:5080   # bez tego dotnet run słucha na :5000
dotnet run --project src/BalanceLab.Api      # migracje przy starcie, hub na /hubs/game
```

Gospodarz zakłada pokój (`CreateRoom`: gra, liczba miejsc, wariant zasad), inni dołączają (`JoinRoom`),
gospodarz sadza boty (`AddBot(miejsce, "mcts:300")`) i zaczyna (`Start`) — wolne miejsca zajmują boty.
Klient dostaje `Room` (miejsca, kto na ruchu, termin), `View` (gra ze swojego miejsca i legalne ruchy, gdy
jest na ruchu) i `Move`; ruch wysyła jako tekst z numerem pozycji: `MakeMove(12, "buy:17")`.

- **Serwer jest sędzią.** Stan gry istnieje tylko w pokoju; ruch przechodzi przez `Apply` gry, trafia do
  dziennika w PostgreSQL i dopiero potem widzą go gracze. Ruch do nieaktualnej pozycji (numer) jest odrzucany.
- **Pokój to aktor.** Prośby graczy, ruchy botów i upływ czasu idą przez jedną kolejkę (`Channel`) i jedną
  pętlę — bez blokad. Wysyłka do klientów ma własną kolejkę, więc wolny klient nie wstrzymuje ruchów i zegarów.
- **Ukryta informacja.** Każde miejsce dostaje własny widok (`IPlayerView`): kartę wziętą w ciemno widzi tylko
  właściciel, reszta stołu i widzowie — jej rewers. Test własności: widok nie zmienia się, gdy tasujemy to,
  czego obserwator nie widzi. Ziarno i hashe partii REST pokazuje dopiero po jej końcu.
- **Boty** liczą poza pętlą, na widoku swojego miejsca, z ziarnem z partii i numeru ruchu — ten sam pokój
  z tym samym ziarnem rozgrywa się identycznie. Bot z lobby ma limit iteracji, żeby nikt nie zamówił
  nieskończonego MCTS na serwerze.
- **Partia nie stoi.** Po upływie czasu na ruch albo gdy gracz rozłączy się na swoim ruchu (po 10 s karencji —
  odświeżenie strony to też rozłączenie), gra za niego bot.
  Powrót z biletem miejsca (`Rejoin`) przywraca widok; gracz, który wrócił przed ruchem bota, gra sam.
  Gdy dziennik kilka razy z rzędu nie przyjmie ruchu, partia jest porzucana zamiast ponawiać bez końca.
- **REST:** `/api/games`, `/api/games/{gra}/rules` (karty wariantu dla ekranu), `/api/rooms`, `/api/matches/{id}` (ruchy i weryfikacja powtórki),
  `/api/matches/{id}/frames/{n}` (widok widza po n ruchach — klatka odtwarzacza). Limit żądań na adres IP.

Testy huba idą przez prawdziwe połączenia WebSocket z serwerem w pamięci (`WebApplicationFactory`) i
PostgreSQL z Testcontainers; e2e: dwóch graczy i bot kończą partię Klejnotów, a powtórka z dziennika daje
te same hashe. Logika pokoi ma osobne testy bez sieci, z fałszywym zegarem (`FakeTimeProvider`).

## W przeglądarce (React)

```bash
(cd web && pnpm install && pnpm build)
export ASPNETCORE_URLS=http://localhost:5080 Web__Root=web/dist
dotnet run --project src/BalanceLab.Api    # gra i front pod localhost:5080
(cd web && pnpm dev)                      # albo front z odświeżaniem na :5173 (proxy do API na :5080)
```

Lobby pokazuje pokoje na żywo i zakłada nowe (gra, miejsca, wariant zasad). Przy stole gospodarz sadza boty
i zaczyna (link do stołu kopiuje się jednym przyciskiem), a każdy gra ze swojego miejsca; widz ogląda bez cudzych
kart. Powtórka `/matches/{id}` przewija partię z dziennika suwakiem — klatki liczy serwer, przeglądarka je tylko pamięta.

Wygląd jest własny, bez bibliotek komponentów: kolory jako role w CSS z osobnymi wartościami dla trybu ciemnego
(za systemem albo z przełącznika, wybór zostaje w przeglądarce), plansze i ilustracje w SVG i CSS. W Czwórkach
krążek spada do swojego pola, ostatni ruch ma obwódkę, a zwycięska czwórka świeci; w Klejnotach karta, którą
stać gracza, jest wyróżniona, a nowa karta na rynku wjeżdża animacją. Animacje wyłącza `prefers-reduced-motion`.

- **Front nie zna zasad.** Klik w żetony i karty składa ruch, ale przycisk „Weź” czy „Kup” pojawia się tylko,
  gdy taki ruch jest na liście legalnych ruchów od serwera. Test własności (fast-check): żadna sekwencja klików
  nie zbuduje ruchu spoza listy, a każdy legalny da się wyklikać.
- **Liczby z serwera.** Widok gry podaje numery kart; koszt i punkty wariantu tej partii daje
  `GET /api/games/{gra}/rules?variant=...` (`IRulesSheet` w silniku), więc „karta tańsza o 1” wygląda
  na ekranie tak, jak gra się w nią na serwerze.
- **Kolejność wiadomości.** Każdy stan pokoju i widok ma wersję — starsza niż posiadana jest pomijana.
  Każda wiadomość z sieci przechodzi przez schemat (zod), zanim trafi do stanu.
- **Powrót po odświeżeniu.** Bilet miejsca jest w `sessionStorage` tej karty: odświeżenie siada z powrotem
  (`Rejoin`), a druga karta to osobny gracz. Po zerwaniu sieci klient sam łączy się i siada ponownie; bilet
  zapomina tylko wtedy, gdy odrzuci go serwer, a nie gdy padnie sieć.
- **Jeden ruch na pozycję.** Po kliknięciu plansza czeka na nowy widok — drugi klik nie wyśle ruchu do starej
  pozycji. Powtórka pyta o klatkę dopiero, gdy suwak stanie, więc przeciąganie nie zasypuje serwera.
- **Architektura** (dependency-cruiser): logika (stan stołu, ruchy, powtórka, kontrakt z serwerem) nie importuje
  Reacta; klient SignalR zna tylko `api/hub.ts`, a reszta rozmawia przez interfejs, który testy podmieniają.

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
dotnet test                 # jednostkowe, właściwości (FsCheck), statystyczne, siła botów, architektura,
                            # integracyjne, hub SignalR i e2e na PostgreSQL (Testcontainers — potrzebny Docker)
dotnet tool restore
dotnet stryker              # mutation testing silnika, próg 90%
dotnet stryker -f stryker-connectfour.json
dotnet stryker -f stryker-klejnoty.json
dotnet stryker -f stryker-mozaika.json
dotnet stryker -f stryker-experiments.json
dotnet stryker -f stryker-bots.json
dotnet stryker -f stryker-simulation.json
dotnet stryker -f stryker-reporting.json
dotnet stryker -f stryker-cli.json
dotnet stryker -f stryker-persistence.json
dotnet stryker -f stryker-catalog.json
dotnet stryker -f stryker-multiplayer.json
dotnet stryker -f stryker-api.json
dotnet run -c Release --project benchmarks/BalanceLab.Benchmarks -- --filter '*'

cd web
pnpm check                  # typy, format, lint, architektura, testy (Vitest + Testing Library + fast-check)
pnpm mutation               # Stryker, próg 90%
pnpm e2e                    # Playwright: prawdziwy serwer gry + PostgreSQL (Testcontainers albo BALANCE_LAB_DB)
```

Na Windowsie z Visual Studio 2022 Stryker może wybrać MSBuild bez .NET 10 — wtedy
`dotnet stryker --msbuild-path "C:\Program Files\dotnet\sdk\<wersja>\MSBuild.dll"`.

## Stack

.NET 10, ASP.NET Core + SignalR, EF Core + PostgreSQL (Npgsql), System.CommandLine, System.Text.Json, xUnit, FsCheck,
Testcontainers, NetArchTest, Stryker.NET, BenchmarkDotNet; front: React 19, TypeScript, Vite, @microsoft/signalr, zod,
Vitest, Testing Library, fast-check, Playwright, dependency-cruiser, StrykerJS.

## Licencja

MIT — [LICENSE](LICENSE).
