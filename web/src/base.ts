/**
 * Adresy aplikacji pod sciezka bazowa. Serwer gry serwuje front z korzenia ("/"), a demo w przegladarce lezy
 * pod /balance-lab/ — trasy w aplikacji sa zawsze od "/", a tu zamieniaja sie na adres w pasku i z powrotem.
 */
export function urls(baseUrl: string) {
  // Vite podaje baze z jednym ukosnikiem na koncu ("/", "/balance-lab/").
  const base = baseUrl.replace(/\/$/u, "");
  return {
    /** Trasa aplikacji ("/rooms/x") jako adres w przegladarce ("/balance-lab/rooms/x"). */
    toUrl: (path: string): string => base + path,
    /** Adres z paska jako trasa aplikacji; adres spoza bazy zostaje bez zmian (i trafi na 404 aplikacji). */
    fromUrl: (pathname: string): string =>
      pathname === base || pathname.startsWith(`${base}/`) ? pathname.slice(base.length) || "/" : pathname,
  };
}

export const { toUrl, fromUrl } = urls(import.meta.env.BASE_URL);
