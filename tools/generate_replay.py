"""data/replay/ankara-kayit.csv örnek kayıt dosyasını üretir.

Kullanım:  python tools/generate_replay.py
Çıktı deterministiktir (sabit seed); dosya depoda hazır olarak bulunur, bu betik yalnızca
nasıl üretildiğini göstermek ve yeniden üretebilmek içindir.
"""
import math
import random
from pathlib import Path

SEED = 2026
DURATION_S = 300
STEP_S = 2.0

# [enlem, boylam] - Çankaya / Tunalı / Kavaklıdere çevresi (simülatör senaryosundan farklı rotalar)
ROUTES = {
    "ALFA-1": ([(39.9060, 32.8600), (39.9030, 32.8620), (39.8950, 32.8640), (39.9000, 32.8520)], 14.0),
    "BRAVO-2": ([(39.9208, 32.8541), (39.9120, 32.8575), (39.9060, 32.8600), (39.9150, 32.8480)], 11.0),
    "CHARLIE-3": ([(39.9100, 32.8400), (39.9180, 32.8100), (39.9000, 32.8280)], 17.0),
}


def haversine(a, b):
    r = 6371008.8
    la1, lo1, la2, lo2 = map(math.radians, (a[0], a[1], b[0], b[1]))
    h = math.sin((la2 - la1) / 2) ** 2 + math.cos(la1) * math.cos(la2) * math.sin((lo2 - lo1) / 2) ** 2
    return 2 * r * math.asin(min(1.0, math.sqrt(h)))


def bearing(a, b):
    la1, lo1, la2, lo2 = map(math.radians, (a[0], a[1], b[0], b[1]))
    y = math.sin(lo2 - lo1) * math.cos(la2)
    x = math.cos(la1) * math.sin(la2) - math.sin(la1) * math.cos(la2) * math.cos(lo2 - lo1)
    return (math.degrees(math.atan2(y, x)) + 360) % 360


def main():
    rng = random.Random(SEED)
    state = {c: [0, 0.0] for c in ROUTES}  # segment, metres into segment
    lines = [
        "# GeoCommand kayıt dosyası - tools/generate_replay.py ile üretildi (seed=%d)" % SEED,
        "# offset_seconds: oynatma başlangıcına göre saniye. Koordinatlar WGS84, ondalık ayırıcı nokta.",
        "offset_seconds,callsign,latitude,longitude,speed_mps,heading_deg",
    ]
    t = 0.0
    while t <= DURATION_S:
        for callsign, (route, base_speed) in ROUTES.items():
            loop = route + [route[0]]
            seg, into = state[callsign]
            speed = base_speed * (1 + 0.1 * (2 * rng.random() - 1))
            if t > 0:
                remaining = speed * STEP_S
                while remaining > 0:
                    length = haversine(loop[seg], loop[seg + 1])
                    if remaining < length - into:
                        into += remaining
                        remaining = 0
                    else:
                        remaining -= length - into
                        seg = (seg + 1) % (len(loop) - 1)
                        into = 0.0
            state[callsign] = [seg, into]
            a, b = loop[seg], loop[seg + 1]
            f = into / haversine(a, b)
            lat = a[0] + (b[0] - a[0]) * f
            lon = a[1] + (b[1] - a[1]) * f
            lines.append("%.1f,%s,%.7f,%.7f,%.2f,%.1f" % (t, callsign, lat, lon, speed, bearing(a, b) % 360))
        t += STEP_S

    out = Path(__file__).resolve().parent.parent / "data" / "replay" / "ankara-kayit.csv"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"{out} yazıldı ({len(lines) - 3} satır)")


if __name__ == "__main__":
    main()
