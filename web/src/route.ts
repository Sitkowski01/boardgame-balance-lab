/** Strony aplikacji. Adresy sa zwykle (bez #), bo API serwuje index.html dla kazdej sciezki poza /api i /hubs. */
export type Route =
  | { page: "lobby" }
  | { page: "room"; id: string }
  | { page: "match"; id: string }
  | { page: "lab"; experiment: string | null }
  | { page: "missing" };

const id = "([0-9a-fA-F-]{36})";
const room = new RegExp(`^/rooms/${id}/?$`);
const match = new RegExp(`^/matches/${id}/?$`);
const lab = new RegExp(`^/lab(?:/${id})?/?$`);

export function parseRoute(path: string): Route {
  if (path === "/" || path === "") {
    return { page: "lobby" };
  }

  const r = room.exec(path)?.[1];
  if (r !== undefined) {
    return { page: "room", id: r };
  }

  const m = match.exec(path)?.[1];
  if (m !== undefined) {
    return { page: "match", id: m };
  }

  const l = lab.exec(path);
  return l === null ? { page: "missing" } : { page: "lab", experiment: l[1] ?? null };
}

const titles: Record<Route["page"], string> = {
  lobby: "Lobby",
  room: "Stół",
  match: "Powtórka",
  lab: "Laboratorium",
  missing: "Nie ma takiej strony",
};

/** Tytul karty przegladarki: strona i nazwa aplikacji (czytnik ekranu czyta go po przejsciu). */
export function routeTitle(route: Route): string {
  return `${titles[route.page]} · Balance Lab`;
}

export function routePath(route: Route): string {
  switch (route.page) {
    case "lobby":
    case "missing":
      return "/";
    case "room":
      return `/rooms/${route.id}`;
    case "match":
      return `/matches/${route.id}`;
    case "lab":
      return route.experiment === null ? "/lab" : `/lab/${route.experiment}`;
  }
}
