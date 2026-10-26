import { useEffect, useState } from "react";
import { secondsLeft } from "./table";

/** Sekundy do konca czasu na ruch; odswiezane co pol sekundy z zegara strony. */
export function Clock({ deadline, now }: { deadline: string | null; now: () => number }) {
  const [time, setTime] = useState(now);
  // Nowy termin liczony od biezacej chwili, nie od ostatniego tyku sprzed nawet pol sekundy: inaczej 60,3 s
  // zaokraglalo sie w gore do "61 s" przy limicie 60. Wzorzec "poprzednie propsy" zamiast efektu.
  const [counted, setCounted] = useState(deadline);
  if (counted !== deadline) {
    setCounted(deadline);
    setTime(now());
  }
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
