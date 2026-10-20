import { useState } from "react";
import type { GameInfo } from "../api/schemas";
import { botChoices, botLabel, type LabLimits } from "./form";

export interface BotsFieldProps {
  game: GameInfo;
  bots: readonly string[];
  limits: LabLimits;
  onChange: (bots: string[]) => void;
}

/** Wybrane boty jako usuwalne etykiety i lista do dopisania kolejnego (do limitu serwera). */
export function BotsField({ game, bots, limits, onChange }: BotsFieldProps) {
  const choices = botChoices(game, limits);
  const [pick, setPick] = useState("");
  const add = pick !== "" ? pick : (choices[0]?.spec ?? "random");
  return (
    <fieldset className="bots">
      <legend>Boty ({bots.length})</legend>
      <ul aria-label="Wybrane boty">
        {bots.map((bot, i) => (
          <li key={`${bot}-${String(i)}`}>
            <span>{botLabel(game, bot)}</span>
            <button
              type="button"
              aria-label={`Usuń: ${botLabel(game, bot)}`}
              onClick={() => {
                onChange(bots.filter((_, j) => j !== i));
              }}
            >
              ×
            </button>
          </li>
        ))}
      </ul>
      <div className="add-bot">
        <select
          aria-label="Bot do dodania"
          value={add}
          onChange={(e) => {
            setPick(e.target.value);
          }}
        >
          {choices.map((b) => (
            <option key={b.spec} value={b.spec}>
              {b.label}
            </option>
          ))}
        </select>
        <button
          type="button"
          disabled={bots.length >= limits.maxBots}
          onClick={() => {
            onChange([...bots, add]);
          }}
        >
          Dodaj bota
        </button>
      </div>
    </fieldset>
  );
}
