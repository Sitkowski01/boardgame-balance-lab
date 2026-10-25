import type { SeatInfo } from "./api/schemas";

/** Czego nie widac z samej planszy: bot na miejscu, rozlaczony gracz albo bot grajacy za niego. */
export function SeatStatus({ seat }: { seat: SeatInfo | undefined }) {
  return seat === undefined ? null : (
    <>
      {seat.kind === "Bot" && <span className="badge">bot</span>}
      {seat.kind === "Human" && !seat.connected && <span className="badge warn">rozłączony</span>}
      {seat.autopilot && <span className="badge warn">gra za niego bot</span>}
    </>
  );
}
