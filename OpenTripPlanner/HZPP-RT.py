"""
HŽPP GTFS-RT Server
====================
Scrapes real-time train positions from hzpp.app and serves them
as GTFS-RT protobuf for OpenTripPlanner.

Endpoint: http://localhost:5000/hzpp-rt

Requirements:
    pip install flask requests gtfs-realtime-bindings

How it works:
    1. On startup, downloads the HZPP GTFS zip from GTFS_ZIP_URL and loads
       trips.txt, stop_times.txt, stops.txt and calendar.txt directly from
       the archive — no local GTFS files required.
    2. Every REFRESH_INTERVAL seconds, polls hzpp.app for each active train.
    3. The website returns the train's current position (station name) and delay.
    4. We match the station name → stop_id and compute per-stop delays.
    5. OTP polls /hzpp-rt every 30s and applies the updates.

Run:
    python HZPP-RT.py
    python HZPP-RT.py --gtfs-url https://www.hzpp.hr/GTFS_files.zip
"""

import argparse
import csv
import io
import json
import logging
import threading
import time
import zipfile
from datetime import datetime, date, timedelta

try:
    from zoneinfo import ZoneInfo as _ZoneInfo
except Exception:
    _ZoneInfo = None
try:
    import pytz as _pytz
except ImportError:
    _pytz = None

import requests
from flask import Flask, Response
from google.transit import gtfs_realtime_pb2

# ---------------------------------------------------------------------------
# CONFIG
# ---------------------------------------------------------------------------

GTFS_ZIP_URL     = "https://www.hzpp.hr/GTFS_files.zip"
REFRESH_INTERVAL = 30            # Seconds between full scrape cycles
BASE_URL         = "https://www.hzpp.app"
REQUEST_TIMEOUT  = 10
REQUEST_DELAY    = 0.3           # Seconds between individual train requests


def _make_tz(key):
    if _ZoneInfo is not None:
        try:
            return _ZoneInfo(key)
        except Exception:
            pass
    if _pytz is not None:
        return _pytz.timezone(key)
    from datetime import timezone
    return timezone.utc


TIMEZONE = _make_tz("Europe/Zagreb")

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s %(message)s",
    datefmt="%H:%M:%S",
)
log = logging.getLogger("hzpp-rt")


# ---------------------------------------------------------------------------
# GTFS STATIC DATA LOADER  (reads from zip, no local files needed)
# ---------------------------------------------------------------------------

def _open_zip_text(zf, filename):
    """Return a text-mode reader for a file inside a ZipFile object."""
    return io.TextIOWrapper(zf.open(filename), encoding="utf-8-sig")


def download_gtfs_zip(url):
    """Download the GTFS zip and return an in-memory ZipFile."""
    log.info("Downloading GTFS zip from %s ...", url)
    resp = requests.get(url, timeout=60)
    resp.raise_for_status()
    log.info("Downloaded %.1f KB", len(resp.content) / 1024)
    return zipfile.ZipFile(io.BytesIO(resp.content))


def load_gtfs(gtfs_zip_url):
    """Download the HZPP GTFS zip and load it into fast lookup structures."""

    zf = download_gtfs_zip(gtfs_zip_url)
    available = zf.namelist()
    log.info("Files in zip: %s", available)

    # ------------------------------------------------------------------
    # stops: stop_id → name,  normalised-name → stop_id
    # ------------------------------------------------------------------
    stops_by_id    = {}
    stop_id_by_name = {}

    with _open_zip_text(zf, "stops.txt") as f:
        for row in csv.DictReader(f):
            sid  = row["stop_id"].strip()
            name = row["stop_name"].strip()
            stops_by_id[sid] = name
            stop_id_by_name[_norm(name)] = sid

    # ------------------------------------------------------------------
    # trips: trip_id → info,  train_number → [trip_ids]
    # ------------------------------------------------------------------
    trips_by_id    = {}
    trips_by_train = {}

    with _open_zip_text(zf, "trips.txt") as f:
        for row in csv.DictReader(f):
            tid  = row["trip_id"].strip()
            svc  = row["service_id"].strip()
            tnum = row.get("trip_short_name", "").strip()
            info = {"trip_id": tid, "service_id": svc, "train_number": tnum}
            trips_by_id[tid] = info
            if tnum:
                trips_by_train.setdefault(tnum, []).append(tid)

    # ------------------------------------------------------------------
    # stop_times: trip_id → sorted list of stop dicts
    # ------------------------------------------------------------------
    stop_times = {}

    with _open_zip_text(zf, "stop_times.txt") as f:
        for row in csv.DictReader(f):
            tid = row["trip_id"].strip()
            sid = row["stop_id"].strip()
            seq = int(row["stop_sequence"])
            arr = _hms_to_sec(row.get("arrival_time", "") or row.get("departure_time", ""))
            dep = _hms_to_sec(row.get("departure_time", "") or row.get("arrival_time", ""))
            stop_times.setdefault(tid, []).append({
                "stop_id":       sid,
                "stop_sequence": seq,
                "arrival_sec":   arr,
                "departure_sec": dep,
            })

    for tid in stop_times:
        stop_times[tid].sort(key=lambda x: x["stop_sequence"])

    # ------------------------------------------------------------------
    # calendar: service_id → set of dates it runs
    # ------------------------------------------------------------------
    calendar = {}

    if "calendar.txt" in available:
        dow_cols = ["monday", "tuesday", "wednesday", "thursday",
                    "friday", "saturday", "sunday"]
        with _open_zip_text(zf, "calendar.txt") as f:
            for row in csv.DictReader(f):
                svc   = row["service_id"].strip()
                start = _parse_date(row["start_date"])
                end   = _parse_date(row["end_date"])
                days  = [row[d].strip() == "1" for d in dow_cols]
                dates = set()
                cur   = start
                while cur <= end:
                    if days[cur.weekday()]:
                        dates.add(cur)
                    cur += timedelta(days=1)
                calendar[svc] = dates
    else:
        log.warning("calendar.txt not found in zip — all trips treated as active")

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

    # Fallback: return first candidate when calendar has no matching date
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

    m = re.search(r'Kolodvor\s*:?\s*</I>\s*(?:<strong>)?([^<\r\n]+)', text, re.IGNORECASE)
    if m:
        raw = m.group(1).strip()
        result["current_station"] = raw.replace("+", " ").replace(".", " ").strip()

    m = re.search(r'Relacija\s*:?\s*(?:<br>)?\s*([^\r\n<]+)', text, re.IGNORECASE)
    if m:
        result["route"] = m.group(1).strip()

    m = re.search(r'Kasni\s+(\d+)\s*min', text, re.IGNORECASE)
    if m:
        result["delay_min"] = int(m.group(1))

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
    Given the parsed train payload from hzpp.app and the GTFS static trip,
    produce a list of StopTimeUpdate dicts for the GTFS-RT feed.
    """
    st_list = gtfs["stop_times"].get(trip_id, [])
    if not st_list:
        return []

    stop_id_by_name = gtfs["stop_id_by_name"]
    stops_by_id     = gtfs["stops_by_id"]

    delay_min        = int(train_payload.get("delay_min", 0))
    global_delay_sec = delay_min * 60
    current_station  = _norm(train_payload.get("current_station") or "")
    finished         = train_payload.get("finished", False)

    updates = []

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

_feed_lock          = threading.Lock()
_current_feed_bytes = b""
_last_update_time   = 0


def build_feed(gtfs, updates_map):
    """Build a complete GTFS-RT FeedMessage from trip_id → [StopTimeUpdate dicts]."""
    feed = gtfs_realtime_pb2.FeedMessage()
    feed.header.gtfs_realtime_version = "2.0"
    feed.header.incrementality        = feed.header.FULL_DATASET
    feed.header.timestamp             = int(time.time())

    for trip_id, stu_list in updates_map.items():
        if not stu_list:
            continue
        entity                   = feed.entity.add()
        entity.id                = trip_id
        trip_update              = entity.trip_update
        trip_update.trip.trip_id = trip_id

        for stu in stu_list:
            s                 = trip_update.stop_time_update.add()
            s.stop_id         = stu["stop_id"]
            s.stop_sequence   = stu["stop_sequence"]
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

            updates_map = {}
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
# FLASK ENDPOINTS
# ---------------------------------------------------------------------------

app = Flask(__name__)


@app.route("/hzpp-rt")
def hzpp_rt():
    with _feed_lock:
        pb = _current_feed_bytes

    if not pb:
        feed = gtfs_realtime_pb2.FeedMessage()
        feed.header.gtfs_realtime_version = "2.0"
        feed.header.incrementality        = feed.header.FULL_DATASET
        feed.header.timestamp             = int(time.time())
        pb = feed.SerializeToString()

    return Response(pb, mimetype="application/x-protobuf")


@app.route("/status")
def status():
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
    return s.strip().lower() if s else ""


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
    h, m  = int(parts[0]), int(parts[1])
    now   = datetime.now(TIMEZONE)
    return now.replace(hour=h % 24, minute=m, second=0, microsecond=0)


# ---------------------------------------------------------------------------
# ENTRY POINT
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="HZPP GTFS-RT server")
    parser.add_argument(
        "--gtfs-url", default=GTFS_ZIP_URL,
        help=f"URL of the HZPP GTFS zip (default: {GTFS_ZIP_URL})"
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

    gtfs = load_gtfs(args.gtfs_url)

    scraper = threading.Thread(target=scrape_loop, args=(gtfs,), daemon=True)
    scraper.start()

    print(f"\n🚆 HŽPP GTFS-RT server")
    print(f"   Feed endpoint  : http://localhost:{args.port}/hzpp-rt")
    print(f"   Status page    : http://localhost:{args.port}/status")
    print(f"   GTFS source    : {args.gtfs_url}")
    print(f"   Scrape interval: {REFRESH_INTERVAL}s\n")

    app.run(host="0.0.0.0", port=args.port)