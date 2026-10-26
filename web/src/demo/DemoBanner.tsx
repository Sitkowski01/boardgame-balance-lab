import { createContext, useContext, useEffect, useState } from "react";

/** Demo w przegladarce: serwer gry w workerze. Null = zwykla strona z prawdziwym serwerem. */
export interface DemoInfo {
  /** Serwer w workerze gotowy (albo blad startu .NET). */
  ready: Promise<void>;
}

export const DemoContext = createContext<DemoInfo | null>(null);

/** Czy strona dziala jako demo (bez serwera: stoly sa tylko w tej karcie, nikt inny do nich nie dolaczy). */
export const useDemo = (): boolean => useContext(DemoContext) !== null;

type Status = "starting" | "ready" | "failed";

/** Pasek pod naglowkiem: co sie dzieje w tle i dlaczego nie ma tu gry przez siec. */
export function DemoBanner() {
  const demo = useContext(DemoContext);
  const [status, setStatus] = useState<Status>("starting");
  useEffect(() => {
    if (demo === null) return undefined;
    let current = true;
    demo.ready.then(
      () => {
        if (current) setStatus("ready");
      },
      () => {
        if (current) setStatus("failed");
      },
    );
    return () => {
      current = false;
    };
  }, [demo]);

  if (demo === null) return null;
  return (
    <div className={`demo-banner ${status}`} role="status">
      <div className="demo-banner-inner">
        {status === "starting" && (
          <>
            <span className="spinner" aria-hidden="true" />
            Uruchamiam serwer gry w Twojej przeglądarce (.NET w WebAssembly)…
          </>
        )}
        {status === "ready" && (
          <>
            <strong>Demo</strong>
            {/* Jeden element flex: inaczej kod lamal sie do osobnego wiersza, a kropka odstawala za nim. */}
            <span>
              Serwer gry działa w tej karcie: te same pokoje, boty i laboratorium co na serwerze, tylko bez sieci. Gra z innymi ludźmi — po{" "}
              <code>docker compose up</code>.
            </span>
          </>
        )}
        {status === "failed" && <>Nie udało się uruchomić serwera w przeglądarce. Odśwież stronę albo spróbuj w nowszej przeglądarce.</>}
      </div>
    </div>
  );
}
