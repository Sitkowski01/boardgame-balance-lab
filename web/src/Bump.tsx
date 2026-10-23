import { useState, type ReactNode } from "react";

/**
 * Liczba, ktora "podskakuje", gdy sie zmieni (punkty, zetony, karty): oko od razu widzi, co dal ostatni ruch.
 * Przy pierwszym pokazaniu stoi; nowa wartosc to nowy klucz, wiec animacja CSS rusza od poczatku przy kazdej zmianie.
 */
export function Bump({ value, children }: { value: number; children: ReactNode }) {
  const [seen, setSeen] = useState({ value, changed: false });
  if (seen.value !== value) {
    setSeen({ value, changed: true });
  }

  return (
    <span key={value} className={seen.changed ? "bump" : undefined}>
      {children}
    </span>
  );
}
