"""Independent offline replay of exported FFT distance bins and natural spline."""
from bisect import bisect_right
from pathlib import Path
import json
import math

ROOT = Path(__file__).resolve().parents[3]
RUN = ROOT / "artifacts/validation/diffraction-energy-20261029"


def spline(x, y, targets):
    # Solve the natural-spline tridiagonal system for the interior curvatures.
    widths = [b-a for a, b in zip(x, x[1:])]
    slopes = [(b-a)/h for a, b, h in zip(y, y[1:], widths)]
    diagonal = [2*(widths[i]+widths[i+1]) for i in range(len(x)-2)]
    rhs = [6*(b-a) for a, b in zip(slopes, slopes[1:])]
    for i in range(1, len(diagonal)):
        factor = widths[i]/diagonal[i-1]
        diagonal[i] -= factor*widths[i]
        rhs[i] -= factor*rhs[i-1]
    second = [0.0]*len(x)
    for i in range(len(diagonal)-1, -1, -1):
        second[i+1] = (rhs[i]-widths[i+1]*second[i+2])/diagonal[i]
    values = []
    for t in targets:
        i = min(max(bisect_right(x, t)-1, 0), len(x)-2)
        h = widths[i]
        a, b = (x[i+1]-t)/h, (t-x[i])/h
        values.append(a*y[i]+b*y[i+1]+h*h*((a*a*a-a)*second[i]+(b*b*b-b)*second[i+1])/6)
    return values


def linear(x, y, t):
    i = min(max(bisect_right(x, t)-1, 0), len(x)-2)
    return y[i]+(y[i+1]-y[i])*(t-x[i])/(x[i+1]-x[i])


def compare(ax, ay, bx, by):
    lo, hi = max(ax[0], bx[0]), min(ax[-1], bx[-1])
    union = sorted({t for t in ax+bx if lo <= t <= hi})
    a = [linear(ax, ay, t) for t in union]
    b = [linear(bx, by, t) for t in union]
    errors = [abs(u-v) for u, v in zip(a, b)]
    return {"count": len(union), "maximumAbsolute": max(errors),
            "violations": sum(e > 1e-5+.002*abs(v) for e, v in zip(errors, b))}


def main():
    d = json.loads(next((RUN / "before").glob("*beam*.json")).read_text(encoding="utf-8-sig"))
    after = json.loads(next((RUN / "after").glob("*beam*.json")).read_text(encoding="utf-8-sig"))
    records = []
    for control in d["controls"]:
        psf = control["psf"]
        size = len(psf)
        pitch = control["sampleSpacingMicrometers"]
        samples = [((x-size/2+1)*pitch, (y-size/2+1)*pitch, psf[(y+1)%size][(x+1)%size])
                   for y in range(size) for x in range(size)]
        total = sum(p for x, y, p in samples)
        cx = sum(x*p for x, y, p in samples)/total
        cy = sum(y*p for x, y, p in samples)/total
        distances = [(math.hypot(x-cx, y-cy), p) for x, y, p in samples]
        def fraction(r):
            return sum(p*min(max((r-distance)/pitch+.5, 0), 1) for distance, p in distances)/total
        n = d["request"]["pupilSampling"]
        maximum = float(d["request"]["workbenchSettings"]["MaximumDistanceMicrometers"])
        knots = [maximum*i/(n-1) for i in range(n)]
        energy = [fraction(r) for r in knots]
        energy[0], energy[1] = 0, energy[2]/4
        targets = [maximum*i/400 for i in range(401)]
        values = spline(knots, energy, targets)
        previous = 0
        for i, value in enumerate(values):
            values[i] = previous = min(max(previous, value), 1)
        index = 0 if control["ignoreOpd"] else 1
        actual, native = d["actual"]["series"][index], d["expected"]["series"][index]
        replay_error = max(abs(a-b) for a, b in zip(values, actual["y"]))
        if not control["interpolate"]:
            assert control["maximumDifferenceFromProduction"] == 0
            assert replay_error < 3e-14
        else:
            corrected = after["actual"]["series"][index]["y"]
            assert max(abs(a-b) for a, b in zip(values, corrected)) < 3e-14
            assert next(c for c in after["controls"] if c["ignoreOpd"] == control["ignoreOpd"] and c["interpolate"])["maximumDifferenceFromProduction"] == 0
        pupil = control["pupil"]
        other = next(c for c in d["controls"] if c["ignoreOpd"] == control["ignoreOpd"] and not c["interpolate"])
        mask = [(p["x"],p["y"]) for p in pupil if math.hypot(p["real"],p["imaginary"]) > 0]
        old_mask = [(p["x"],p["y"]) for p in other["pupil"] if math.hypot(p["real"],p["imaginary"]) > 0]
        assert mask == old_mask and len(mask) == 405
        changed = sum(math.hypot(p["real"],p["imaginary"]) != math.hypot(q["real"],q["imaginary"])
                      for p, q in zip(pupil, other["pupil"]))
        records.append({"ignoreOpd": control["ignoreOpd"], "interpolateAmplitude": control["interpolate"],
            "pitchMilliradians": pitch, "unchangedPupilMaskNodes": len(mask), "normalizedPupilValuesDifferent": changed,
            "centroidMilliradians": [cx,cy], "maximumReplayDifferenceFromProduction": replay_error,
            "metric": compare(targets, values, native["x"], native["y"]),
            "x": targets, "y": values})
    (RUN / "decomposition.json").write_text(json.dumps({"controls": records}, indent=2)+"\n", encoding="utf-8")
    print(json.dumps([{k:v for k,v in r.items() if k not in ["x","y"]} for r in records], indent=2))


if __name__ == "__main__":
    main()
