/** Strony aplikacji. Adresy sa zwykle (bez #), bo API serwuje index.html dla kazdej sciezki poza /api i /hubs. */
export type Route = { page: "lobby" } | { page: "room"; id: string } | { page: "match"; id: string } | { page: "missing" };

const id = "([0-9a-fA-F-]{36})";
const room = new RegExp(`^/rooms/${id}/?$`);
const match = new RegExp(`^/matches/${id}/?$`);

export function parseRoute(path: string): Route {
  if (path === "/" || path === "") {
    return { page: "lobby" };
  }

  const r = room.exec(path)?.[1];
  if (r !== undefined) {
    return { page: "room", id: r };
  }

  const m = match.exec(path)?.[1];
  return m === undefined ? { page: "missing" } : { page: "match", id: m };
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
  }
}
