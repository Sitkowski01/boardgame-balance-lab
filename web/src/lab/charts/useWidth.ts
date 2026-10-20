import { useEffect, useState, type RefObject } from "react";

/**
 * Szerokosc elementu w pikselach, sledzona przy zmianie rozmiaru. Wykres SVG rysowany w tej szerokosci ma
 * napisy w prawdziwej wielkosci na kazdym ekranie (viewBox skalowany do telefonu robil je nieczytelne).
 * Bez ResizeObserver (stare przegladarki, testy w jsdom) zostaje szerokosc domyslna.
 */
export function useWidth(ref: RefObject<HTMLElement | null>, fallback: number): number {
  const [width, setWidth] = useState(fallback);
  useEffect(() => {
    const element = ref.current;
    if (element === null || typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver((entries) => {
      const measured = entries[0]?.contentRect.width ?? 0;
      if (measured > 0) setWidth(Math.round(measured));
    });
    observer.observe(element);
    return () => {
      observer.disconnect();
    };
  }, [ref]);
  return width;
}
