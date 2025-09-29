# Nebula Pulse UI

- Tailwind CSS 4 configured via `tailwind.config.ts` with the **KTUI** palette and shadows.
- Global KTUI utility classes (`.ktui-card`, `.ktui-pill`, `.ktui-badge`) live in `src/styles.scss` for consistent styling.
- UI expects WebSocket at `/ws` delivering JSON messages for `signals`, `executions`, `alerts`. No mock data.
