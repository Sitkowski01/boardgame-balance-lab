import { useEffect, useState } from "react";

/** Liczba na poczatku tekstu ("53,5%", "−6,5 ruchu", "200 z 200"): znak, czesc calkowita, ulamkowa i reszta. */
const leading = /^([−+]?)(\d+)(?:,(\d+))?(.*)$/su;

/** Ile trwa dochodzenie liczby do wartosci. */
export const CountUpMs = 700;

/** Tekst z liczba ustawiona na `share` (0..1) drogi od zera; reszta tekstu bez zmian. */
export function partial(text: string, share: number): string {
  const match = leading.exec(text);
  if (match === null) return text;
  const [, sign = "", whole = "", fraction, rest = ""] = match;
  const digits = fraction?.length ?? 0;
  const value = Number(`${whole}.${fraction ?? "0"}`) * share;
  return `${sign}${value.toFixed(digits).replace(".", ",")}${rest}`;
}

const calm = (): boolean => typeof window.matchMedia !== "function" || window.matchMedia("(prefers-reduced-motion: reduce)").matches;

/**
 * Wartosc kafelka, ktora przy pierwszym pokazaniu "dochodzi" od zera do wyniku. Czytnik ekranu dostaje od razu
 * wynik (ukryty tekst), a przy ograniczonym ruchu albo bez liczby na poczatku tekst stoi od razu.
 */
export function CountUp({ text }: { text: string }) {
  const [moving] = useState(() => !calm() && leading.test(text));
  const [shown, setShown] = useState(() => (moving ? partial(text, 0) : text));

  useEffect(() => {
    if (!moving) return;
    let frame = 0;
    const start = performance.now();
    const step = (now: number): void => {
      const t = Math.min(1, (now - start) / CountUpMs);
      setShown(partial(text, 1 - (1 - t) ** 3));
      if (t < 1) frame = requestAnimationFrame(step);
    };
    frame = requestAnimationFrame(step);
    return () => {
      cancelAnimationFrame(frame);
    };
  }, [moving, text]);

  return moving ? (
    <>
      <span aria-hidden="true">{shown}</span>
      <span className="visually-hidden">{text}</span>
    </>
  ) : (
    text
  );
}
