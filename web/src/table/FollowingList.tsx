import { useLayoutEffect, useRef, useState, type ReactNode } from "react";

/**
 * Lista ruchow, ktora po kazdym nowym ruchu przewija sie na dol: przy dlugiej partii najnowsze ruchy chowaly sie
 * pod krawedzia, a widac bylo tylko poczatek. Gdy cos jest ukryte u gory, wiersze tam przygasaja zamiast urywac sie
 * w polowie.
 */
export function FollowingList({ count, children }: { count: number; children: ReactNode }) {
  const list = useRef<HTMLOListElement>(null);
  const [hidden, setHidden] = useState(false);
  useLayoutEffect(() => {
    const el = list.current;
    if (el === null) return;
    el.scrollTop = el.scrollHeight;
    setHidden(el.scrollTop > 0);
  }, [count]);
  return (
    <ol
      ref={list}
      className={hidden ? "scrolled" : undefined}
      onScroll={(e) => {
        setHidden(e.currentTarget.scrollTop > 0);
      }}
    >
      {children}
    </ol>
  );
}
