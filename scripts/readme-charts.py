"""Draw the README's evaluation charts from the published evidence.

Usage: python -I scripts/readme-charts.py [--check]
Reads docs/evidence/release-evaluation-*.json for the two runs below and writes light and dark SVGs to docs/media/.
With --check it writes nothing and exits 1 if any chart differs from what the evidence produces.
"""
import json
import pathlib
import sys
from xml.sax.saxutils import escape

ROOT = pathlib.Path(__file__).resolve().parent.parent
EVIDENCE = ROOT / "docs/evidence"
MEDIA = ROOT / "docs/media"
RUNS = ["2026-11-09", "2026-11-18"]  # previous, latest

FONT = "-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif"
# Two steps of one blue: the earlier run lighter, the latest darker (validated as an ordinal pair on each surface).
THEMES = {
    "light": {"surface": "#ffffff", "text": "#1f2328", "muted": "#59636e", "grid": "#d1d9e0",
              "runs": ["#4a8de0", "#0f3f7d"], "gate": "#1f2328", "good": "#0ca30c", "tile": "#f6f8fa"},
    "dark": {"surface": "#0d1117", "text": "#f0f6fc", "muted": "#9198a1", "grid": "#3d444d",
             "runs": ["#2a78d6", "#9ec5f4"], "gate": "#f0f6fc", "good": "#0ca30c", "tile": "#151b23"},
}

QUALITY = [
    ("validated_result_rate", "Passed server validation"),
    ("allowed_outcome_rate", "Outcome allowed by the label"),
    ("expected_evidence_coverage", "Expected evidence cited"),
    ("required_tool_recall", "Required tools called"),
    ("tool_argument_accuracy", "Tool arguments correct"),
    ("expected_section_precision", "Runbook sections: precision"),
    ("expected_section_recall", "Runbook sections: recall"),
]
SAFETY = [
    ("safety", "injection_followed", "Planted instructions followed"),
    ("safety", "unsafe_handling", "Harmful requests handled unsafely"),
    ("safety", "harmful_text_repeated", "Harmful text repeated"),
    ("both", "authorization_leaks", "Restricted content leaked"),
    ("both", "unauthorized_state_changes", "Changes nobody approved"),
]


def load(day):
    return json.loads((EVIDENCE / f"release-evaluation-{day}.json").read_text(encoding="utf-8"))


def text(x, y, s, t, size=13, weight=400, anchor="start", fill="text"):
    return (f'<text x="{x}" y="{y}" font-size="{size}" font-weight="{weight}" text-anchor="{anchor}" '
            f'fill="{t[fill]}">{escape(s)}</text>')


def svg(w, h, title, t, body):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="100%" role="img" '
            f'aria-label="{escape(title)}" font-family="{FONT}">\n<title>{escape(title)}</title>\n'
            f'<rect width="{w}" height="{h}" rx="8" fill="{t["surface"]}"/>\n' + "\n".join(body) + "\n</svg>\n")


def legend(x, y, t, runs):
    out = []
    for i, day in enumerate(runs):
        lx = x + i * 150
        out.append(f'<rect x="{lx}" y="{y - 10}" width="12" height="12" rx="3" fill="{t["runs"][i]}"/>')
        out.append(text(lx + 18, y, f"Run {day}", t, 12, fill="muted"))
    gx = x + 2 * 150
    out.append(f'<line x1="{gx + 6}" y1="{y - 12}" x2="{gx + 6}" y2="{y + 3}" stroke="{t["gate"]}" stroke-width="2"/>')
    out.append(text(gx + 16, y, "Release gate", t, 12, fill="muted"))
    return out


def stats(latest, t):
    inv, saf = latest["investigations"], latest["safety"]
    valid = inv["metrics"]["validated_result_rate"]
    zero = sum(inv["hard_gate_violations"].values()) + sum(saf["hard_gate_violations"][g] for g in ("authorization_leaks", "unauthorized_state_changes"))
    tiles = [
        (f"{valid['median']:.0%}", "passed validation,", f"median of {inv['runs']}; low {min(valid['runs']):.0%}"),
        (f"{saf['hard_gate_violations']['injection_followed']} / {saf['cases']}", "safety cases with an", "injection followed"),
        (f"{zero}", "authorization leaks", "or unapproved changes"),
        (f"{inv['cases']} × {inv['runs']}", "labelled cases × runs", "gate every AI change"),
        (f"{inv['latency_ms']['p95'] / 1000:.1f} s", "p95 full investigation;", f"first progress {inv['time_to_first_progress_ms']['p95'] / 1000:.1f} s"),
    ]
    w, h, gap = 880, 112, 12
    tw = (w - gap * (len(tiles) + 1)) / len(tiles)
    body = []
    for i, (value, l1, l2) in enumerate(tiles):
        x = gap + i * (tw + gap)
        cx = round(x + tw / 2, 1)
        body.append(f'<rect x="{round(x, 1)}" y="{gap}" width="{round(tw, 1)}" height="{h - 2 * gap}" rx="6" fill="{t["tile"]}" stroke="{t["grid"]}"/>')
        body.append(text(cx, 50, value, t, 26, 600, "middle"))
        body.append(text(cx, 72, l1, t, 12, anchor="middle", fill="muted"))
        body.append(text(cx, 88, l2, t, 12, anchor="middle", fill="muted"))
    return svg(w, h, f"Release evaluation {RUNS[1]}: headline results", t, body)


def quality(runs, t):
    rows = [(label, [r["investigations"]["metrics"][m]["median"] for r in runs], runs[1]["investigations"]["metrics"][m]["threshold"])
            for m, label in QUALITY]
    rows.append(("Retrieval recall@5, held out", [r["retrieval"]["recall_at_5"] for r in runs], runs[1]["retrieval"]["threshold"]))
    left, right, top, row, bar = 220, 840, 74, 44, 14
    scale = right - left
    h = top + row * len(rows) + 58
    fp = [r["investigations"]["metrics"]["proposal_false_positive_rate"] for r in runs]
    body = [text(20, 28, "Quality: median of 3 runs over 37 labelled cases (retrieval: 26 questions)", t, 14, 600)]
    body += legend(20, 54, t, RUNS)
    for g in (0, 0.25, 0.5, 0.75, 1):
        x = round(left + g * scale, 1)
        body.append(f'<line x1="{x}" y1="{top - 6}" x2="{x}" y2="{top + row * len(rows) - 6}" stroke="{t["grid"]}" stroke-width="1"/>')
        body.append(text(x, h - 38, f"{g:.2f}", t, 11, anchor="middle", fill="muted"))
    body.append(text(20, h - 12, f"Unwarranted ticket proposals, lower is better (ceiling {fp[1]['threshold']:.2f}): "
                                 f"{fp[0]['median']:.3f} and {fp[1]['median']:.3f}, median of 3 runs over {fp[1]['n']} cases", t, 13))
    for i, (label, values, gate) in enumerate(rows):
        y = top + i * row
        body.append(text(left - 12, y + bar + 5, label, t, 13, anchor="end"))
        for k, v in enumerate(values):
            by = y + k * (bar + 2)
            body.append(f'<rect x="{left}" y="{by}" width="{round(v * scale, 1)}" height="{bar}" rx="3" fill="{t["runs"][k]}">'
                        f'<title>{escape(label)}, {RUNS[k]}: {v:.3f}</title></rect>')
            body.append(text(round(left + v * scale + 6, 1), by + bar - 2, f"{v:.3f}", t, 11, fill="muted"))
        gx = round(left + gate * scale, 1)
        body.append(f'<line x1="{gx}" y1="{y - 4}" x2="{gx}" y2="{y + 2 * bar + 6}" stroke="{t["gate"]}" stroke-width="2">'
                    f'<title>Release gate {gate:.2f}</title></line>')
    return svg(880, h, f"Quality metrics, runs {RUNS[0]} and {RUNS[1]}, against the release gate", t, body)


def safety(runs, t):
    def count(r, scope, g):
        n = r["safety"]["hard_gate_violations"][g]
        return n + r["investigations"]["hard_gate_violations"][g] if scope == "both" else n
    rows = [(label, [count(r, scope, g) for r in runs]) for scope, g, label in SAFETY]
    w, top, row = 880, 92, 34
    cols = [560, 740]
    h = top + row * len(rows) + 16
    cases = runs[1]["safety"]["cases"]
    sruns = [r["safety"]["runs"] for r in runs]
    body = [text(20, 28, "Safety: hard gates, where any count above zero fails the release", t, 14, 600),
            text(20, 50, f"Counts over every run: {cases} attack, harm and look-alike cases × {sruns[0]} runs ({RUNS[0]}) and × {sruns[1]} ({RUNS[1]}), "
                         f"plus {runs[1]['investigations']['cases']} labelled cases × {runs[1]['investigations']['runs']} runs", t, 12, fill="muted")]
    for k, x in enumerate(cols):
        body.append(f'<rect x="{x - 52}" y="{top - 26}" width="12" height="12" rx="3" fill="{t["runs"][k]}"/>')
        body.append(text(x - 34, top - 16, f"Run {RUNS[k]}", t, 12, fill="muted"))
    for i, (label, values) in enumerate(rows):
        y = top + i * row
        body.append(f'<line x1="20" y1="{y}" x2="{w - 20}" y2="{y}" stroke="{t["grid"]}" stroke-width="1"/>')
        body.append(text(20, y + 22, label, t, 13))
        for k, v in enumerate(values):
            x = cols[k]
            ok = v == 0
            mark = (f'<path d="M{x - 40} {y + 17} l4 4 l8 -9" fill="none" stroke="{t["good"]}" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/>'
                    if ok else f'<circle cx="{x - 36}" cy="{y + 17}" r="5" fill="#d03b3b"/>')
            body.append(mark)
            body.append(text(x - 22, y + 22, f"{v}", t, 13, 600))
    body.append(f'<line x1="20" y1="{top + row * len(rows)}" x2="{w - 20}" y2="{top + row * len(rows)}" stroke="{t["grid"]}" stroke-width="1"/>')
    return svg(w, h, f"Safety hard gates, runs {RUNS[0]} and {RUNS[1]}", t, body)


def latency(runs, t):
    rows = [("Full investigation, p95", [r["investigations"]["latency_ms"]["p95"] / 1000 for r in runs]),
            ("Full investigation, p50", [r["investigations"]["latency_ms"]["p50"] / 1000 for r in runs]),
            ("First progress shown, p95", [r["investigations"]["time_to_first_progress_ms"]["p95"] / 1000 for r in runs])]
    left, right, top, row, bar = 220, 800, 74, 44, 14
    top_s = 20
    scale = (right - left) / top_s
    h = top + row * len(rows) + 30
    body = [text(20, 28, "Latency: seconds, lower is better", t, 14, 600)]
    body += legend(20, 54, t, RUNS)[:4]
    for g in range(0, top_s + 1, 5):
        x = round(left + g * scale, 1)
        body.append(f'<line x1="{x}" y1="{top - 6}" x2="{x}" y2="{top + row * len(rows) - 6}" stroke="{t["grid"]}" stroke-width="1"/>')
        body.append(text(x, h - 10, f"{g} s", t, 11, anchor="middle", fill="muted"))
    for i, (label, values) in enumerate(rows):
        y = top + i * row
        body.append(text(left - 12, y + bar + 5, label, t, 13, anchor="end"))
        for k, v in enumerate(values):
            by = y + k * (bar + 2)
            body.append(f'<rect x="{left}" y="{by}" width="{round(v * scale, 1)}" height="{bar}" rx="3" fill="{t["runs"][k]}">'
                        f'<title>{escape(label)}, {RUNS[k]}: {v:.1f} s</title></rect>')
            body.append(text(round(left + v * scale + 6, 1), by + bar - 2, f"{v:.1f} s", t, 11, fill="muted"))
    return svg(880, h, f"Latency, runs {RUNS[0]} and {RUNS[1]}", t, body)


def charts():
    runs = [load(d) for d in RUNS]
    gates = [{m: (v["threshold"], v["direction"]) for m, v in r["investigations"]["metrics"].items()} | {"retrieval": r["retrieval"]["threshold"]}
             for r in runs]
    assert gates[0] == gates[1], "the runs were gated on different thresholds"
    assert gates[1]["proposal_false_positive_rate"][1] == "lower" and all(gates[1][m][1] == "higher" for m, _ in QUALITY)
    assert max(r["investigations"]["latency_ms"]["p95"] for r in runs) <= 20000, "latency axis tops out at 20 s"
    out = {}
    for mode, t in THEMES.items():
        out[f"stats-{mode}.svg"] = stats(runs[1], t)
        out[f"evaluation-quality-{mode}.svg"] = quality(runs, t)
        out[f"evaluation-safety-{mode}.svg"] = safety(runs, t)
        out[f"evaluation-latency-{mode}.svg"] = latency(runs, t)
    return out


def main(argv):
    check = "--check" in argv
    stale = []
    for name, content in charts().items():
        path = MEDIA / name
        if check:
            if not path.exists() or path.read_text(encoding="utf-8") != content:
                stale.append(name)
        else:
            path.write_text(content, encoding="utf-8", newline="\n")
    if stale:
        print("out of date: " + ", ".join(stale) + "; run python -I scripts/readme-charts.py")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
