"""GPS alıcısı yerine geçen NMEA 0183 yayıncısı (Nmea plugin'ini donanımsız denemek için).

data/replay/ankara-kayit.csv kaydındaki her araç için ayrı bir TCP portu açar ve bağlanan istemciye
o aracın konumlarını $GPRMC cümleleri olarak gönderir. Zaman damgası gönderim anının UTC saatidir.

Kullanım:
    python tools/nmea_emitter.py                  # ALFA-1:10110, BRAVO-2:10111, CHARLIE-3:10112
    python tools/nmea_emitter.py --speed 5        # kaydı 5 kat hızlı oynat
    python tools/nmea_emitter.py --corrupt 0.05   # cümlelerin %5'inin sağlama toplamını boz (hata yolunu görmek için)
    python tools/nmea_emitter.py --serial COM11 --callsign ALFA-1 [--baud 4800]
                                                  # TCP yerine bir aracı seri porttan (RS-232) yayınla

Seri port için sanal bir port çifti gerekir (Windows: com0com, ör. COM10<->COM11; Linux:
socat -d -d pty,raw,echo=0 pty,raw,echo=0). Yayıncı çiftin bir ucuna yazar, API diğer ucunu okur.
TCP modu yalnızca standart kütüphaneyi kullanır; seri mod pyserial ister (pip install pyserial).
Ctrl+C ile durur.
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


def emit(send, track, speed: float, corrupt: float, stop: threading.Event) -> bool:
    """Kaydı bir kez oynatır; durdurulursa False döner. send: tek cümleyi (bayt) yazan işlev."""
    start = time.monotonic()
    for offset, lat, lon, spd, hdg in track:
        delay = start + offset / speed - time.monotonic()
        if delay > 0 and stop.wait(delay):
            return False
        line = rmc(lat, lon, spd, hdg, datetime.now(timezone.utc))
        if random.random() < corrupt:
            line = line[:-4] + "00\r\n"
        send(line.encode("ascii"))
    return not stop.is_set()


def serve_serial(port: str, baud: int, callsign: str, track, speed: float, corrupt: float, stop: threading.Event):
    try:
        import serial  # pyserial; yalnızca seri modda gerekir
    except ImportError:
        raise SystemExit("Seri mod için pyserial gerekli: pip install pyserial")
    # serial_for_url: "COM11", "/dev/pts/3" veya test için "loop://" kabul eder.
    with serial.serial_for_url(port, baudrate=baud, bytesize=8, parity="N", stopbits=1, write_timeout=2) as line:
        print(f"{callsign}: {port} ({baud} 8N1) üzerinden yayınlanıyor")
        while emit(line.write, track, speed, corrupt, stop):  # kayıt bitince baştan
            pass


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
            while emit(conn.sendall, track, speed, corrupt, stop):  # kayıt bitince baştan
                pass
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
    parser.add_argument("--serial", help="TCP yerine bu seri porta yaz (ör. COM11)")
    parser.add_argument("--callsign", help="seri modda yayınlanacak araç (varsayılan: kayıttaki ilk araç)")
    parser.add_argument("--baud", type=int, default=4800, help="seri hız; NMEA 0183 standardı 4800")
    args = parser.parse_args()

    tracks = sorted(load(args.file).items())
    stop = threading.Event()
    threads = []
    if args.serial:
        callsign = args.callsign or tracks[0][0]
        track = dict(tracks).get(callsign)
        if track is None:
            raise SystemExit(f"'{callsign}' kayıtta yok. Mevcut: {', '.join(c for c, _ in tracks)}")
        targets = [(serve_serial, (args.serial, args.baud, callsign, track, args.speed, args.corrupt, stop))]
    else:
        targets = [(serve, (callsign, args.base_port + i, track, args.speed, args.corrupt, stop))
                   for i, (callsign, track) in enumerate(tracks)]
    for target, target_args in targets:
        t = threading.Thread(target=target, args=target_args, daemon=True)
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
