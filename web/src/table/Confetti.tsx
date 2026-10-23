import { useEffect, useRef, useState } from "react";

/** Kolory zetonow i krazkow z gier: konfetti wyglada jak rozsypane elementy stolu, a nie jak ozdoba z innej strony. */
const colors = ["#d8403f", "#eec53b", "#2f6fd1", "#26864a", "#e0b041", "#5cc29d"];

/** Ile trwa wybuch konfetti; potem plotno znika. */
export const ConfettiMs = 1600;

interface Piece {
  x: number;
  y: number;
  vx: number;
  vy: number;
  spin: number;
  size: number;
  color: string;
}

const calm = (): boolean => typeof window.matchMedia !== "function" || window.matchMedia("(prefers-reduced-motion: reduce)").matches;

/** Platki wystrzelone z gornego srodka w gore i na boki; generator liczb do podmiany w testach. */
export function burst(width: number, count: number, random: () => number = Math.random): Piece[] {
  return Array.from({ length: count }, (_, i) => ({
    x: width / 2,
    y: 12,
    vx: (random() - 0.5) * 9,
    vy: -3 - random() * 5,
    spin: random() * Math.PI,
    size: 5 + random() * 5,
    color: colors[i % colors.length] as string,
  }));
}

/**
 * Krotkie konfetti nad wynikiem, raz, gdy partia skonczyla sie na oczach gracza. Ozdoba: ukryta przed czytnikiem
 * ekranu, nie lapie klikniec i nie pojawia sie przy ograniczonym ruchu ani bez plotna.
 */
export function Confetti() {
  const canvas = useRef<HTMLCanvasElement>(null);
  const [done, setDone] = useState(calm);

  useEffect(() => {
    const ctx = canvas.current?.getContext("2d");
    if (done || canvas.current === null || ctx === null || ctx === undefined) {
      setDone(true);
      return;
    }

    const { width, height } = canvas.current;
    const pieces = burst(width, 90);
    const start = performance.now();
    let frame = 0;
    const step = (now: number): void => {
      const t = now - start;
      ctx.clearRect(0, 0, width, height);
      for (const p of pieces) {
        p.vy += 0.18;
        p.x += p.vx;
        p.y += p.vy;
        p.spin += 0.2;
        ctx.save();
        ctx.translate(p.x, p.y);
        ctx.rotate(p.spin);
        ctx.globalAlpha = Math.max(0, 1 - t / ConfettiMs);
        ctx.fillStyle = p.color;
        ctx.fillRect(-p.size / 2, -p.size / 4, p.size, p.size / 2);
        ctx.restore();
      }
      if (t < ConfettiMs) {
        frame = requestAnimationFrame(step);
      } else {
        setDone(true);
      }
    };
    frame = requestAnimationFrame(step);
    return () => {
      cancelAnimationFrame(frame);
    };
  }, [done]);

  return done ? null : <canvas ref={canvas} className="confetti" width={640} height={240} aria-hidden="true" />;
}
