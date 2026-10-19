import { useEffect, useState } from "react";
import { secondsLeft } from "./table";

/** Sekundy do konca czasu na ruch; odswiezane co pol sekundy z zegara strony. */
export function Clock({ deadline, now }: { deadline: string | null; now: () => number }) {
  const [time, setTime] = useState(now);
  useEffect(() => {
    const timer = setInterval(() => {
      setTime(now());
    }, 500);
    return () => {
      clearInterval(timer);
    };
  }, [now]);
  const left = secondsLeft(deadline, time);
  return left === null ? null : (
    <span className="clock" aria-label="Czas na ruch">
      {left} s
    </span>
  );
}
