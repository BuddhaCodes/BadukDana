# Fixtures

Synthetic JSON shaped after the official client's types (`online-go/goban` `src/engine/protocol/*.ts`,
`online-go/online-go.com` `src/models/*.d.ts`, `src/lib/data_schema.ts`) as of 2026-09-29.
Names and ids are made up. Replace or complement them with anonymised recordings from beta.online-go.com
when available (see docs/OGS_API.md §6).

- `gamedata_9x9.json`, `removed_stones_accepted.json`: shaped after goban `GobanEngineConfig` / `removed_stones_accepted` (e61c56e); anonymised, hand-written until a real beta capture replaces them.
- `oje_root.json`, `oje_q16.json`: OGS Joseki Explorer positions (`GET /oje/position?id=…&mode=0`), hand-written after the fields online-go.com `src/views/Joseki/Joseki.tsx` reads (`node_id`, `placement` "root"/"pass"/"Q16", `category` IDEAL/GOOD/MISTAKE/TRICK/QUESTION, `description`, `tags`, `next_moves[{placement, category, node_id, variation_label}]`), 2026-10-01. Not yet checked against a live response.
