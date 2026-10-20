import { useEffect, useState } from "react";
import { ApiError } from "../api/rest";
import type { LabClient } from "./client";
import { finished, type ExperimentInfo } from "./model";

/** Co ile pytamy o postep: dosc czesto, zeby pasek sie ruszal, i dosc rzadko dla limitu zadan serwera (120/min). */
export const PollEvery = 1500;

/** Po bledzie (siec, 429, 5xx) pytamy rzadziej. */
export const RetryEvery = 4000;

export type ExperimentState = { info: ExperimentInfo | null; error: string | null };

/**
 * Stan eksperymentu odswiezany, dopoki sie liczy. Odpowiedz dla starego identyfikatora (zmiana strony
 * w trakcie zapytania) nie nadpisze nowego.
 */
export function useExperiment(client: LabClient, id: string | null): ExperimentState {
  const [state, setState] = useState<ExperimentState & { id: string | null }>({ id, info: null, error: null });
  useEffect(() => {
    if (id === null) return;
    let current = true;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const poll = (): void => {
      client.get(id).then(
        (info) => {
          if (!current) return;
          setState({ id, info, error: null });
          if (!finished(info.status)) timer = setTimeout(poll, PollEvery);
        },
        (e: unknown) => {
          if (!current) return;
          // Nieznany eksperyment (np. serwer go zapomnial) to koniec; reszta bledow mija, wiec ponawiamy, zostawiajac ostatni stan.
          const gone = e instanceof ApiError && e.status === 404;
          const message = e instanceof Error && e.message !== "" ? e.message : "Nie udało się pobrać eksperymentu.";
          setState((s) => ({ id, info: s.id === id ? s.info : null, error: gone ? message : `${message} Ponawiam…` }));
          if (!gone) timer = setTimeout(poll, RetryEvery);
        },
      );
    };
    poll();
    return () => {
      current = false;
      clearTimeout(timer);
    };
  }, [client, id]);

  return state.id === id ? { info: state.info, error: state.error } : { info: null, error: null };
}
