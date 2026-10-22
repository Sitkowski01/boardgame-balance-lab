import { useCallback, useEffect, useRef, useState } from "react";
import type { Connect } from "./api/connection";
import { api } from "./api/rest";
import type { LabClient } from "./lab/client";
import { LabPage } from "./lab/LabPage";
import { Link } from "./Link";
import { LobbyPage } from "./lobby/LobbyPage";
import { ReplayPage } from "./replay/ReplayPage";
import { parseRoute, routeTitle, type Route } from "./route";
import { Shell } from "./Shell";
import { TablePage } from "./table/TablePage";
import type { Tickets } from "./table/tickets";

export interface AppProps {
  connect: Connect;
  tickets: Tickets;
  lab: LabClient;
}

/** Strony pod zwyklymi adresami; przejscia przez historie przegladarki (dziala "wstecz"). */
const loadGames = () => api.games();

export function App({ connect, tickets, lab }: AppProps) {
  const [route, setRoute] = useState<Route>(() => parseRoute(window.location.pathname));
  useEffect(() => {
    const back = (): void => {
      setRoute(parseRoute(window.location.pathname));
    };
    window.addEventListener("popstate", back);
    return () => {
      window.removeEventListener("popstate", back);
    };
  }, []);
  // Przejscie linkiem (nie "wstecz" ani pierwsze wczytanie): nowa strona od gory, a fokus na niej, zeby
  // czytnik ekranu i klawiatura zaczynaly od tresci, a nie od linku, ktory wlasnie zniknal.
  const moved = useRef(false);
  const navigate = useCallback((path: string): void => {
    window.history.pushState(null, "", path);
    moved.current = true;
    setRoute(parseRoute(path));
  }, []);
  useEffect(() => {
    document.title = routeTitle(route);
    if (!moved.current) {
      return;
    }

    moved.current = false;
    window.scrollTo(0, 0);
    const main = document.querySelector("main");
    if (main !== null) {
      main.tabIndex = -1;
      main.focus({ preventScroll: true });
    }
  }, [route]);

  return (
    <Shell route={route} navigate={navigate}>
      <Page route={route} navigate={navigate} connect={connect} tickets={tickets} lab={lab} />
    </Shell>
  );
}

function Page({ route, navigate, connect, tickets, lab }: AppProps & { route: Route; navigate: (path: string) => void }) {
  switch (route.page) {
    case "lobby":
      return <LobbyPage connect={connect} tickets={tickets} navigate={navigate} />;
    case "room":
      return <TablePage key={route.id} room={route.id} connect={connect} tickets={tickets} navigate={navigate} />;
    case "match":
      return <ReplayPage key={route.id} match={route.id} navigate={navigate} />;
    case "lab":
      return <LabPage client={lab} loadGames={loadGames} experiment={route.experiment} navigate={navigate} />;
    case "missing":
      return (
        <main className="missing">
          <p className="missing-code" aria-hidden="true">
            404
          </p>
          <h1>Nie ma takiej strony</h1>
          <p className="lead">Ten stół już się rozszedł albo adres ma literówkę. Partie i eksperymenty mają adresy z długim numerem.</p>
          <Link to="/" navigate={navigate}>
            ← Lobby
          </Link>
        </main>
      );
  }
}
