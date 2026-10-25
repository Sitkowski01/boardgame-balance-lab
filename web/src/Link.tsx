import type { MouseEvent, ReactNode } from "react";
import { toUrl } from "./base";

/**
 * Link w aplikacji: zwykly adres (da sie otworzyc w nowej karcie), ale klik zmienia strone bez
 * przeladowania — polaczenie z serwerem i stan zostaja. Ctrl/Cmd/Shift/Alt albo inny przycisk myszy
 * zostawiaja klik przegladarce (nowa karta, nowe okno, pobranie).
 */
export function Link({
  to,
  navigate,
  className,
  children,
}: {
  to: string;
  navigate: (path: string) => void;
  className?: string;
  children: ReactNode;
}) {
  return (
    <a
      href={toUrl(to)}
      className={className}
      onClick={(e: MouseEvent<HTMLAnchorElement>) => {
        if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) {
          return;
        }

        e.preventDefault();
        navigate(to);
      }}
    >
      {children}
    </a>
  );
}
