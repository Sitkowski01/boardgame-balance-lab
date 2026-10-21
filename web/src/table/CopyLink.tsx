import { useEffect, useState } from "react";

type CopyState = { kind: "idle" } | { kind: "copied" } | { kind: "manual"; address: string };

/**
 * Link do stolu dla znajomych: kopiuje adres strony do schowka, a po chwili przycisk wraca do zwyklego napisu
 * (mozna skopiowac jeszcze raz). Bez schowka pokazuje adres do recznego skopiowania.
 */
export function CopyLink({ href = () => window.location.href, reset = 2000 }: { href?: () => string; reset?: number }) {
  const [state, setState] = useState<CopyState>({ kind: "idle" });
  useEffect(() => {
    if (state.kind !== "copied") {
      return undefined;
    }

    const timer = setTimeout(() => {
      setState({ kind: "idle" });
    }, reset);
    return () => {
      clearTimeout(timer);
    };
  }, [state, reset]);

  const copy = (): void => {
    const address = href();
    // Schowek dziala tylko na https (i localhost); bez niego wywolanie rzuca od razu, wiec idzie przez Promise.
    Promise.resolve()
      .then(() => navigator.clipboard.writeText(address))
      .then(
        () => {
          setState({ kind: "copied" });
        },
        () => {
          setState({ kind: "manual", address });
        },
      );
  };
  return (
    <div className="copy-link">
      <button type="button" onClick={copy}>
        {state.kind === "copied" ? "Skopiowano ✓" : "Kopiuj link do stołu"}
      </button>
      {state.kind === "manual" && (
        <input
          readOnly
          aria-label="Link do stołu"
          value={state.address}
          onFocus={(e) => {
            e.target.select();
          }}
        />
      )}
    </div>
  );
}
