import { useEffect, useState, type ReactNode } from "react";
import { DemoBanner } from "./demo/DemoBanner";
import { Link } from "./Link";
import type { Route } from "./route";
import { applyTheme, currentTheme, loadTheme, saveTheme, type Theme } from "./theme";

export const Repository = "https://github.com/Sitkowski01/boardgame-balance-lab";

export interface ShellProps {
  route: Route;
  navigate: (path: string) => void;
  children: ReactNode;
  /** Czy system prosi o ciemny motyw (testy podstawiaja wlasna odpowiedz). */
  systemDark?: () => boolean;
  /** Zapytanie o motyw systemu do sledzenia zmian (null = przegladarka bez matchMedia). */
  systemQuery?: () => MediaQueryList | null;
}

// Starsze przegladarki (i jsdom w testach) moga nie miec matchMedia: wtedy jasny motyw.
const darkQuery = (): MediaQueryList | null =>
  typeof window.matchMedia === "function" ? window.matchMedia("(prefers-color-scheme: dark)") : null;
const prefersDark = (): boolean => darkQuery()?.matches ?? false;

/** Rama kazdej strony: znak i nazwa, nawigacja miedzy gra a laboratorium, przelacznik motywu i stopka. */
export function Shell({ route, navigate, children, systemDark = prefersDark, systemQuery = darkQuery }: ShellProps) {
  const [chosen, setChosen] = useState<Theme | null>(loadTheme);
  const [theme, setTheme] = useState<Theme>(() => currentTheme(chosen, systemDark()));
  // Bez wyboru gracza style ida za systemem, wiec przelacznik tez: inaczej pokazalby zly motyw do zmiany.
  useEffect(() => {
    const query = systemQuery();
    if (query === null || chosen !== null) {
      return undefined;
    }

    const follow = (e: MediaQueryListEvent): void => {
      setTheme(e.matches ? "dark" : "light");
    };
    query.addEventListener("change", follow);
    return () => {
      query.removeEventListener("change", follow);
    };
  }, [chosen, systemQuery]);
  const toggle = (): void => {
    const next = theme === "dark" ? "light" : "dark";
    saveTheme(next);
    applyTheme(next);
    setChosen(next);
    setTheme(next);
  };
  const section = route.page === "lab" ? "lab" : "play";

  return (
    <div className="shell">
      <header className="topbar">
        <div className="topbar-inner">
          <Link to="/" navigate={navigate} className="brand">
            <Logo />
            <span>Balance Lab</span>
          </Link>
          <nav aria-label="Główna">
            <NavLink to="/" current={section === "play"} navigate={navigate}>
              Graj
            </NavLink>
            <NavLink to="/lab" current={section === "lab"} navigate={navigate}>
              Laboratorium
            </NavLink>
          </nav>
          <button
            type="button"
            className="icon-button"
            aria-label={theme === "dark" ? "Włącz jasny motyw" : "Włącz ciemny motyw"}
            title={theme === "dark" ? "Jasny motyw" : "Ciemny motyw"}
            onClick={toggle}
          >
            {theme === "dark" ? <SunIcon /> : <MoonIcon />}
          </button>
        </div>
      </header>
      <DemoBanner />
      {children}
      <footer className="footer">
        <div className="footer-inner">
          <span>Balance Lab — silnik gier, boty MCTS i symulator turniejów w .NET 10, stół na żywo przez SignalR, front w React.</span>
          <a href={Repository} target="_blank" rel="noreferrer">
            Kod na GitHubie
          </a>
        </div>
      </footer>
    </div>
  );
}

function NavLink({
  to,
  current,
  navigate,
  children,
}: {
  to: string;
  current: boolean;
  navigate: (path: string) => void;
  children: ReactNode;
}) {
  return (
    <span className="nav-link" aria-current={current ? "page" : undefined}>
      <Link to={to} navigate={navigate}>
        {children}
      </Link>
    </span>
  );
}

/** Znak: waga z kafelkiem na jednej szali i zetonem na drugiej — balans elementow gry. */
export function Logo() {
  return (
    <svg className="logo" viewBox="0 0 32 32" width="32" height="32" aria-hidden="true">
      <rect x="1" y="1" width="30" height="30" rx="8" fill="var(--accent)" />
      <path d="M16 7v16M10 25h12M7 11h18" stroke="var(--accent-ink)" strokeWidth="2" strokeLinecap="round" />
      <path d="M7 11l-3 7h6zM25 11l-3 7h6z" fill="none" stroke="var(--accent-ink)" strokeWidth="1.6" strokeLinejoin="round" />
      <rect x="5" y="14.5" width="4" height="3" rx="0.8" fill="var(--gold)" />
      <circle cx="25" cy="16" r="1.8" fill="var(--gold)" />
    </svg>
  );
}

function MoonIcon() {
  return (
    <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
      <path d="M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinejoin="round" />
    </svg>
  );
}

function SunIcon() {
  return (
    <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
      <circle cx="12" cy="12" r="4" fill="none" stroke="currentColor" strokeWidth="1.8" />
      <path
        d="M12 2v2M12 20v2M2 12h2M20 12h2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
      />
    </svg>
  );
}
