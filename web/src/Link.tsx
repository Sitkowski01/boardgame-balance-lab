import type { ReactNode } from "react";

/**
 * Link w aplikacji: zwykly adres (da sie otworzyc w nowej karcie), ale klik zmienia strone bez
 * przeladowania — polaczenie z serwerem i stan zostaja.
 */
export function Link({ to, navigate, children }: { to: string; navigate: (path: string) => void; children: ReactNode }) {
  return (
    <a
      href={to}
      onClick={(e) => {
        e.preventDefault();
        navigate(to);
      }}
    >
      {children}
    </a>
  );
}
