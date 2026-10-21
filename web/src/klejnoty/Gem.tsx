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

/** Rabat karty: oszlifowany klejnot w kolorze (SVG), nazwa dla czytnikow ekranu. */
export function GemIcon({ gem, label }: { gem: number; label: string }) {
  return (
    <svg className={`gem-icon gem-icon-${String(gem)}`} viewBox="0 0 24 24" role="img" aria-label={label}>
      <path className="facet-base" d="M12 2.5l8.5 6.2L12 21.5 3.5 8.7z" />
      <path className="facet-top" d="M12 2.5l8.5 6.2h-17z" />
      <path className="facet-shine" d="M12 2.5l-3.2 6.2h6.4z" />
      <path className="facet-shade" d="M12 21.5l-3.2-12.8h6.4z" />
    </svg>
  );
}
