import { gemName } from "./model";

/** Zeton albo rabat w kolorze: kolko z liczba; nazwa koloru dla czytnikow ekranu. */
export function Gem({ gem, count, label }: { gem: number; count?: number; label?: string }) {
  return (
    <span
      className={`gem gem-${String(gem)}`}
      title={gemName(gem)}
      aria-label={label ?? `${gemName(gem)}${count === undefined ? "" : `: ${String(count)}`}`}
    >
      {count ?? ""}
    </span>
  );
}

/** Koszt albo wymaganie: tylko kolory z liczba wieksza od zera. */
export function Cost({ counts }: { counts: readonly number[] }) {
  return <span className="cost">{counts.map((n, gem) => (n > 0 ? <Gem key={gem} gem={gem} count={n} /> : null))}</span>;
}
