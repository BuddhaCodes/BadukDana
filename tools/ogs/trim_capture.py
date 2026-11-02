#!/usr/bin/env python3
"""Turns a Hoshi OGS capture (logs in <data>/ogs-captures/*.jsonl, made with --Ogs:RecordTraffic=true)
into a test fixture: keeps the frames of one game (or all), drops chat text and replaces usernames and
user ids with neutral ones. Secrets were already removed when recording.

    python3 tools/ogs/trim_capture.py capture.jsonl tests/Hoshi.Ogs.Tests/Fixtures/recorded/beta-9x9.jsonl [--game 12345]
"""
import argparse, json, re, sys

def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("source"); ap.add_argument("target"); ap.add_argument("--game", type=int)
    ap.add_argument("--keep-chat", action="store_true", help="keep chat text (it is removed by default)")
    a = ap.parse_args()
    names, ids = {}, {}

    def alias_name(n):
        return names.setdefault(n, f"player{len(names) + 1}")

    def alias_id(i):
        return ids.setdefault(i, 900000 + len(ids) + 1)

    def clean(node, key=""):
        if isinstance(node, dict):
            out = {}
            for k, v in node.items():
                if k in ("username", "black_username", "white_username") and isinstance(v, str):
                    out[k] = alias_name(v)
                elif k in ("id", "player_id", "black_player_id", "white_player_id", "user_id", "winner") and isinstance(v, int) and v > 0 and key != "game":
                    out[k] = alias_id(v)
                elif k in ("body", "message") and not a.keep_chat and isinstance(v, str) and key == "line":
                    out[k] = "…"
                else:
                    out[k] = clean(v, k)
            return out
        if isinstance(node, list):
            return [clean(x, key) for x in node]
        return node

    kept = 0
    with open(a.source, encoding="utf-8") as src, open(a.target, "w", encoding="utf-8") as dst:
        for line in src:
            e = json.loads(line)
            if e.get("k") == "ws" and a.game is not None:
                frame = e.get("data")
                ev = frame[0] if isinstance(frame, list) and frame else ""
                gid = frame[1].get("game_id") if isinstance(frame, list) and len(frame) > 1 and isinstance(frame[1], dict) else None
                if not (re.match(rf"game/{a.game}/", str(ev)) or gid == a.game):
                    continue
            if e.get("k") == "http":
                continue  # REST responses hold profiles and lists; fixtures for those are made by hand
            dst.write(json.dumps(clean(e), ensure_ascii=False, separators=(",", ":")) + "\n")
            kept += 1
    print(f"{kept} entries → {a.target}; {len(names)} names and {len(ids)} ids replaced", file=sys.stderr)
    return 0

if __name__ == "__main__":
    sys.exit(main())
