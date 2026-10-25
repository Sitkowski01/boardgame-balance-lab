import { useState, type SyntheticEvent } from "react";
import type { GameInfo } from "../api/schemas";
import { useDemo } from "../demo/DemoBanner";
import { gameBlurbs, gameTitle } from "../names";
import { loadName, saveName } from "../table/tickets";
import { GameArt } from "./GameArt";

/**
 * Nowy pokoj: gra (karty do wyboru), liczba miejsc z jej zakresu, wariant zasad (jesli gra je ma) i imie gospodarza.
 * Do odpowiedzi serwera przycisk jest wylaczony — podwojny klik nie zaklada dwoch pokoi.
 */
export function CreateRoom({
  games,
  onCreate,
}: {
  games: GameInfo[];
  onCreate: (game: string, seats: number, variant: string | null, name: string) => Promise<void>;
}) {
  const [key, setKey] = useState<string>();
  const [seats, setSeats] = useState(0);
  const [variant, setVariant] = useState("");
  const [name, setName] = useState(loadName);
  const [busy, setBusy] = useState(false);
  const demo = useDemo();
  const game = games.find((g) => g.key === key) ?? games[0];
  if (game === undefined) {
    return null;
  }

  const count = seats >= game.minPlayers && seats <= game.maxPlayers ? seats : game.minPlayers;
  const submit = (e: SyntheticEvent): void => {
    e.preventDefault();
    if (busy) {
      return;
    }

    setBusy(true);
    saveName(name.trim());
    void onCreate(game.key, count, game.hasVariants && variant.trim() !== "" ? variant.trim() : null, name.trim()).finally(() => {
      setBusy(false);
    });
  };

  return (
    <form className="create panel" aria-label="Nowy pokój" onSubmit={submit}>
      <fieldset className="game-choice">
        <legend>Gra</legend>
        {games.map((g) => (
          <label key={g.key} className={g.key === game.key ? "game-card chosen" : "game-card"}>
            <input
              type="radio"
              name="game"
              value={g.key}
              aria-label={gameTitle(g.name)}
              checked={g.key === game.key}
              onChange={() => {
                setKey(g.key);
              }}
            />
            <GameArt game={g.key} />
            <span className="game-card-name">{gameTitle(g.name)}</span>
            <span className="game-card-players">{players(g)}</span>
            <span className="game-card-blurb">{gameBlurbs[g.key] ?? ""}</span>
          </label>
        ))}
      </fieldset>
      <div className="create-fields">
        <label>
          Twoje imię
          <input
            value={name}
            maxLength={24}
            placeholder="np. Ala"
            onChange={(e) => {
              setName(e.target.value);
            }}
          />
        </label>
        <label>
          Miejsca
          <select
            value={count}
            onChange={(e) => {
              setSeats(Number(e.target.value));
            }}
          >
            {Array.from({ length: game.maxPlayers - game.minPlayers + 1 }, (_, i) => game.minPlayers + i).map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        </label>
        {game.hasVariants && (
          <label className="wide">
            Wariant zasad
            <input
              value={variant}
              placeholder={game.key === "klejnoty" ? "np. card17.points=2; win=13" : "puste = zasady podstawowe"}
              onChange={(e) => {
                setVariant(e.target.value);
              }}
            />
          </label>
        )}
        <button type="submit" className="primary" disabled={busy || name.trim() === ""}>
          {busy ? "Zakładam…" : "Załóż pokój"}
        </button>
      </div>
      <p className="hint">
        {demo
          ? "Przy stole posadzisz boty na wolnych miejscach. W demo stół jest tylko w tej karcie — boty liczą w Twojej przeglądarce."
          : "Przy stole posadzisz boty na wolnych miejscach — możesz zagrać sam albo wysłać link znajomym."}
      </p>
    </form>
  );
}

function players(game: GameInfo): string {
  return game.minPlayers === game.maxPlayers
    ? `${String(game.minPlayers)} graczy`
    : `${String(game.minPlayers)}–${String(game.maxPlayers)} graczy`;
}
