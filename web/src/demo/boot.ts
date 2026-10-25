import type { Connect } from "../api/connection";
import { demoConnect, demoFetch } from "./server";
import { workerTransport } from "./transport";

export interface Demo {
  connect: Connect;
  fetch: typeof fetch;
  /** Serwer gry gotowy (albo blad startu .NET). */
  ready: Promise<void>;
}

/** Laboratorium ma osobny worker: eksperyment zajmuje watek az do konca, a stol ma w tym czasie grac dalej. */
export const isLabPath = (path: string): boolean => path === "/api/experiments" || path.startsWith("/api/experiments/");

/** Uruchamia serwer demo w dwoch workerach (stol i laboratorium) i podaje strone polaczenie i fetch do nich. */
export function startDemo(network: typeof fetch): Demo {
  const spawn = (name: string) => new Worker(new URL("./worker.ts", import.meta.url), { type: "module", name });
  const game = workerTransport(spawn("game"));
  const lab = workerTransport(spawn("lab"));
  // Blad startu laboratorium wyjdzie przy pierwszym zamowieniu; sam start nie moze zostawic nieobsluzonej obietnicy.
  lab.ready.catch(() => undefined);
  game.ready.catch(() => undefined);
  return {
    connect: demoConnect(game),
    fetch: demoFetch((path) => (isLabPath(path) ? lab : game), network),
    ready: game.ready,
  };
}
