"""GPS alıcısı yerine geçen NMEA 0183 yayıncısı (Nmea plugin'ini donanımsız denemek için).

data/replay/ankara-kayit.csv kaydındaki her araç için ayrı bir TCP portu açar ve bağlanan istemciye
o aracın konumlarını $GPRMC cümleleri olarak gönderir. Zaman damgası gönderim anının UTC saatidir.

Kullanım:
    python tools/nmea_emitter.py                  # ALFA-1:10110, BRAVO-2:10111, CHARLIE-3:10112
    python tools/nmea_emitter.py --speed 5        # kaydı 5 kat hızlı oynat
    python tools/nmea_emitter.py --corrupt 0.05   # cümlelerin %5'inin sağlama toplamını boz (hata yolunu görmek için)

Yalnızca standart kütüphane kullanır. Ctrl+C ile durur.
"""
import argparse
import csv
import random
import socket
import threading
import time
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
KNOTS_PER_MPS = 3600 / 1852


def checksum(body: str) -> str:
    value = 0
    for ch in body:
        value ^= ord(ch)
    return f"{value:02X}"


def rmc(lat: float, lon: float, speed_mps: float, heading: float, now: datetime) -> str:
    def dm(value: float, degree_digits: int) -> str:
        value = abs(value)
        degrees = int(value)
        minutes = (value - degrees) * 60
        return f"{degrees:0{degree_digits}d}{minutes:07.4f}"

    body = ",".join([
        "GPRMC",
        now.strftime("%H%M%S.") + f"{now.microsecond // 10000:02d}",
        "A",
        dm(lat, 2), "N" if lat >= 0 else "S",
        dm(lon, 3), "E" if lon >= 0 else "W",
        f"{speed_mps * KNOTS_PER_MPS:.1f}",
        f"{heading:.1f}",
        now.strftime("%d%m%y"),
        "", "",
        "A",
    ])
    return f"${body}*{checksum(body)}\r\n"


def load(path: Path):
    tracks = {}
    with path.open(encoding="utf-8") as f:
        rows = csv.DictReader(line for line in f if not line.startswith("#"))
        for row in rows:
            tracks.setdefault(row["callsign"], []).append((
                float(row["offset_seconds"]), float(row["latitude"]), float(row["longitude"]),
                float(row["speed_mps"]), float(row["heading_deg"])))
    return tracks


def serve(callsign: str, port: int, track, speed: float, corrupt: float, stop: threading.Event):
    server = socket.create_server(("127.0.0.1", port))
    server.settimeout(0.5)
    print(f"{callsign}: 127.0.0.1:{port} dinleniyor")
    while not stop.is_set():
        try:
            conn, peer = server.accept()
        except socket.timeout:
            continue
        print(f"{callsign}: {peer[0]}:{peer[1]} bağlandı")
        try:
            while not stop.is_set():  # kayıt bitince baştan
                start = time.monotonic()
                for offset, lat, lon, spd, hdg in track:
                    delay = start + offset / speed - time.monotonic()
                    if delay > 0 and stop.wait(delay):
                        break
                    line = rmc(lat, lon, spd, hdg, datetime.now(timezone.utc))
                    if random.random() < corrupt:
                        line = line[:-4] + "00\r\n"
                    conn.sendall(line.encode("ascii"))
        except (ConnectionError, OSError):
            print(f"{callsign}: istemci ayrıldı")
        finally:
            conn.close()
    server.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--file", type=Path, default=ROOT / "data" / "replay" / "ankara-kayit.csv")
    parser.add_argument("--base-port", type=int, default=10110)
    parser.add_argument("--speed", type=float, default=1.0)
    parser.add_argument("--corrupt", type=float, default=0.0)
    args = parser.parse_args()

    stop = threading.Event()
    threads = []
    for i, (callsign, track) in enumerate(sorted(load(args.file).items())):
        t = threading.Thread(target=serve, args=(callsign, args.base_port + i, track, args.speed, args.corrupt, stop), daemon=True)
        t.start()
        threads.append(t)
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        stop.set()
        for t in threads:
            t.join(timeout=2)


if __name__ == "__main__":
    main()
