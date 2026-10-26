import { useEffect, useRef, useState } from "react";
import { Gem } from "./Gem";
import { gemName } from "./model";
import { canPick, parseMove, takeMove, toggleGem } from "./moves";

export interface BankProps {
  bank: readonly number[];
  legalMoves: readonly string[];
  onMove: (move: string) => void;
}

/**
 * Bank zetonow: wybor kolorow, "Wez", oddawanie nadmiaru i pas. Zeton da sie wybrac tylko, jesli
 * prowadzi do legalnego ruchu z listy serwera.
 */
export function Bank({ bank, legalMoves, onMove }: BankProps) {
  // Wybor zetonow nalezy do jednej listy legalnych ruchow: nowy widok (np. bot zagral za gracza po czasie)
  // zaczyna wybor od nowa, zamiast zostawic zetony wybrane w innej pozycji.
  const [picked, setPicked] = useState<{ moves: readonly string[]; gems: number[] }>({ moves: legalMoves, gems: [] });
  const selection = picked.moves === legalMoves ? picked.gems : [];
  const setSelection = (gems: number[]): void => {
    setPicked({ moves: legalMoves, gems });
  };
  const take = takeMove(selection, legalMoves);
  const returns = legalMoves.filter((m) => m.startsWith("return:"));
  // Prosba o oddanie zetonu lezy pod rynkiem, czesto pod krawedzia ekranu: bez niej nic sie nie da kliknac,
  // wiec strona sama ja pokazuje (gladko, chyba ze gracz wylaczyl ruch w systemie).
  const returnsBox = useRef<HTMLDivElement>(null);
  const mustReturn = returns.length > 0;
  useEffect(() => {
    if (!mustReturn) return;
    const still = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    returnsBox.current?.scrollIntoView({ block: "nearest", behavior: still ? "auto" : "smooth" });
  }, [mustReturn]);
  const play = (move: string): void => {
    setSelection([]);
    onMove(move);
  };

  return (
    <section aria-label="Bank" className="bank">
      {bank.map((count, gem) => {
        // Zlota nie ma w zadnym ruchu "take" od serwera, wiec canPick blokuje je tak samo jak pusty kolor.
        const pickable = count > 0 && canPick(selection, gem, legalMoves);
        return (
          <button
            key={gem}
            type="button"
            className={selection.includes(gem) ? "picked" : ""}
            disabled={!pickable}
            aria-pressed={selection.includes(gem)}
            aria-label={`Bank: ${gemName(gem)} (${String(count)})`}
            onClick={() => {
              setSelection(toggleGem(selection, gem, legalMoves));
            }}
          >
            <Gem gem={gem} count={count} />
          </button>
        );
      })}
      <div className="take">
        <span aria-label="Wybrane żetony">
          {selection.map((gem, i) => (
            <Gem key={i} gem={gem} />
          ))}
        </span>
        <button
          type="button"
          disabled={take === null}
          onClick={() => {
            if (take !== null) play(take);
          }}
        >
          Weź żetony
        </button>
        {selection.length > 0 && (
          <button
            type="button"
            onClick={() => {
              setSelection([]);
            }}
          >
            Wyczyść
          </button>
        )}
      </div>
      {returns.length > 0 && (
        <div ref={returnsBox} className="returns" role="group" aria-label="Oddaj żeton">
          <span>Masz ponad 10 żetonów — oddaj jeden:</span>
          {returns.map((move) => {
            const parsed = parseMove(move);
            const gem = parsed.kind === "return" ? parsed.gem : 0;
            return (
              <button
                key={move}
                type="button"
                aria-label={`Oddaj: ${gemName(gem)}`}
                onClick={() => {
                  play(move);
                }}
              >
                <Gem gem={gem} />
              </button>
            );
          })}
        </div>
      )}
      {legalMoves.includes("pass") && (
        <button
          type="button"
          onClick={() => {
            play("pass");
          }}
        >
          Pasuj
        </button>
      )}
    </section>
  );
}
