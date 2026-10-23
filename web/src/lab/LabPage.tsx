import { useCallback, useEffect, useRef, useState } from "react";
import type { GameInfo } from "../api/schemas";
import { gameTitle } from "../names";
import { Link } from "../Link";
import type { LabClient } from "./client";
import { ExperimentFormView } from "./ExperimentFormView";
import { DefaultLimits, KindNames, type LabLimits } from "./form";
import { finished, resultOf, type ExperimentInfo, type ExperimentRequest, type ExperimentStatus } from "./model";
import { Results } from "./Results";
import { useExperiment } from "./useExperiment";
import "./lab.css";

export interface LabPageProps {
  client: LabClient;
  loadGames: () => Promise<GameInfo[]>;
  /** Eksperyment z adresu /lab/{id}; null = sam formularz. */
  experiment: string | null;
  navigate: (path: string) => void;
}

/** Tyle ostatnich eksperymentow na liscie; serwer pamieta wiecej, ale lista ma sie miescic na ekranie. */
const MaxRecent = 8;

/** Co ile odswiezamy liste, gdy sa na niej liczone eksperymenty. */
export const RecentEvery = 3000;

const StatusNames: Record<ExperimentStatus, string> = {
  Queued: "w kolejce",
  Running: "liczy się",
  Done: "gotowe",
  Failed: "błąd",
  Cancelled: "anulowany",
};

/**
 * Laboratorium balansu: zamowienie eksperymentu, postep i wyniki z wykresami. Eksperyment ma wlasny adres
 * (/lab/{id}), wiec wynik da sie odswiezyc albo wyslac komus jako link.
 */
export function LabPage({ client, loadGames, experiment, navigate }: LabPageProps) {
  const [games, setGames] = useState<GameInfo[] | string | null>(null);
  const [recent, setRecent] = useState<ExperimentInfo[]>([]);
  const { info, error } = useExperiment(client, experiment);

  useEffect(() => {
    let current = true;
    loadGames().then(
      (loaded) => {
        if (current) setGames(loaded);
      },
      (e: unknown) => {
        if (current) setGames(e instanceof Error && e.message !== "" ? e.message : "Nie udało się pobrać listy gier.");
      },
    );
    return () => {
      current = false;
    };
  }, [loadGames]);

  const [limits, setLimits] = useState<LabLimits>(DefaultLimits);
  useEffect(() => {
    let current = true;
    client.limits().then(
      (loaded) => {
        if (current) setLimits(loaded);
      },
      () => undefined, // zostaja domyslne; serwer i tak sprawdzi zamowienie
    );
    return () => {
      current = false;
    };
  }, [client]);

  const asked = useRef(0);
  const refresh = useCallback((): void => {
    const ask = ++asked.current;
    client.recent().then(
      (list) => {
        if (ask === asked.current) setRecent(list);
      },
      () => undefined,
    );
  }, [client]);
  // Lista odswieza sie przy wejsciu, gdy otwarty eksperyment zmieni stan, i co chwile, dopoki cos na niej sie liczy.
  useEffect(refresh, [refresh, info?.status]);
  const busy = recent.some((e) => !finished(e.status));
  useEffect(() => {
    if (!busy) return;
    const timer = setInterval(refresh, RecentEvery);
    return () => {
      clearInterval(timer);
    };
  }, [busy, refresh]);

  const gameName = (key: string): string => gameTitle((Array.isArray(games) ? games.find((g) => g.key === key)?.name : undefined) ?? key);
  const submit = async (request: ExperimentRequest): Promise<void> => {
    const accepted = await client.submit(request);
    navigate(`/lab/${accepted.id}`);
  };

  return (
    <main className="lab">
      <header className="lab-head">
        <h1>Laboratorium balansu</h1>
        <Link to="/" navigate={navigate}>
          ← Lobby
        </Link>
      </header>
      <p className="lede">
        Zamów tysiące partii botów i zobacz, co jest niezbalansowane: przewagę miejsca przy stole, dominującą strategię, za mocne elementy
        gry i to, co zmienia poprawka w zasadach. Każda liczba ma 95% przedział — widać, co jest efektem, a co szumem.
      </p>

      {typeof games === "string" ? (
        <p role="alert">{games}</p>
      ) : games === null ? (
        <p>Wczytywanie gier…</p>
      ) : (
        <ExperimentFormView games={games} limits={limits} onSubmit={submit} />
      )}

      {experiment !== null && (
        <section className="experiment" aria-label="Eksperyment">
          {error !== null && <p role="alert">{error}</p>}
          {info !== null && (
            <Progress info={info} game={gameName(info.request.game)} onCancel={() => void client.cancel(info.id).catch(() => undefined)} />
          )}
          {info !== null && info.status === "Done" && <Done info={info} />}
        </section>
      )}

      {recent.length > 0 && (
        <section className="recent" aria-label="Ostatnie eksperymenty">
          <h2>Ostatnie eksperymenty</h2>
          <ul>
            {recent.slice(0, MaxRecent).map((e) => (
              <li key={e.id} className={e.id === experiment ? "current" : ""}>
                <Link to={`/lab/${e.id}`} navigate={navigate}>
                  {KindNames[e.request.kind]}: {gameName(e.request.game)}, {e.request.bots.join(" · ")}
                </Link>
                <span className={`status ${e.status.toLowerCase()}`}>{StatusNames[e.status]}</span>
              </li>
            ))}
          </ul>
        </section>
      )}
    </main>
  );
}

function Progress({ info, game, onCancel }: { info: ExperimentInfo; game: string; onCancel: () => void }) {
  const share = info.total === 0 ? 0 : info.done / info.total;
  return (
    <div className="progress-panel">
      <div className="progress-head">
        <strong>
          {KindNames[info.request.kind]}: {game}
          {info.request.variant !== null && ` (${info.request.variant})`}
        </strong>
        <span className={`status ${info.status.toLowerCase()}`}>{StatusNames[info.status]}</span>
        {!finished(info.status) && (
          <button type="button" onClick={onCancel}>
            Anuluj
          </button>
        )}
      </div>
      <div
        className={`progress${finished(info.status) ? "" : " running"}`}
        role="progressbar"
        aria-label="Postęp eksperymentu"
        aria-valuemin={0}
        aria-valuemax={info.total}
        aria-valuenow={info.done}
        aria-valuetext={`${String(info.done)} z ${String(info.total)} partii`}
      >
        <span style={{ width: `${String(Math.round(share * 100))}%` }} />
      </div>
      <p className="note">
        {info.done} z {info.total} partii · boty: {info.request.bots.join(", ")} · ziarno {info.request.seed}
      </p>
      {info.error !== null && <p role="alert">{info.error}</p>}
    </div>
  );
}

function Done({ info }: { info: ExperimentInfo }) {
  const result = resultOf(info);
  return result === null ? (
    <p role="alert">Serwer przysłał wynik, którego ta wersja strony nie rozumie — odśwież stronę.</p>
  ) : (
    <Results result={result} />
  );
}
