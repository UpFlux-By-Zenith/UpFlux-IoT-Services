#!/usr/bin/env python3
"""
Flask micro-service that exposes

  • POST /ai/clustering   – DBSCAN → PCA(2D) + synthetic points for plotting
  • POST /ai/scheduling   – window‑aware greedy scheduler
"""

from flask import Flask, request, jsonify
from sklearn.cluster import DBSCAN
from sklearn.decomposition import PCA
from sklearn.preprocessing import StandardScaler
import numpy as np
import datetime
import random
import os

app = Flask(__name__)

DBSCAN_EPS          = float(3.5)
DBSCAN_MIN_SAMPLES  = int  (2)

# total dots to render
TARGET_TOTAL_POINTS = int  (100)

# spread of fake dots
SYNTHETIC_SIGMA     = float(0.12)

# seconds the FW chunk takes
UPDATE_PAYLOAD_SEC  = int  (25)
CLUSTER_SETUP_SEC   = int  (5)
MIN_CLUSTER_GAP_SEC = int  (30)


# ---------------------------------------------------------------------------


# ===========================================================================
#  /ai/clustering
# ===========================================================================
@app.route("/ai/clustering", methods=["POST"])
def clustering():
    """
    Expected body: list[{
        "deviceUuid": "...",
        "busyFraction": 0-1,
        "avgCpu": float,
        "avgMem": float,
        "avgNet": float
    }]

    Returns
    {
      "clusters":  [ { "clusterId":"0", "deviceUuids":[...] }, ... ],
      "plotData":  [ { "deviceUuid":"...", "x":.., "y":.., "clusterId":"0", "isSynthetic":false }, ... ]
    }
    """
    data = request.get_json()
    if not isinstance(data, list) or not data:
        return jsonify({"error": "Input must be a non-empty array"}), 400

    device_ids = [d["deviceUuid"] for d in data]
    matrix = np.array([
        [d["busyFraction"], d["avgCpu"], d["avgMem"], d["avgNet"]]
        for d in data
    ])

    # ---- scaling → DBSCAN --------------------------------------------------
    scaler     = StandardScaler()
    X_scaled   = scaler.fit_transform(matrix)
    db         = DBSCAN(eps=DBSCAN_EPS, min_samples=DBSCAN_MIN_SAMPLES)
    labels     = db.fit_predict(X_scaled)           # shape (N,)

    # ---- PCA for 2-D plot --------------------------------------------------
    pca        = PCA(n_components=2)
    coords_2d  = pca.fit_transform(X_scaled)        # shape (N,2)

    # ---- build real plot points + cluster list ----------------------------
    plot_data  = []
    cluster_map = {}
    for idx, lbl in enumerate(labels):
        lbl_str = str(lbl)
        cluster_map.setdefault(lbl_str, []).append(device_ids[idx])
        plot_data.append({
            "deviceUuid" : device_ids[idx],
            "x"          : float(coords_2d[idx, 0]),
            "y"          : float(coords_2d[idx, 1]),
            "clusterId"  : lbl_str,
            "isSynthetic": False
        })

    clusters = [
        {"clusterId": cid, "deviceUuids": devs}
        for cid, devs in cluster_map.items()
    ]

    # -----------------------------------------------------------------------
    #  add synthetic points purely for display -  these are for the fake devces (QCS)
    # -----------------------------------------------------------------------
    n_real   = len(plot_data)
    n_fake   = max(0, TARGET_TOTAL_POINTS - n_real)
    if n_fake:
        # centroid per cluster in PCA space
        centroids = {
            lbl: coords_2d[labels == int(lbl)].mean(axis=0)
            for lbl in cluster_map.keys()
        }
        # proportional allocation
        tot_size = sum(len(v) for v in cluster_map.values())
        fake_pts = []
        counter  = 0
        for lbl, size in cluster_map.items():
            share = max(1, round(n_fake * len(size) / tot_size))
            for _ in range(share):
                mu = centroids[lbl]
                sample = np.random.normal(loc=mu, scale=SYNTHETIC_SIGMA, size=2)
                fake_pts.append({
                    "deviceUuid": f"synthetic_{counter:04d}",
                    "x"         : float(sample[0]),
                    "y"         : float(sample[1]),
                    "clusterId" : lbl,
                    "isSynthetic": True
                })
                counter += 1
                if counter >= n_fake:
                    break
            if counter >= n_fake:
                break
        plot_data.extend(fake_pts)

    return jsonify({"clusters": clusters, "plotData": plot_data})


# ===========================================================================
#  /ai/scheduling
# ===========================================================================
@app.route("/ai/scheduling", methods=["POST"])
def scheduling():
    """
    Input JSON:
      {
        "clusters":      [ { "clusterId":"0", "deviceUuids":[...] }, ... ],
        "aggregatorData":[
              { "deviceUuid":"...", "nextIdleTime":"2025-05-01T12:00:00Z", "idleDurationSecs": 60 },
              ...
        ]
      }

    Output:
      { "clusters": [
          { "clusterId":"0", "deviceUuids":[...], "updateTimeUtc":"2025-05-01T12:00:05Z" },
          ...
        ]}
    """
    req            = request.get_json(force=True)
    clusters_input = req.get("clusters", [])
    agg_list       = req.get("aggregatorData", [])

    idle_map = {a["deviceUuid"]: a for a in agg_list}
    now       = datetime.datetime.now(datetime.timezone.utc)

    # ---- build feasible windows per cluster --------------------------------
    jobs = []
    for c in clusters_input:
        devs = c["deviceUuids"]
        windows = []
        for d in devs:
            info = idle_map.get(d)
            # any missing then skip cluster
            if not info or not info["nextIdleTime"]:
                break
            start = datetime.datetime.fromisoformat(
                info["nextIdleTime"].replace("Z", "+00:00"))
            dur   = info["idleDurationSecs"]
            if dur < UPDATE_PAYLOAD_SEC + CLUSTER_SETUP_SEC:
                break
            windows.append((start, start + datetime.timedelta(seconds=dur)))
        else:
            # all devices had a window – intersect them
            latest_start  = max(w[0] for w in windows)
            earliest_end  = min(w[1] for w in windows)
            if earliest_end >= latest_start + datetime.timedelta(seconds=UPDATE_PAYLOAD_SEC):
                jobs.append({
                    "clusterId"  : c["clusterId"],
                    "deviceUuids": devs,
                    "windowStart": latest_start,
                    "windowEnd"  : earliest_end
                })

    # ---- schedule greedily then adjust -------------------------------------
    jobs.sort(key=lambda j: j["windowStart"])
    scheduled = []
    current   = now

    for j in jobs:
        start = max(j["windowStart"], current)
        if start + datetime.timedelta(seconds=UPDATE_PAYLOAD_SEC) <= j["windowEnd"]:
            scheduled.append({
                "clusterId"  : j["clusterId"],
                "deviceUuids": j["deviceUuids"],
                "updateTimeUtc": start.isoformat().replace("+00:00", "Z")
            })
            # next job cannot start before this ends + minimal gap
            current = start + datetime.timedelta(
                seconds=max(UPDATE_PAYLOAD_SEC, MIN_CLUSTER_GAP_SEC))

    # left-shift small gaps (simple repair)
    for i in range(1, len(scheduled)):
        prev_end = datetime.datetime.fromisoformat(
            scheduled[i-1]["updateTimeUtc"].replace("Z", "+00:00")
        ) + datetime.timedelta(seconds=UPDATE_PAYLOAD_SEC)
        ideal = max(prev_end, jobs[i]["windowStart"])
        if ideal + datetime.timedelta(seconds=UPDATE_PAYLOAD_SEC) <= jobs[i]["windowEnd"]:
            scheduled[i]["updateTimeUtc"] = ideal.isoformat().replace("+00:00", "Z")

    return jsonify({"clusters": scheduled})


# ===========================================================================
if __name__ == "__main__":
    app.run(host="0.0.0.0", port=82, debug=False)
