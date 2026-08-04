"""Pull a Statcast sample and compute the real league rates the sim is tuned against."""
import csv, io, sys, time, urllib.request
from collections import defaultdict

DATES = [
    "2024-04-05", "2024-04-13", "2024-04-21", "2024-04-29",
    "2024-05-07", "2024-05-15", "2024-05-23", "2024-05-31",
    "2024-06-08", "2024-06-16", "2024-06-24", "2024-07-02",
    "2024-07-13", "2024-07-21", "2024-07-29", "2024-08-06",
    "2024-08-14", "2024-08-22", "2024-08-30", "2024-09-07",
    "2024-09-15", "2024-09-23",
]

URL = ("https://baseballsavant.mlb.com/statcast_search/csv?all=true&type=details"
       "&game_date_gt={d}&game_date_lt={d}&player_type=batter")

rows = []
for d in DATES:
    try:
        req = urllib.request.Request(URL.format(d=d), headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(req, timeout=90) as r:
            text = r.read().decode("utf-8", "replace")
        got = list(csv.DictReader(io.StringIO(text.lstrip("﻿"))))
        rows.extend(got)
        print(f"  {d}: {len(got):>5} pitches", file=sys.stderr)
    except Exception as e:
        print(f"  {d}: FAILED {e}", file=sys.stderr)
    time.sleep(1.5)

if not rows:
    sys.exit("no data")

WHIFF = {"swinging_strike", "swinging_strike_blocked", "foul_tip", "missed_bunt"}
FOUL = {"foul", "foul_bunt", "foul_pitchout"}
INPLAY = {"hit_into_play", "hit_into_play_score", "hit_into_play_no_out"}
CALLED = {"called_strike"}
BALLS = {"ball", "blocked_ball", "pitchout"}

n = len(rows)
whiff = sum(r["description"] in WHIFF for r in rows)
foul = sum(r["description"] in FOUL for r in rows)
inplay = sum(r["description"] in INPLAY for r in rows)
called = sum(r["description"] in CALLED for r in rows)
ball = sum(r["description"] in BALLS for r in rows)
swings = whiff + foul + inplay

# Zone: Statcast codes 1-9 as inside the strike zone, 11-14 as outside.
zone = sum(1 for r in rows if (r.get("zone") or "").strip().isdigit()
           and int(r["zone"]) <= 9)

games = {r["game_pk"] for r in rows}
g = len(games)

ev = defaultdict(int)
for r in rows:
    e = (r.get("events") or "").strip()
    if e:
        ev[e] += 1

hits = sum(ev[k] for k in ("single", "double", "triple", "home_run"))
ks = sum(ev[k] for k in ("strikeout", "strikeout_double_play"))
bb = sum(ev[k] for k in ("walk", "intent_walk"))
pa = sum(ev.values())

# Final score per game, from the running score columns.
score = {}
for r in rows:
    try:
        tot = int(r["post_home_score"]) + int(r["post_away_score"])
    except (ValueError, KeyError, TypeError):
        continue
    pk = r["game_pk"]
    if tot > score.get(pk, 0):
        score[pk] = tot
runs = sum(score.values())

# Batted-ball physics, which is what the sim actually models.
def nums(col, lo, hi):
    out = []
    for r in rows:
        v = (r.get(col) or "").strip()
        if not v:
            continue
        try:
            f = float(v)
        except ValueError:
            continue
        if lo <= f <= hi:
            out.append(f)
    return out

launch_speed = nums("launch_speed", 1, 130)
launch_angle = nums("launch_angle", -90, 90)
distance = nums("hit_distance_sc", 1, 600)

def pct(v, p):
    if not v:
        return 0.0
    s = sorted(v)
    return s[min(len(s) - 1, int(len(s) * p / 100))]

print(f"\n=== Statcast sample: {n} pitches, {g} games, {len(DATES)} dates in 2024 ===")
print(f"PER GAME (both clubs combined)")
print(f"  Runs {runs/g:.2f}   Hits {hits/g:.2f}   HR {ev['home_run']/g:.2f}   "
      f"K {ks/g:.2f}   BB {bb/g:.2f}")
print(f"  2B {ev['double']/g:.2f}   3B {ev['triple']/g:.2f}   "
      f"Pitches {n/g:.1f}   PA {pa/g:.1f}   Pitches/PA {n/pa:.2f}")
print(f"PITCH LEVEL")
print(f"  Zone% {zone/n*100:.1f}   Swing% {swings/n*100:.1f}   "
      f"Called strike% {called/n*100:.1f}   Ball% {ball/n*100:.1f}")
print(f"  Whiff/swing {whiff/swings*100:.1f}   Foul/swing {foul/swings*100:.1f}   "
      f"InPlay/swing {inplay/swings*100:.1f}")
print(f"BATTED BALL")
print(f"  Exit velo  mean {sum(launch_speed)/len(launch_speed):.1f}  "
      f"p50 {pct(launch_speed,50):.0f}  p90 {pct(launch_speed,90):.0f}  "
      f"max {max(launch_speed):.0f}  (n={len(launch_speed)})")
print(f"  Launch ang mean {sum(launch_angle)/len(launch_angle):.1f}  "
      f"p50 {pct(launch_angle,50):.0f}  p90 {pct(launch_angle,90):.0f}")
print(f"  Distance   mean {sum(distance)/len(distance):.0f}  "
      f"p90 {pct(distance,90):.0f}  max {max(distance):.0f}")
