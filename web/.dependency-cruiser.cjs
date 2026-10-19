// Reguly architektury frontu. Logika (stan stolu, skladanie ruchow, powtorka, kontrakt z serwerem) to czysty
// TypeScript bez Reacta — testuje sie ja bez DOM. Klient SignalR zna tylko hub.ts: reszta aplikacji rozmawia
// z serwerem przez kontrakt GameServer, ktory testy podstawiaja.
/** @type {import('dependency-cruiser').IConfiguration} */
module.exports = {
  forbidden: [
    {
      name: "logika-bez-reacta",
      comment: "czyste moduly (*.ts poza hookami use*.ts) nie importuja Reacta ani komponentow",
      severity: "error",
      from: { path: "^src/.+\\.ts$", pathNot: ["(^|/)use[A-Z][^/]*\\.ts$", "\\.test\\.ts$", "^src/test/"] },
      to: { path: ["node_modules/react", "\\.tsx$"] },
    },
    {
      name: "signalr-tylko-w-hub",
      comment: "tylko src/api/hub.ts uzywa klienta SignalR; komponenty i hooki znaja kontrakt z connection.ts",
      severity: "error",
      from: { pathNot: "^src/api/hub\\.ts$" },
      to: { path: "node_modules/@microsoft/signalr" },
    },
    {
      name: "hub-tylko-w-main",
      comment: "prawdziwe polaczenie wstrzykuje main.tsx — reszta dostaje Connect z zewnatrz (testy podstawiaja wlasne)",
      severity: "error",
      from: { pathNot: "^src/main\\.tsx$" },
      to: { path: "^src/api/hub\\.ts$" },
    },
    {
      name: "api-nie-zna-widokow",
      comment: "warstwa api (schematy, REST, polaczenie) nie zalezy od stron, plansz ani stanu stolu",
      severity: "error",
      from: { path: "^src/api/", pathNot: "\\.test\\.ts$" },
      to: { path: "^src/(?!api/)" },
    },
    {
      name: "gry-niezalezne",
      comment: "plansza jednej gry nie importuje drugiej gry; wspolny jest tylko rejestr games.tsx",
      severity: "error",
      from: { path: "^src/(klejnoty|connectfour|mozaika)/" },
      to: { path: "^src/(klejnoty|connectfour|mozaika)/", pathNot: "^src/$1/" },
    },
    {
      name: "bez-cykli",
      severity: "error",
      from: {},
      to: { circular: true },
    },
  ],
  options: {
    doNotFollow: { path: "node_modules" },
    tsPreCompilationDeps: true,
    tsConfig: { fileName: "tsconfig.json" },
    enhancedResolveOptions: { exportsFields: ["exports"], conditionNames: ["import", "require", "node", "default", "types"] },
  },
};
