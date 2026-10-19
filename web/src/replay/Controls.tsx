export interface ControlsProps {
  position: number;
  last: number;
  /** Odtwarzanie wlasnie trwa (przycisk pokazuje "Pauza"). */
  running: boolean;
  onGo: (position: number) => void;
  onPlay: (play: boolean) => void;
}

/** Przewijanie powtorki: skoki o ruch, na poczatek i koniec, odtwarzanie i suwak. */
export function Controls({ position, last, running, onGo, onPlay }: ControlsProps) {
  return (
    <div className="controls" role="group" aria-label="Przewijanie">
      <button
        type="button"
        onClick={() => {
          onGo(0);
        }}
        disabled={position === 0}
      >
        ⏮ Początek
      </button>
      <button
        type="button"
        onClick={() => {
          onGo(position - 1);
        }}
        disabled={position === 0}
      >
        ◀ Poprzedni
      </button>
      <button
        type="button"
        onClick={() => {
          onPlay(!running);
        }}
        disabled={position >= last}
      >
        {running ? "⏸ Pauza" : "▶ Odtwarzaj"}
      </button>
      <button
        type="button"
        onClick={() => {
          onGo(position + 1);
        }}
        disabled={position >= last}
      >
        Następny ▶
      </button>
      <button
        type="button"
        onClick={() => {
          onGo(last);
        }}
        disabled={position >= last}
      >
        Koniec ⏭
      </button>
      <input
        type="range"
        aria-label="Ruch"
        min={0}
        max={last}
        value={position}
        onChange={(e) => {
          onGo(Number(e.target.value));
        }}
      />
      <output aria-label="Pozycja">
        ruch {position} z {last}
      </output>
    </div>
  );
}
