import { useCallback, useEffect, useState } from "react";
import type { Connect } from "./api/connection";
import { Link } from "./Link";
import { LobbyPage } from "./lobby/LobbyPage";
import { ReplayPage } from "./replay/ReplayPage";
import { parseRoute, type Route } from "./route";
import { TablePage } from "./table/TablePage";
import type { Tickets } from "./table/tickets";

export interface AppProps {
  connect: Connect;
  tickets: Tickets;
}

/** Trzy strony pod zwyklymi adresami; przejscia przez historie przegladarki (dziala "wstecz"). */
export function App({ connect, tickets }: AppProps) {
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
  const navigate = useCallback((path: string): void => {
    window.history.pushState(null, "", path);
    setRoute(parseRoute(path));
  }, []);

  switch (route.page) {
    case "lobby":
      return <LobbyPage connect={connect} tickets={tickets} navigate={navigate} />;
    case "room":
      return <TablePage key={route.id} room={route.id} connect={connect} tickets={tickets} navigate={navigate} />;
    case "match":
      return <ReplayPage key={route.id} match={route.id} navigate={navigate} />;
    case "missing":
      return (
        <main>
          <h1>Nie ma takiej strony</h1>
          <Link to="/" navigate={navigate}>
            ← Lobby
          </Link>
        </main>
      );
  }
}
