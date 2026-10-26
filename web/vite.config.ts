import { copyFileSync, cpSync, existsSync, writeFileSync } from "node:fs";
import react from "@vitejs/plugin-react";
import { defineConfig, type Plugin } from "vite";

// W trybie dev front i serwer gry sa pod jednym adresem: proxy przekazuje REST i hub (WebSocket) do API.
// W produkcji API samo serwuje zbudowany front (Web:Root), wiec sciezki sa te same.
const api = process.env.API_URL ?? "http://127.0.0.1:5080";

/** Runtime .NET z serwerem demo: wynik `dotnet publish demo/BalanceLab.Demo.Wasm -o artifacts/demo-wasm`. */
const dotnetRuntime = "../artifacts/demo-wasm/wwwroot/_framework";

/** Apache, katalog strony: sciezka bez pliku (trasa aplikacji, np. /balance-lab/lab) to strona demo. */
const siteHtaccess = `FallbackResource /balance-lab/index.html
AddType application/wasm .wasm
`;

/**
 * Apache, runtime .NET: brakujacy plik to 404 (a nie strona, ktora loader .NET probowalby czytac jako modul),
 * a przegladarka z obsluga brotli dostaje gotowe pliki .br — tylko gdy serwer umie dopisac naglowek kompresji.
 * Typ pliku ustawia ta sama regula, ktora podmienia plik: LiteSpeed nie robi drugiego przebiegu regul, a
 * ForceType to drugi bezpiecznik. Tylko .wasm i .dat: skrypty serwer kompresuje sam, a gotowe .js.br
 * LiteSpeed kompresowal drugi raz (przegladarka rozpakowywala raz i dostawala bajty brotli zamiast JS).
 */
const runtimeHtaccess = String.raw`FallbackResource disabled
<IfModule mod_headers.c>
  <IfModule mod_rewrite.c>
    RewriteEngine On
    RewriteBase /balance-lab/_framework/
    RewriteCond %{HTTP:Accept-Encoding} br
    RewriteCond %{REQUEST_FILENAME}.br -f
    RewriteRule ^(.+)\.wasm$ $1.wasm.br [T=application/wasm,L]
    RewriteCond %{HTTP:Accept-Encoding} br
    RewriteCond %{REQUEST_FILENAME}.br -f
    RewriteRule ^(.+)\.dat$ $1.dat.br [T=application/octet-stream,L]
  </IfModule>
  <FilesMatch "\.br$">
    Header set Content-Encoding br
    Header append Vary Accept-Encoding
  </FilesMatch>
  <FilesMatch "\.wasm\.br$">
    ForceType application/wasm
  </FilesMatch>
  <FilesMatch "\.dat\.br$">
    ForceType application/octet-stream
  </FilesMatch>
</IfModule>
`;

/** Po zbudowaniu demo kladzie runtime .NET obok strony (dist-demo/_framework), skad laduje go worker. */
function demoRuntime(): Plugin {
  return {
    name: "balance-lab-demo-runtime",
    apply: "build",
    closeBundle() {
      if (!existsSync(dotnetRuntime)) {
        throw new Error(`Brak ${dotnetRuntime} - najpierw dotnet publish demo/BalanceLab.Demo.Wasm (pnpm build:demo robi to sam).`);
      }

      cpSync(dotnetRuntime, "dist-demo/_framework", { recursive: true });
      // Hosting statyczny nie zna tras aplikacji: /balance-lab/lab po odswiezeniu to dla niego brak pliku.
      // 404.html (GitHub Pages, Netlify) i .htaccess (Apache) oddaja wtedy strone, a trase czyta juz aplikacja.
      copyFileSync("dist-demo/index.html", "dist-demo/404.html");
      writeFileSync("dist-demo/.htaccess", siteHtaccess);
      writeFileSync("dist-demo/_framework/.htaccess", runtimeHtaccess);
    },
  };
}

// Demo (vite build --mode demo): strona statyczna pod /balance-lab/ z serwerem gry w WebAssembly w Web Workerach.
export default defineConfig(({ mode }) => ({
  plugins: mode === "demo" ? [react(), demoRuntime()] : [react()],
  worker: { format: "es" },
  ...(mode === "demo" ? { base: "/balance-lab/", build: { outDir: "dist-demo" } } : {}),
  server: {
    proxy: {
      "/api": { target: api, changeOrigin: true },
      "/hubs": { target: api, changeOrigin: true, ws: true },
    },
  },
}));
