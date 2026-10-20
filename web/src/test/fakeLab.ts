import { vi } from "vitest";
import type { LabClient } from "../lab/client";
import { DefaultLimits } from "../lab/form";
import type { ExperimentInfo, ExperimentRequest } from "../lab/model";

/** Eksperyment jak z serwera; wynik i stan do podmiany. */
export function experiment(patch: Partial<ExperimentInfo> = {}): ExperimentInfo {
  const request: ExperimentRequest = {
    game: "mozaika",
    kind: "Tournament",
    bots: ["greedy", "random"],
    games: 100,
    seed: "1",
    variant: null,
  };
  return {
    id: "00000000-0000-0000-0000-0000000000e1",
    request,
    status: "Queued",
    done: 0,
    total: 100,
    error: null,
    created: "2026-10-20T09:00:00Z",
    finished: null,
    result: null,
    ...patch,
  };
}

/** Laboratorium w pamieci: kazda metoda to vi.fn, ktora test moze przestawic. */
export function fakeLab(info: ExperimentInfo = experiment()) {
  const client = {
    submit: vi.fn<LabClient["submit"]>(() => Promise.resolve(info)),
    get: vi.fn<LabClient["get"]>(() => Promise.resolve(info)),
    cancel: vi.fn<LabClient["cancel"]>(() => Promise.resolve({ ...info, status: "Cancelled" })),
    recent: vi.fn<LabClient["recent"]>(() => Promise.resolve([])),
    limits: vi.fn<LabClient["limits"]>(() => Promise.resolve({ ...DefaultLimits })),
  } satisfies LabClient;
  return client;
}
