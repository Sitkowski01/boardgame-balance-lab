import { useState, type SyntheticEvent } from "react";
import { loadName, saveName } from "./tickets";

/** Imie i "Usiadz": nazwa zostaje w przegladarce na nastepny raz. Do odpowiedzi serwera przycisk jest wylaczony. */
export function JoinForm({ onJoin }: { onJoin: (name: string) => Promise<void> }) {
  const [name, setName] = useState(loadName);
  const [busy, setBusy] = useState(false);
  const submit = (e: SyntheticEvent): void => {
    e.preventDefault();
    if (busy) {
      return;
    }

    setBusy(true);
    saveName(name.trim());
    void onJoin(name.trim()).finally(() => {
      setBusy(false);
    });
  };
  return (
    <form className="join" onSubmit={submit}>
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
      <button type="submit" disabled={busy || name.trim() === ""}>
        {busy ? "Siadam…" : "Usiądź przy stole"}
      </button>
    </form>
  );
}
