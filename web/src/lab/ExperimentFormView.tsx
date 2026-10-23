import { useId, useState } from "react";
import type { GameInfo } from "../api/schemas";
import { gameTitle } from "../names";
import { BotsField } from "./BotsField";
import {
  DefaultLimits,
  defaultForm,
  fromRequest,
  grouped,
  KindHelp,
  KindNames,
  problems,
  toRequest,
  work,
  type ExperimentForm,
  type LabLimits,
} from "./form";
import type { ExperimentKind, ExperimentRequest } from "./model";

export interface ExperimentFormProps {
  games: readonly GameInfo[];
  /** Limity serwera; do ich nadejscia domyslne. */
  limits?: LabLimits;
  /** Wysyla zamowienie; odrzucona obietnica zostawia formularz z komunikatem. */
  onSubmit: (request: ExperimentRequest) => Promise<void>;
  /** Zamowienie, od ktorego formularz startuje (otwarty eksperyment); bez niego ustawienia domyslne pierwszej gry. */
  start?: ExperimentRequest | undefined;
}

const kinds: ExperimentKind[] = ["Tournament", "League", "Comparison"];

/** Skladnia wariantu z serwera ("zmiany po średnikach: win=N; ...") z polskim wstepem; sam zapis zostaje jak w CLI. */
const syntaxHint = (syntax: string): string => `Zmiany oddzielaj średnikami: ${syntax.replace(/^zmiany po średnikach: /u, "")}`;

/** Zamowienie eksperymentu: gra, rodzaj, boty, liczba partii, ziarno i wariant zasad. */
export function ExperimentFormView({ games, limits = DefaultLimits, onSubmit, start }: ExperimentFormProps) {
  const id = useId();
  const first = games[0];
  const [form, setForm] = useState<ExperimentForm | null>(() =>
    start !== undefined && games.some((g) => g.key === start.game) ? fromRequest(start) : first === undefined ? null : defaultForm(first),
  );
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const game = games.find((g) => g.key === form?.game);
  if (form === null || game === undefined) {
    return <p className="note">Brak gier w katalogu serwera.</p>;
  }

  const found = problems(form, game, limits);
  const set = (patch: Partial<ExperimentForm>): void => {
    setForm({ ...form, ...patch });
    setError(null);
  };

  return (
    <form
      className="lab-form"
      aria-label="Nowy eksperyment"
      onSubmit={(e) => {
        e.preventDefault();
        if (found.length > 0 || sending) return;
        setSending(true);
        onSubmit(toRequest(form)).then(
          () => {
            setSending(false);
          },
          (err: unknown) => {
            setSending(false);
            setError(err instanceof Error && err.message !== "" ? err.message : "Serwer nie przyjął eksperymentu.");
          },
        );
      }}
    >
      <div className="field-row">
        <label>
          Gra
          <select
            value={form.game}
            onChange={(e) => {
              const next = games.find((g) => g.key === e.target.value);
              if (next !== undefined) {
                setForm(defaultForm(next, form.kind));
                setError(null);
              }
            }}
          >
            {games.map((g) => (
              <option key={g.key} value={g.key}>
                {gameTitle(g.name)}
              </option>
            ))}
          </select>
        </label>
        <fieldset className="kinds">
          <legend>Rodzaj</legend>
          {kinds.map((kind) => (
            <label key={kind} className={form.kind === kind ? "chosen" : ""}>
              <input
                type="radio"
                name={`${id}-kind`}
                checked={form.kind === kind}
                onChange={() => {
                  setForm({ ...defaultForm(game, kind), variant: form.variant, seed: form.seed });
                  setError(null);
                }}
              />
              {KindNames[kind]}
            </label>
          ))}
        </fieldset>
      </div>
      <p className="hint">{KindHelp[form.kind]}</p>

      {/* Klucz gry: lista do dopisania zaczyna od nowa przy innej grze (inne profile botow). */}
      <BotsField
        key={game.key}
        game={game}
        bots={form.bots}
        limits={limits}
        onChange={(bots) => {
          set({ bots });
        }}
      />

      <div className="field-row">
        <label>
          {form.kind === "League" ? "Partie na parę" : "Partie"}
          <input
            inputMode="numeric"
            value={form.games}
            onChange={(e) => {
              set({ games: e.target.value.trim() });
            }}
          />
        </label>
        <label>
          Ziarno
          <input
            inputMode="numeric"
            value={form.seed}
            onChange={(e) => {
              set({ seed: e.target.value.trim() });
            }}
          />
        </label>
        {game.hasVariants && (
          <label className="variant">
            {form.kind === "Comparison" ? "Wariant (co jeśli)" : "Wariant zasad (opcjonalnie)"}
            <input
              value={form.variant}
              placeholder={form.kind === "Comparison" ? "zmiana zasad — składnia poniżej" : "puste = oryginał"}
              onChange={(e) => {
                set({ variant: e.target.value });
              }}
            />
            {game.variantSyntax !== null && <span className="hint">{syntaxHint(game.variantSyntax)}</span>}
          </label>
        )}
      </div>

      <p className="hint" aria-label="Praca">
        Praca: {Number.isFinite(work(form)) ? grouped(work(form)) : "—"} z {grouped(limits.maxWork)} jednostek (partie × iteracje botów)
      </p>
      {found.length > 0 && (
        <ul className="problems" aria-label="Do poprawienia">
          {found.map((p) => (
            <li key={p}>{p}</li>
          ))}
        </ul>
      )}
      {error !== null && <p role="alert">{error}</p>}
      <button type="submit" className="primary" disabled={found.length > 0 || sending}>
        {sending ? "Wysyłanie…" : "Uruchom eksperyment"}
      </button>
    </form>
  );
}
