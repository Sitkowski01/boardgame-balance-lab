/**
 * Motyw strony: jasny albo ciemny. Bez wyboru gracza strona idzie za ustawieniem systemu (prefers-color-scheme),
 * a wybor zostaje w localStorage. Przegladarka moze odmowic dostepu do pamieci, wiec zapis i odczyt sa w try.
 */
export type Theme = "light" | "dark";

const key = "balance-lab:theme";

export function loadTheme(storage: () => Storage = () => localStorage): Theme | null {
  try {
    const stored = storage().getItem(key);
    return stored === "light" || stored === "dark" ? stored : null;
  } catch {
    return null;
  }
}

export function saveTheme(theme: Theme, storage: () => Storage = () => localStorage): void {
  try {
    storage().setItem(key, theme);
  } catch {
    // Bez pamieci przegladarki motyw dziala do odswiezenia strony.
  }
}

/** Motyw widoczny teraz: wybrany przez gracza, a bez wyboru ten z systemu. */
export function currentTheme(chosen: Theme | null, systemDark: boolean): Theme {
  return chosen ?? (systemDark ? "dark" : "light");
}

/** Wybor motywu trafia do atrybutu data-theme na <html>; style maja reguly dla obu wartosci. */
export function applyTheme(theme: Theme | null, root: HTMLElement = document.documentElement): void {
  if (theme === null) {
    delete root.dataset.theme;
  } else {
    root.dataset.theme = theme;
  }
}
