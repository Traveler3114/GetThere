"""
HŽPP GTFS-RT Server
====================
Scrapes real-time train positions from vlak.hzpp.hr and serves them
as GTFS-RT protobuf for OpenTripPlanner.

Endpoint: http://localhost:5000/hzpp-rt

Requirements:
    pip install flask requests gtfs-realtime-bindings

How it works:
    1. On startup, loads the HZPP GTFS static data (trips.txt, stop_times.txt,
       stops.txt) to build lookup tables.
    2. Every REFRESH_INTERVAL seconds, polls vlak.hzpp.hr for each active train.
    3. The website returns the train's current position (station name) and delay.
    4. We match the station name → stop_id and compute per-stop delays.
    5. OTP polls /hzpp-rt every 30s and applies the updates.

Run:
    python HZPP-RT.py
    python HZPP-RT.py --gtfs-dir C:/path/to/gtfs/folder
"""

import argparse
import csv
import json
import logging
import os
import threading
import time
from datetime import datetime, date, timedelta
try:
    from zoneinfo import ZoneInfo as _ZoneInfo
except Exception:
    _ZoneInfo = None
try:
    import pytz as _pytz
except ImportError:
    _pytz = None

def _make_tz(key):
    if _ZoneInfo is not None:
        try:
            return _ZoneInfo(key)
        except Exception:
            pass
    if _pytz is not None:
        return _pytz.timezone(key)
    from datetime import timezone
    return timezone.utc  # last resort

import requests
from flask import Flask, Response
from google.transit import gtfs_realtime_pb2

# ---------------------------------------------------------------------------
# CONFIG
# ---------------------------------------------------------------------------

GTFS_DIR        = "."            # Folder with trips.txt / stop_times.txt / stops.txt
REFRESH_INTERVAL = 30            # Seconds between full scrape cycles
TIMEZONE         = _make_tz("Europe/Zagreb")
BASE_URL         = "https://www.hzpp.app"
REQUEST_TIMEOUT  = 10
REQUEST_DELAY    = 0.3           # Seconds between individual train requests (be polite)

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s %(message)s",
    datefmt="%H:%M:%S",
)
log = logging.getLogger("hzpp-rt")

# ---------------------------------------------------------------------------
# GTFS STATIC DATA LOADER
# ---------------------------------------------------------------------------

def load_gtfs(gtfs_dir):
    """Load trips, stop_times and stops into fast lookup structures."""

    log.info("Loading GTFS static data from %s", gtfs_dir)

    # stops: stop_id → {"name": str, "name_lower": str}
    stops_by_id   = {}
    # stops: normalised name → stop_id  (for matching website station names)
    stop_id_by_name = {}

    with open(os.path.join(gtfs_dir, "stops.txt"), encoding="utf-8-sig") as f:
        for row in csv.DictReader(f):
            sid  = row["stop_id"].strip()
            name = row["stop_name"].strip()
            stops_by_id[sid] = name
            stop_id_by_name[_norm(name)] = sid

    # trips: train_number (trip_short_name) → list of trip_ids running on each service_id
    # We also need trip_id → service_id for calendar filtering.
    # trips: trip_id → {train_number, route_id, service_id}
    trips_by_id      = {}   # trip_id → dict
    trips_by_train   = {}   # train_number → [trip_id, ...]

    with open(os.path.join(gtfs_dir, "trips.txt"), encoding="utf-8-sig") as f:
        for row in csv.DictReader(f):
            tid    = row["trip_id"].strip()
            svc    = row["service_id"].strip()
            tnum   = row.get("trip_short_name", "").strip()
            info   = {"trip_id": tid, "service_id": svc, "train_number": tnum}
            trips_by_id[tid] = info
            if tnum:
                trips_by_train.setdefault(tnum, []).append(tid)

    # stop_times: trip_id → list of {stop_id, stop_sequence, arrival_sec, departure_sec}
    # sorted by stop_sequence
    stop_times = {}   # trip_id → sorted list

    with open(os.path.join(gtfs_dir, "stop_times.txt"), encoding="utf-8-sig") as f:
        for row in csv.DictReader(f):
            tid  = row["trip_id"].strip()
            sid  = row["stop_id"].strip()
            seq  = int(row["stop_sequence"])
            arr  = _hms_to_sec(row.get("arrival_time", "") or row.get("departure_time", ""))
            dep  = _hms_to_sec(row.get("departure_time", "") or row.get("arrival_time", ""))
            stop_times.setdefault(tid, []).append({
                "stop_id":       sid,
                "stop_sequence": seq,
                "arrival_sec":   arr,
                "departure_sec": dep,
            })

    for tid in stop_times:
        stop_times[tid].sort(key=lambda x: x["stop_sequence"])

    # calendar: load calendar.txt to know which service_ids run today
    calendar_path = os.path.join(gtfs_dir, "calendar.txt")
    calendar = {}   # service_id → set of dates it runs

    if os.path.exists(calendar_path):
        dow_cols = ["monday","tuesday","wednesday","thursday","friday","saturday","sunday"]
        with open(calendar_path, encoding="utf-8-sig") as f:
            for row in csv.DictReader(f):
                svc   = row["service_id"].strip()
                start = _parse_date(row["start_date"])
                end   = _parse_date(row["end_date"])
                days  = [row[d].strip() == "1" for d in dow_cols]
                dates = set()
                cur = start
                while cur <= end:
                    if days[cur.weekday()]:
                        dates.add(cur)
                    cur += timedelta(days=1)
                calendar[svc] = dates

    log.info(
        "Loaded %d stops, %d trips, %d stop-time entries",
        len(stops_by_id), len(trips_by_id), sum(len(v) for v in stop_times.values()),
    )

    return {
        "stops_by_id":     stops_by_id,
        "stop_id_by_name": stop_id_by_name,
        "trips_by_id":     trips_by_id,
        "trips_by_train":  trips_by_train,
        "stop_times":      stop_times,
        "calendar":        calendar,
    }


def get_active_trip_id(train_number, gtfs, for_date=None):
    """Return the trip_id for a given train number that runs on for_date (default: today)."""
    if for_date is None:
        for_date = datetime.now(TIMEZONE).date()

    candidates = gtfs["trips_by_train"].get(str(train_number), [])
    cal = gtfs["calendar"]

    for tid in candidates:
        svc = gtfs["trips_by_id"][tid]["service_id"]
        if svc in cal and for_date in cal[svc]:
            return tid

    # Fallback: return first candidate (useful when calendar.txt has no matching date
    # but we still want to serve something)
    return candidates[0] if candidates else None


# ---------------------------------------------------------------------------
# HZPP WEBSITE SCRAPER
# ---------------------------------------------------------------------------

SESSION = requests.Session()
SESSION.headers.update({
    "User-Agent": "Mozilla/5.0 (compatible; HZPP-RT/1.0)",
    "Accept":     "application/json",
    "Referer":    BASE_URL,
})


def fetch_train_data(train_number):
    """
    Fetch real-time data for a train from hzpp.app.

    The SvelteKit __data.json endpoint returns two newline-delimited JSON lines:
      Line 1: {"type":"data","nodes":[...]}  -- metadata
      Line 2: {"type":"chunk","id":1,"data":["<HTML>...</HTML>"]}
                                                ^^^^^^^^^^^^^^^^^
                                                Ancient HTML from the internal
                                                HZ Infrastruktura system at
                                                10.215.0.117/hzinfo

    Example HTML content:
      Trenutna pozicija vlak: 4061
      Relacija: OGULIN---->ZAGREB-GLA
      Kolodvor: ZAGREB+GL.+KOL.
      Zavrsio voznju 11.04.26. u 14:31 sati
      Kasni    2 min.
      Stanje vlaka od 12/04/26   u 17:07
    """
    url = (
        f"{BASE_URL}/__data.json"
        f"?trainId={train_number}"
        f"&x-sveltekit-trailing-slash=1"
        f"&x-sveltekit-invalidated=01"
    )
    try:
        resp = SESSION.get(url, timeout=REQUEST_TIMEOUT)
        resp.raise_for_status()

        # Response is newline-delimited JSON -- two separate JSON objects
        html_content = None
        for line in resp.text.strip().splitlines():
            line = line.strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
            except Exception:
                continue
            if obj.get("type") == "chunk":
                for item in obj.get("data", []):
                    if isinstance(item, str) and "<HTML>" in item.upper():
                        html_content = item
                        break

        if not html_content:
            log.debug("Train %s: no HTML chunk found", train_number)
            return None

        return _parse_hzinfo_html(html_content, train_number)

    except Exception as e:
        log.debug("Failed to fetch train %s: %s", train_number, e)
        return None


def _parse_hzinfo_html(html, train_number):
    """
    Parse the ancient HZ Infrastruktura HTML page.

    Extracts:
      current_station -- where the train is right now
      delay_min       -- integer minutes late (0 = on time)
      finished        -- True if journey completed
      route           -- e.g. "OGULIN - ZAGREB GLA"
    """
    import re
    import html as html_module

    text = html_module.unescape(html)

    result = {
        "train_number":    str(train_number),
        "current_station": None,
        "delay_min":       0,
        "finished":        False,
        "route":           None,
    }

    # Current station: <I>Kolodvor: </I><strong>ZAGREB+GL.+KOL.<br>
    m = re.search(r'Kolodvor\s*:?\s*</I>\s*(?:<strong>)?([^<\r\n]+)', text, re.IGNORECASE)
    if m:
        raw = m.group(1).strip()
        result["current_station"] = raw.replace("+", " ").replace(".", " ").strip()

    # Route: Relacija:<br> OGULIN---->ZAGREB-GLA
    m = re.search(r'Relacija\s*:?\s*(?:<br>)?\s*([^\r\n<]+)', text, re.IGNORECASE)
    if m:
        result["route"] = m.group(1).strip()

    # Delay: Kasni    2 min.
    m = re.search(r'Kasni\s+(\d+)\s*min', text, re.IGNORECASE)
    if m:
        result["delay_min"] = int(m.group(1))

    # Finished journey: Zavrsio voznju / Zavrsio voznju
    if re.search(r'Zavr[ss\u0161]io\s+vo[z\u017e]nju', text, re.IGNORECASE):
        result["finished"] = True

    log.debug(
        "Train %s: station=%s delay=%dmin finished=%s",
        train_number, result["current_station"], result["delay_min"], result["finished"]
    )
    return result


def get_active_train_numbers(gtfs):
    """
    Return train numbers (trip_short_names) that are expected to run today.
    We only poll trains that have service today to avoid hammering the API.
    """
    today = datetime.now(TIMEZONE).date()
    cal   = gtfs["calendar"]
    active = set()

    for tnum, tids in gtfs["trips_by_train"].items():
        for tid in tids:
            svc = gtfs["trips_by_id"][tid]["service_id"]
            if svc in cal and today in cal[svc]:
                active.add(tnum)
                break

    # Also include trains with no calendar data (safety net)
    if not active:
        active = set(gtfs["trips_by_train"].keys())

    return sorted(active, key=lambda x: int(x) if x.isdigit() else 0)


# ---------------------------------------------------------------------------
# DELAY CALCULATOR
# ---------------------------------------------------------------------------

def compute_stop_time_updates(trip_id, train_payload, gtfs):
    """
    Given the parsed train payload from vlak.hzpp.hr and the GTFS static trip,
    produce a list of StopTimeUpdate dicts for the GTFS-RT feed.

    The website tells us:
      - delay: overall delay in minutes
      - currentStation: name of the station the train is at / just passed
      - stations: list with per-station scheduled/actual times (if available)

    Strategy:
      1. If stations list is present with actual times → compute per-stop delays.
      2. Otherwise fall back to propagating the global delay from the current stop onward.
    """
    st_list = gtfs["stop_times"].get(trip_id, [])
    if not st_list:
        return []

    stop_id_by_name = gtfs["stop_id_by_name"]
    stops_by_id     = gtfs["stops_by_id"]

    # hzpp.app gives us: delay in minutes + current station name
    delay_min        = int(train_payload.get("delay_min", 0))
    global_delay_sec = delay_min * 60
    current_station  = _norm(train_payload.get("current_station") or "")
    finished         = train_payload.get("finished", False)

    updates = []

    # Find which stop sequence the train is currently at.
    # Station names in the HTML are uppercase, GTFS names are mixed case.
    # Normalise both to lowercase for matching.
    current_seq = None
    for st in st_list:
        sid   = st["stop_id"]
        sname = _norm(stops_by_id.get(sid, ""))
        if current_station and (
            sname == current_station
            or current_station in sname
            or sname in current_station
            or any(w in sname for w in current_station.split() if len(w) > 3)
        ):
            current_seq = st["stop_sequence"]
            break

    for st in st_list:
        seq = st["stop_sequence"]
        # Propagate delay to current stop and all future stops.
        if current_seq is None or seq >= current_seq or finished:
            updates.append({
                "stop_id":       st["stop_id"],
                "stop_sequence": seq,
                "delay":         global_delay_sec,
            })

    return updates


# ---------------------------------------------------------------------------
# FEED BUILDER
# ---------------------------------------------------------------------------

_feed_lock = threading.Lock()
_current_feed_bytes = b""   # Latest serialised protobuf (served to OTP)
_last_update_time   = 0


def build_feed(gtfs, updates_map):
    """
    Build a complete GTFS-RT FeedMessage from a map of
    trip_id → [StopTimeUpdate dicts].
    """
    feed = gtfs_realtime_pb2.FeedMessage()
    feed.header.gtfs_realtime_version = "2.0"
    feed.header.incrementality        = feed.header.FULL_DATASET
    feed.header.timestamp             = int(time.time())

    for trip_id, stu_list in updates_map.items():
        if not stu_list:
            continue
        entity            = feed.entity.add()
        entity.id         = trip_id
        trip_update       = entity.trip_update
        trip_update.trip.trip_id = trip_id

        for stu in stu_list:
            s             = trip_update.stop_time_update.add()
            s.stop_id     = stu["stop_id"]
            s.stop_sequence = stu["stop_sequence"]
            s.arrival.delay   = stu["delay"]
            s.departure.delay = stu["delay"]

    return feed


# ---------------------------------------------------------------------------
# BACKGROUND SCRAPE LOOP
# ---------------------------------------------------------------------------

def scrape_loop(gtfs):
    global _current_feed_bytes, _last_update_time

    log.info("Scrape loop started (interval=%ds)", REFRESH_INTERVAL)

    while True:
        try:
            active_trains = get_active_train_numbers(gtfs)
            log.info("Polling %d active trains...", len(active_trains))

            updates_map = {}   # trip_id → [stu_dicts]
            ok_count    = 0

            for tnum in active_trains:
                payload = fetch_train_data(tnum)
                time.sleep(REQUEST_DELAY)

                if payload is None:
                    continue

                trip_id = get_active_trip_id(tnum, gtfs)
                if trip_id is None:
                    log.debug("No active trip for train %s today", tnum)
                    continue

                stu_list = compute_stop_time_updates(trip_id, payload, gtfs)
                if stu_list:
                    updates_map[trip_id] = stu_list
                    ok_count += 1

            log.info("Got updates for %d/%d trains", ok_count, len(active_trains))

            feed = build_feed(gtfs, updates_map)
            pb   = feed.SerializeToString()

            with _feed_lock:
                _current_feed_bytes = pb
                _last_update_time   = time.time()

        except Exception:
            log.exception("Scrape loop error")

        time.sleep(REFRESH_INTERVAL)


# ---------------------------------------------------------------------------
# FLASK ENDPOINT
# ---------------------------------------------------------------------------

app = Flask(__name__)


@app.route("/hzpp-rt")
def hzpp_rt():
    with _feed_lock:
        pb = _current_feed_bytes

    if not pb:
        # Return empty feed if we haven't scraped yet
        feed = gtfs_realtime_pb2.FeedMessage()
        feed.header.gtfs_realtime_version = "2.0"
        feed.header.incrementality        = feed.header.FULL_DATASET
        feed.header.timestamp             = int(time.time())
        pb = feed.SerializeToString()

    return Response(pb, mimetype="application/x-protobuf")


@app.route("/status")
def status():
    """Human-readable status page."""
    age = int(time.time() - _last_update_time) if _last_update_time else -1
    with _feed_lock:
        size = len(_current_feed_bytes)
    return (
        f"<pre>HZPP-RT status\n"
        f"Feed size:    {size} bytes\n"
        f"Last updated: {age}s ago\n"
        f"Refresh:      every {REFRESH_INTERVAL}s\n"
        f"Endpoint:     /hzpp-rt\n</pre>"
    )


# ---------------------------------------------------------------------------
# HELPERS
# ---------------------------------------------------------------------------

def _norm(s):
    """Normalise a station name for fuzzy matching."""
    if not s:
        return ""
    return s.strip().lower()


def _hms_to_sec(hms):
    """Convert HH:MM:SS string to seconds since midnight. Handles >24h GTFS times."""
    if not hms:
        return 0
    parts = hms.strip().split(":")
    h, m, s = int(parts[0]), int(parts[1]), int(parts[2]) if len(parts) > 2 else 0
    return h * 3600 + m * 60 + s


def _parse_date(s):
    """Parse YYYYMMDD string to date."""
    return date(int(s[:4]), int(s[4:6]), int(s[6:8]))


def _parse_hhmm(s):
    """Parse HH:MM or HH:MM:SS time string to a datetime (date=today, Zagreb TZ)."""
    parts = s.strip().split(":")
    h, m = int(parts[0]), int(parts[1])
    now   = datetime.now(TIMEZONE)
    return now.replace(hour=h % 24, minute=m, second=0, microsecond=0)


# ---------------------------------------------------------------------------
# ENTRY POINT
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="HZPP GTFS-RT server")
    parser.add_argument(
        "--gtfs-dir", default=GTFS_DIR,
        help="Directory containing HZPP GTFS .txt files (default: current dir)"
    )
    parser.add_argument(
        "--port", type=int, default=5000,
        help="Port to listen on (default: 5000)"
    )
    parser.add_argument(
        "--interval", type=int, default=REFRESH_INTERVAL,
        help="Scrape interval in seconds (default: 30)"
    )
    args = parser.parse_args()

    REFRESH_INTERVAL = args.interval

    gtfs = load_gtfs(args.gtfs_dir)

    # Start background scraper thread
    scraper = threading.Thread(target=scrape_loop, args=(gtfs,), daemon=True)
    scraper.start()

    print(f"\n🚆 HŽPP GTFS-RT server")
    print(f"   Feed endpoint : http://localhost:{args.port}/hzpp-rt")
    print(f"   Status page   : http://localhost:{args.port}/status")
    print(f"   Scrape interval: {REFRESH_INTERVAL}s\n")

    app.run(host="0.0.0.0", port=5000)