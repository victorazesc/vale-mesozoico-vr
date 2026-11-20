#!/usr/bin/env python3
"""Read only analysis of the temporary Unity locomotion CSV."""
import csv
import math
import statistics
import sys
from collections import defaultdict
from pathlib import Path


def analyze(run):
    path = Path(run)
    rows = list(csv.DictReader((path / "motion.csv").open()))
    grouped = defaultdict(list)
    for row in rows:
        grouped[(row["actor"], int(row["leg"]))].append(row)
    print(f"RUN {path.name}")
    for (actor, leg), samples in sorted(grouped.items()):
        phases = [float(r["normalizedTime"]) for r in samples]
        elapsed = float(samples[-1]["time"]) - float(samples[0]["time"])
        cycles = phases[-1] - phases[0]
        touchdown = [r for i, r in enumerate(samples) if i and samples[i - 1]["swing"] == "1" and r["swing"] == "0"]
        intervals = [float(b["time"]) - float(a["time"]) for a, b in zip(touchdown, touchdown[1:])]
        stance_slip = [float(r["stanceSlip"]) for r in samples]
        replants = sum(int(r["stanceReplant"]) for r in samples)
        phase_at_contact = [float(r["normalizedTime"]) % 1 for r in touchdown]
        # A stable touchdown should occur once per cycle, with a narrow circular phase distribution.
        if phase_at_contact:
            x = statistics.mean(math.cos(2 * math.pi * t) for t in phase_at_contact)
            y = statistics.mean(math.sin(2 * math.pi * t) for t in phase_at_contact)
            concentration = (x * x + y * y) ** 0.5
        else:
            concentration = 0
        print(f" {actor} leg={leg}: seconds={elapsed:.3f} cycles={cycles:.3f} touchdowns={len(touchdown)} "
              f"phaseConcentration={concentration:.3f} phases={','.join(f'{p:.3f}' for p in phase_at_contact)} "
              f"minStepInterval={min(intervals, default=0):.3f}s maxSlip={max(stance_slip):.4f}m replants={replants}")


for run in sys.argv[1:]:
    analyze(run)
