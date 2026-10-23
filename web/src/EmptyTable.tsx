/**
 * Ilustracja pustego stolu na stronach "nie ma": sukno bez graczy, przewrocona karta i zeton, ktory jeszcze sie
 * toczy. Ten sam jezyk co ilustracja w lobby, zeby strona bledu nie wygladala jak wypadek przy pracy.
 */
export function EmptyTable() {
  return (
    <svg className="empty-table" viewBox="0 0 240 150" aria-hidden="true">
      <ellipse cx="120" cy="92" rx="108" ry="46" fill="var(--felt-deep)" />
      <ellipse cx="120" cy="86" rx="108" ry="46" fill="var(--felt)" />
      <ellipse cx="120" cy="86" rx="92" ry="36" fill="none" stroke="rgb(255 255 255 / 0.12)" strokeWidth="2" strokeDasharray="6 6" />
      <g className="fallen-card">
        <rect x="70" y="58" width="40" height="56" rx="6" fill="#fffaf0" stroke="#d1a43c" strokeWidth="2" transform="rotate(-68 90 86)" />
        <path d="M86 80l8 6-8 12-8-12z" fill="#2f6fd1" transform="rotate(-68 90 86)" />
      </g>
      <circle cx="150" cy="96" r="11" fill="#e8c23a" stroke="rgb(0 0 0 / 0.25)" />
      <circle cx="150" cy="96" r="7" fill="none" stroke="#fff2c0" strokeWidth="1.6" strokeDasharray="3 2" />
      <g className="rolling-chip">
        <circle cx="0" cy="0" r="11" fill="#d33c3c" stroke="rgb(0 0 0 / 0.25)" />
        <circle cx="0" cy="0" r="7" fill="none" stroke="#ffd0d0" strokeWidth="1.6" strokeDasharray="3 2" />
      </g>
    </svg>
  );
}
