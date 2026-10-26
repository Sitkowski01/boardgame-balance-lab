import type { GameInfo } from "./api/schemas";
import { plural } from "./plural";

/**
 * Nazwy dla gracza. Serwer nazywa gry i boty po swojemu (ConnectFour, greedy:kolory, ismcts:300) — te same
 * nazwy sa w dziennikach i raportach, wiec zostaja; strona pokazuje je po polsku.
 */
const titles: Record<string, string> = { ConnectFour: "Czwórki" };

/** Nazwa gry na stronie; wariant zasad dopisuje serwer do nazwy, wiec gry z wariantami zostaja bez zmian. */
export function gameTitle(name: string): string {
  return titles[name] ?? name;
}

/** Imie miejsca przy stole: bot po polsku, czlowiek tak, jak sie wpisal; puste miejsce bez imienia. */
export function seatName(seat: { kind: "Human" | "Bot" | "Empty"; name: string | null }): string | null {
  return seat.kind === "Bot" ? playerName(seat.name ?? "") : seat.name;
}

/** Krotki opis gry na karcie w lobby. */
export const gameBlurbs: Record<string, string> = {
  klejnoty: "Zbieraj żetony, kupuj karty kopalń i przyciągaj patronów. Kto pierwszy do 15 punktów — w stylu Splendora.",
  mozaika: "Bierz kafelki z warsztatów i układaj ścianę tak, żeby nic nie spadło na podłogę — w stylu Azula.",
  connectfour: "Cztery krążki w linii: krótka partia na rozgrzewkę z botem MCTS, który nie wybacza.",
};

/** Zasady w trzech zdaniach dla poczekalni: czas na lekture, zanim gospodarz zacznie partie. */
export const gameRules: Record<string, readonly string[]> = {
  connectfour: [
    "Na zmianę wrzucacie krążki do jednej z siedmiu kolumn — krążek spada na najniższe wolne pole.",
    "Wygrywa, kto pierwszy ułoży cztery w linii: poziomo, pionowo albo po skosie.",
    "Pełna plansza bez czwórki to remis.",
  ],
  klejnoty: [
    "W ruchu bierzesz żetony (trzy różne albo dwa takie same), kupujesz kartę albo rezerwujesz ją ze złotem; żetonów najwyżej 10.",
    "Kupione karty dają punkty i stały rabat w swoim kolorze, więc kolejne są tańsze; patron przychodzi sam, gdy masz jego rabaty.",
    "Kto pierwszy dojdzie do 15 punktów, kończy grę — runda dogrywa się do końca.",
  ],
  mozaika: [
    "Bierzesz wszystkie kafelki jednego koloru z warsztatu (reszta idzie na środek) albo ze środka.",
    "Kładziesz je na jeden rząd wzoru; to, co się nie zmieści, spada na podłogę i odbiera punkty.",
    "Pełny rząd wzoru przenosi kafelek na ścianę i daje punkty za sąsiadów; pełny rząd ściany kończy grę.",
  ],
};

const botKinds: Record<string, string> = { greedy: "Zachłanny", random: "Losowy", mcts: "MCTS", ismcts: "ISMCTS" };
const botName = /^(greedy|random|mcts|ismcts)(?::(.+))?$/u;

/** Bot po polsku: greedy:kolory -> "Zachłanny: kolory", mcts:500 -> "MCTS 500"; inne imiona bez zmian. */
export function playerName(name: string): string {
  const match = botName.exec(name);
  if (match === null) {
    return name;
  }

  const kind = botKinds[match[1] ?? ""] ?? name;
  const detail = match[2];
  if (detail === undefined) {
    return kind;
  }

  return /^\d+$/u.test(detail) ? `${kind} ${detail}` : `${kind}: ${detail}`;
}

/** Jak gra bot (z opisu, np. "mcts:500"), jednym zdaniem dla gracza; null dla innych imion. */
export function botStyle(name: string): string | null {
  const match = botName.exec(name);
  const detail = match?.[2];
  switch (match?.[1]) {
    case "random":
      return "Bot: wybiera ruchy na chybił trafił.";
    case "greedy":
      return detail === undefined
        ? "Bot: bierze ruch, który od razu daje najwięcej."
        : `Bot: bierze ruch, który od razu daje najwięcej (styl: ${detail}).`;
    case "mcts":
      return `Bot: przed każdym ruchem rozgrywa w pamięci ${detail ?? "1000"} szybkich partii.`;
    case "ismcts":
      return `Bot: zgaduje ukryte karty i rozgrywa w pamięci ${detail ?? "1000"} partii na ruch.`;
    default:
      return null;
  }
}

/** Kolory klejnotow w zapisie serwera (Gems.ToString): przymiotnik karty i formy liczby przy koszcie. */
const gems: Record<string, { card: string; forms: [string, string, string] }> = {
  W: { card: "biała", forms: ["biały", "białe", "białych"] },
  U: { card: "niebieska", forms: ["niebieski", "niebieskie", "niebieskich"] },
  G: { card: "zielona", forms: ["zielony", "zielone", "zielonych"] },
  R: { card: "czerwona", forms: ["czerwony", "czerwone", "czerwonych"] },
  K: { card: "czarna", forms: ["czarny", "czarne", "czarnych"] },
};
const tiers = ["", "I", "II", "III"];
const klejnotyCard = /^#(\d+) poz\.([123]) ([WUGRK]) (\d+) pkt, koszt ((?:[WUGRK]\d+ ?)+)$/u;

/**
 * Element gry w raporcie laboratorium. Karte Klejnotow serwer opisuje skrotem ("#17 poz.1 U 0 pkt, koszt W1 G1"),
 * bo ten sam opis idzie do raportow CLI; strona rozpisuje go slowami. Inne elementy zostaja bez zmian.
 */
export function itemName(name: string): string {
  const card = klejnotyCard.exec(name);
  if (card === null) {
    return name;
  }

  const [, id, tier, bonus, points, cost] = card as unknown as [string, string, string, string, string, string];
  const parts = cost
    .trim()
    .split(" ")
    .map((part) => {
      const n = Number(part.slice(1));
      const forms = gems[part.charAt(0)]?.forms ?? ["", "", ""];
      return `${String(n)} ${plural(n, ...forms)}`;
    });
  return `#${id} · ${tiers[Number(tier)] ?? tier} · ${gems[bonus]?.card ?? bonus}, ${points} pkt · koszt: ${parts.join(", ")}`;
}

/** "3 gry, od 2 do 4 graczy" z katalogu serwera. */
export function gamesFact(games: readonly GameInfo[]): string {
  const n = games.length;
  const noun = plural(n, "gra", "gry", "gier");
  const min = Math.min(...games.map((g) => g.minPlayers));
  const max = Math.max(...games.map((g) => g.maxPlayers));
  return `${String(n)} ${noun}, ${min === max ? `${String(min)} graczy` : `od ${String(min)} do ${String(max)} graczy`}`;
}
