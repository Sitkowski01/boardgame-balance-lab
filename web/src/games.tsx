import { useEffect, useState, type ReactNode } from "react";
import { z } from "zod";
import { api } from "./api/rest";
import type { SeatInfo } from "./api/schemas";
import { ConnectFourBoard } from "./connectfour/ConnectFourBoard";
import { connectFourView, describeColumn } from "./connectfour/model";
import { KlejnotyBoard } from "./klejnoty/KlejnotyBoard";
import { klejnotySheet, klejnotyView, type CardSheet } from "./klejnoty/model";
import { describeMove } from "./klejnoty/moves";
import { MozaikaBoard } from "./mozaika/MozaikaBoard";
import { mozaikaSheet, mozaikaView } from "./mozaika/model";
import { describeMove as describeMozaika } from "./mozaika/moves";

/** Plansza dowolnej gry: widok z serwera (jeszcze niesprawdzony) i ruchy odbiorcy. */
export interface BoardProps {
  game: string;
  variant: string | null;
  view: unknown;
  legalMoves: readonly string[];
  seat: number | null;
  names: readonly string[];
  /** Miejsca przy stole na zywo (w powtorce brak). */
  seats?: readonly SeatInfo[] | undefined;
  onMove: (move: string) => void;
  /** Pobieranie tabeli zasad (testy podstawiaja wlasne); sprawdza ja dopiero schemat planszy tej gry. */
  loadSheet?: (game: string, variant: string | null) => Promise<unknown>;
}

/** Boty do dodania przy stole: opis jak w CLI (BotSpec) i nazwa dla gracza. */
export const botsFor = (game: string): { spec: string; label: string }[] => {
  switch (game) {
    case "klejnoty":
      return [
        { spec: "ismcts:300", label: "ISMCTS (mocny)" },
        { spec: "greedy", label: "Zachłanny" },
        { spec: "random", label: "Losowy" },
      ];
    case "mozaika":
      // W Mozaice prosta ocena pozycji bije ISMCTS z losowymi dogrywkami (MozaikaBotTests, test sily).
      return [
        { spec: "greedy", label: "Zachłanny (mocny)" },
        { spec: "ismcts:300", label: "ISMCTS" },
        { spec: "random", label: "Losowy" },
      ];
    default:
      return [
        { spec: "mcts:500", label: "MCTS (mocny)" },
        { spec: "random", label: "Losowy" },
      ];
  }
};

/** Ruch po ludzku dla listy ruchow; karty Klejnotow opisuje tabela zasad, jesli juz jest. */
export function describe(game: string, move: string, cards?: readonly CardSheet[]): string {
  try {
    switch (game) {
      case "klejnoty":
        return describeMove(move, cards);
      case "mozaika":
        return describeMozaika(move);
      default:
        return describeColumn(move);
    }
  } catch {
    return move;
  }
}

const defaultLoad = (game: string, variant: string | null): Promise<unknown> => api.rules(game, variant, z.unknown());
const noSheet = (): Promise<unknown> => Promise.resolve(null);
const cardsSheet = klejnotySheet.nullable();

/**
 * Opis ruchow partii dla listy ruchow. Klejnoty opisuja karte jej kolorem i punktami z tabeli zasad wariantu —
 * sam numer karty nic graczowi nie mowi; bez tabeli (jeszcze nie przyszla albo blad) zostaje numer.
 */
export function useDescribe(
  game: string | null,
  variant: string | null,
  load: (game: string, variant: string | null) => Promise<unknown> = defaultLoad,
): (move: string) => string {
  const sheet = useSheet(game ?? "", variant, cardsSheet, game === "klejnoty" ? load : noSheet);
  const cards = sheet !== null && typeof sheet !== "string" ? sheet.cards : undefined;
  return (move) => describe(game ?? "", move, cards);
}

/** Wybiera plansze po nazwie gry i sprawdza widok jej schematem. */
export function GameBoard(props: BoardProps) {
  switch (props.game) {
    case "klejnoty":
      return (
        <WithSheet key={props.game} {...props} sheetSchema={klejnotySheet} viewSchema={klejnotyView}>
          {(view, sheet) => <KlejnotyBoard view={view} sheet={sheet} {...boardOf(props)} />}
        </WithSheet>
      );
    case "mozaika":
      return (
        <WithSheet key={props.game} {...props} sheetSchema={mozaikaSheet} viewSchema={mozaikaView}>
          {(view, sheet) => <MozaikaBoard view={view} sheet={sheet} {...boardOf(props)} />}
        </WithSheet>
      );
  }

  const view = connectFourView.safeParse(props.view);
  return view.success ? (
    <ConnectFourBoard view={view.data} legalMoves={props.legalMoves} names={props.names} onMove={props.onMove} />
  ) : (
    <BadView />
  );
}

const boardOf = ({ legalMoves, seat, names, seats, onMove }: BoardProps) => ({ legalMoves, seat, names, seats, onMove });

/** Gry, ktorych plansza ma wlasny panel kazdego gracza — lista miejsc obok stolu powtarzalaby go w trakcie partii. */
export const hasPlayerPanels = (game: string): boolean => game === "klejnoty" || game === "mozaika";

/** Tabela zasad wariantu partii (np. karta tansza o 1 w wariancie) — bez niej plansza nie wie, co znacza numery. */
export function useSheet<T>(
  game: string,
  variant: string | null,
  schema: z.ZodType<T>,
  load: (game: string, variant: string | null) => Promise<unknown> = defaultLoad,
): T | string | null {
  // Tabela pamieta, dla jakiej gry i wariantu przyszla: po zmianie wariantu stara nie trafia do nowej planszy.
  const [loaded, setLoaded] = useState<{ game: string; variant: string | null; sheet: T | string } | null>(null);
  useEffect(() => {
    let current = true;
    const set = (sheet: T | string): void => {
      if (current) setLoaded({ game, variant, sheet });
    };
    load(game, variant).then(
      (data) => {
        const parsed = schema.safeParse(data);
        set(parsed.success ? parsed.data : "Serwer przysłał zasady gry, których ta wersja strony nie rozumie.");
      },
      (e: unknown) => {
        set(e instanceof Error && e.message !== "" ? e.message : "Nie udało się pobrać zasad gry.");
      },
    );
    return () => {
      current = false;
    };
  }, [game, variant, schema, load]);
  return loaded?.game === game && loaded.variant === variant ? loaded.sheet : null;
}

interface WithSheetProps<V, S> extends BoardProps {
  sheetSchema: z.ZodType<S>;
  viewSchema: z.ZodType<V>;
  children: (view: V, sheet: S) => ReactNode;
}

/** Plansza gry z tabela zasad: najpierw tabela, potem widok sprawdzony schematem tej gry. */
function WithSheet<V, S>({ game, variant, view, loadSheet, sheetSchema, viewSchema, children }: WithSheetProps<V, S>) {
  const sheet = useSheet(game, variant, sheetSchema, loadSheet);
  const parsed = viewSchema.safeParse(view);
  if (typeof sheet === "string") {
    return <p role="alert">{sheet}</p>;
  }

  if (sheet === null) {
    return <p>Wczytywanie zasad…</p>;
  }

  return parsed.success ? children(parsed.data, sheet) : <BadView />;
}

function BadView() {
  return <p role="alert">Serwer przysłał planszę, której ta wersja strony nie rozumie — odśwież stronę.</p>;
}
