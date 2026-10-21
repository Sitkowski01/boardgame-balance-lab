/** Ilustracja gry na karcie w lobby i przy pokoju: kilka jej elementow, bez tekstu (ozdoba, aria-hidden). */
export function GameArt({ game }: { game: string }) {
  return (
    <svg className={`game-art art-${game}`} viewBox="0 0 120 72" aria-hidden="true" data-game={game}>
      {game === "klejnoty" ? <Klejnoty /> : game === "mozaika" ? <Mozaika /> : <ConnectFour />}
    </svg>
  );
}

function Chip({ x, y, fill, ring }: { x: number; y: number; fill: string; ring: string }) {
  return (
    <g>
      <circle cx={x} cy={y} r="13" fill={fill} stroke="rgb(0 0 0 / 0.25)" />
      <circle cx={x} cy={y} r="9.5" fill="none" stroke={ring} strokeWidth="1.6" strokeDasharray="3 2.2" />
    </g>
  );
}

function Klejnoty() {
  return (
    <>
      <rect x="62" y="8" width="44" height="58" rx="6" fill="#fffaf0" stroke="#d1a43c" strokeWidth="2" transform="rotate(8 84 37)" />
      <g transform="rotate(8 84 37)">
        <text x="70" y="24" fontSize="12" fontWeight="700" fill="#2b2b2b" fontFamily="Inter Variable, sans-serif">
          3
        </text>
        <path d="M84 30l8 6-8 14-8-14z" fill="#2e9a4f" />
        <path d="M84 30l8 6h-16z" fill="#58c47a" />
        <path d="M84 50l-8-14h16z" fill="#1f7a3c" opacity="0.6" />
      </g>
      <Chip x={26} y={46} fill="#c9353b" ring="#ffd7d9" />
      <Chip x={40} y={30} fill="#2f6fd1" ring="#cfe0ff" />
      <Chip x={50} y={52} fill="#f5f3ee" ring="#b9b4a8" />
    </>
  );
}

const mosaic = [
  ["#1f9e94", "#d4952a", "#b3243b"],
  ["#4a4c50", "#1f9e94", "#d4952a"],
  ["#efe6cf", "#4a4c50", "#1f9e94"],
];

function Mozaika() {
  return (
    <>
      <circle cx="36" cy="36" r="26" fill="#efe6cf" opacity="0.55" stroke="#c9b98f" />
      <rect x="25" y="25" width="10" height="10" rx="2" fill="#b3243b" />
      <rect x="37" y="25" width="10" height="10" rx="2" fill="#b3243b" />
      <rect x="25" y="37" width="10" height="10" rx="2" fill="#1f9e94" />
      <rect x="37" y="37" width="10" height="10" rx="2" fill="#d4952a" />
      {mosaic.map((row, r) =>
        row.map((fill, c) => (
          <rect
            key={`${String(r)}-${String(c)}`}
            x={70 + c * 14}
            y={15 + r * 14}
            width="12"
            height="12"
            rx="2.5"
            fill={fill}
            opacity={(r + c) % 3 === 2 ? 0.3 : 1}
          />
        )),
      )}
    </>
  );
}

const discs = [
  [null, null, null, null, null],
  [null, null, 1, null, null],
  [null, 0, 1, null, null],
  [0, 1, 0, 0, 1],
] as const;

function ConnectFour() {
  return (
    <>
      <rect x="18" y="6" width="84" height="64" rx="8" fill="#2a4f8f" />
      {discs.map((row, r) =>
        row.map((cell, c) => (
          <circle
            key={`${String(r)}-${String(c)}`}
            cx={30 + c * 15}
            cy={17 + r * 15}
            r="5.8"
            fill={cell === null ? "rgb(255 255 255 / 0.9)" : cell === 0 ? "#d33c3c" : "#e8c23a"}
          />
        )),
      )}
    </>
  );
}
