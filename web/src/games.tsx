import { useEffect, useState } from "react";
import { api } from "./api/rest";
import { ConnectFourBoard } from "./connectfour/ConnectFourBoard";
import { connectFourView, describeColumn } from "./connectfour/model";
import { KlejnotyBoard } from "./klejnoty/KlejnotyBoard";
import { klejnotySheet, klejnotyView, type CardSheet, type KlejnotySheet } from "./klejnoty/model";
import { describeMove } from "./klejnoty/moves";

/** Plansza dowolnej gry: widok z serwera (jeszcze niesprawdzony) i ruchy odbiorcy. */
export interface BoardProps {
  game: string;
  variant: string | null;
  view: unknown;
  legalMoves: readonly string[];
  seat: number | null;
  names: readonly string[];
  onMove: (move: string) => void;
  /** Pobieranie tabeli zasad (testy podstawiaja wlasne). */
  loadSheet?: (game: string, variant: string | null) => Promise<KlejnotySheet>;
}

/** Boty do dodania przy stole: opis jak w CLI (BotSpec) i nazwa dla gracza. */
export const botsFor = (game: string): { spec: string; label: string }[] =>
  game === "klejnoty"
    ? [
        { spec: "ismcts:300", label: "ISMCTS (mocny)" },
        { spec: "greedy", label: "Zachłanny" },
        { spec: "random", label: "Losowy" },
      ]
    : [
        { spec: "mcts:500", label: "MCTS (mocny)" },
        { spec: "random", label: "Losowy" },
      ];

/** Ruch po ludzku dla listy ruchow; karty Klejnotow opisuje tabela zasad, jesli juz jest. */
export function describe(game: string, move: string, cards?: readonly CardSheet[]): string {
  try {
    return game === "klejnoty" ? describeMove(move, cards) : describeColumn(move);
  } catch {
    return move;
  }
}

const defaultLoad = (game: string, variant: string | null): Promise<KlejnotySheet> => api.rules(game, variant, klejnotySheet);

/** Wybiera plansze po nazwie gry i sprawdza widok jej schematem. */
export function GameBoard(props: BoardProps) {
  if (props.game === "klejnoty") {
    return <KlejnotyLoader {...props} />;
  }

  const view = connectFourView.safeParse(props.view);
  return view.success ? (
    <ConnectFourBoard view={view.data} legalMoves={props.legalMoves} names={props.names} onMove={props.onMove} />
  ) : (
    <BadView />
  );
}

/** Tabela kart wariantu partii (np. karta tansza o 1 w wariancie) — bez niej plansza nie wie, co znacza numery kart. */
export function useSheet(
  game: string,
  variant: string | null,
  load: (game: string, variant: string | null) => Promise<KlejnotySheet> = defaultLoad,
): KlejnotySheet | string | null {
  const [sheet, setSheet] = useState<KlejnotySheet | string | null>(null);
  useEffect(() => {
    let current = true;
    load(game, variant).then(
      (loaded) => {
        if (current) setSheet(loaded);
      },
      (e: unknown) => {
        if (current) setSheet(e instanceof Error && e.message !== "" ? e.message : "Nie udało się pobrać zasad gry.");
      },
    );
    return () => {
      current = false;
    };
  }, [game, variant, load]);
  return sheet;
}

function KlejnotyLoader({ game, variant, view, legalMoves, seat, names, onMove, loadSheet }: BoardProps) {
  const sheet = useSheet(game, variant, loadSheet);
  const parsed = klejnotyView.safeParse(view);
  if (typeof sheet === "string") {
    return <p role="alert">{sheet}</p>;
  }

  if (sheet === null) {
    return <p>Wczytywanie zasad…</p>;
  }

  return parsed.success ? (
    <KlejnotyBoard view={parsed.data} sheet={sheet} legalMoves={legalMoves} seat={seat} names={names} onMove={onMove} />
  ) : (
    <BadView />
  );
}

function BadView() {
  return <p role="alert">Serwer przysłał planszę, której ta wersja strony nie rozumie — odśwież stronę.</p>;
}
