import { useEffect, useRef, useState, type ReactNode } from "react";

/**
 * Wiersz kolejki przyklejony pod naglowkiem: na telefonie gracz przewija do banku i rynku, a "Twoj ruch!" zostawal
 * wysoko poza ekranem. Tlo dostaje dopiero po przyklejeniu (wczesniej odcinalby sie od tla strony smuga), zeby
 * plansza chowala sie pod nim jak pod naglowkiem, a nie przeswitywala wokol pigulki.
 */
export function TurnBar({ children }: { children: ReactNode }) {
  const bar = useRef<HTMLDivElement>(null);
  const [stuck, setStuck] = useState(false);
  useEffect(() => {
    const el = bar.current;
    if (el === null || typeof IntersectionObserver === "undefined") return undefined;
    // Przyklejony pasek stoi rowno pod naglowkiem: obszar obserwacji konczy sie piksel nizej, wiec gorna krawedz wystaje.
    const topbar = parseFloat(getComputedStyle(el).getPropertyValue("--topbar-height")) || 0;
    const observer = new IntersectionObserver(
      ([entry]) => {
        if (entry !== undefined) setStuck(entry.intersectionRatio < 1 && entry.boundingClientRect.top <= topbar + 1);
      },
      { rootMargin: `-${String(topbar + 1)}px 0px 0px 0px`, threshold: 1 },
    );
    observer.observe(el);
    return () => {
      observer.disconnect();
    };
  }, []);
  return (
    <div ref={bar} className={stuck ? "turn-bar stuck" : "turn-bar"}>
      {children}
    </div>
  );
}
