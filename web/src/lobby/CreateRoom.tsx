import { useState, type SyntheticEvent } from "react";
import type { GameInfo } from "../api/schemas";
import { loadName, saveName } from "../table/tickets";

/** Nowy pokoj: gra, liczba miejsc z jej zakresu, wariant zasad (jesli gra je ma) i imie gospodarza. */
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
  const game = games.find((g) => g.key === key) ?? games[0];
  if (game === undefined) {
    return null;
  }

  const count = seats >= game.minPlayers && seats <= game.maxPlayers ? seats : game.minPlayers;
  const submit = (e: SyntheticEvent): void => {
    e.preventDefault();
    saveName(name.trim());
    void onCreate(game.key, count, game.hasVariants && variant.trim() !== "" ? variant.trim() : null, name.trim());
  };

  return (
    <form className="create" aria-label="Nowy pokój" onSubmit={submit}>
      <label>
        Twoje imię
        <input
          value={name}
          maxLength={24}
          onChange={(e) => {
            setName(e.target.value);
          }}
        />
      </label>
      <label>
        Gra
        <select
          value={game.key}
          onChange={(e) => {
            setKey(e.target.value);
          }}
        >
          {games.map((g) => (
            <option key={g.key} value={g.key}>
              {g.name}
            </option>
          ))}
        </select>
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
        <label>
          Wariant zasad
          <input
            value={variant}
            placeholder="np. card17.points=2; win=13"
            onChange={(e) => {
              setVariant(e.target.value);
            }}
          />
        </label>
      )}
      <button type="submit" className="primary" disabled={name.trim() === ""}>
        Załóż pokój
      </button>
    </form>
  );
}
