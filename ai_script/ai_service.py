#!/usr/bin/env python3
"""
Flask micro-service that exposes

  • POST /ai/clustering   – DBSCAN -  PCA(2D) + synthetic points for plotting
  • POST /ai/scheduling   – window aware greedy scheduler
"""

from flask import Flask, request, jsonify
from sklearn.cluster import DBSCAN
from sklearn.decomposition import PCA
from sklearn.preprocessing import StandardScaler
import numpy as np
import datetime
import random

app = Flask(__name__)

DBSCAN_EPS = float(3.5)
DBSCAN_MIN_SAMPLES = int(2)

# total dots to render
TARGET_TOTAL_POINTS = int(100)

# spread of fake dots
SYNTHETIC_SIGMA = float(0.12)

# seconds the FW chunk takes
UPDATE_PAYLOAD_SEC = int(25)
CLUSTER_SETUP_SEC = int(5)
MIN_CLUSTER_GAP_SEC = int(30)


# ---------------------------------------------------------------------------


# ===========================================================================
#  /ai/clustering
# ===========================================================================
@app.route("/ai/clustering", methods=["POST"])
def clustering() -> tuple:
    """
    Returns
      {
        clusters : [ {clusterId:str, deviceUuids:list[str]} ... ]
        plotData : [ {deviceUuid,x,y,clusterId,isSynthetic} ... ]
      }
    """
    data: list[dict] = request.get_json(force=True)
    if not data:
        return jsonify({"error": "empty payload"}), 400

    # real devices - QCS
    real_ids   = [d["deviceUuid"] for d in data]
    X_real     = np.asarray(
        [[d["busyFraction"], d["avgCpu"], d["avgMem"], d["avgNet"]] for d in data],
        dtype=float,
    )
    X_scaled   = StandardScaler().fit_transform(X_real)

    # A dynamic to set the episelum which chooses the number of clusters
    TARGET_CLUSTERS = 6
    EPS_RANGE       = np.arange(0.4, 2.05, 0.1)

    best_eps  = EPS_RANGE[0]
    best_diff = 1e9
    best_lbl  = None

    for eps in EPS_RANGE:
        lbl = DBSCAN(eps=eps, min_samples=1).fit_predict(X_scaled)
        n_clusters = len({l for l in lbl if l != -1})
        diff = abs(n_clusters - TARGET_CLUSTERS)
        if diff < best_diff:
            best_eps, best_diff, best_lbl = eps, diff, lbl
        if diff == 0:
            break

    labels = best_lbl

    # PCA for plotting
    coords_2d = PCA(n_components=2).fit_transform(X_scaled)

    plot_data: list[dict] = []
    real_cluster_map: dict[str, list[str]] = {}

    for i, lbl in enumerate(labels):
        cid = f"{lbl}"
        real_cluster_map.setdefault(cid, []).append(real_ids[i])
        plot_data.append(
            dict(
                deviceUuid=real_ids[i],
                x=float(coords_2d[i, 0]),
                y=float(coords_2d[i, 1]),
                clusterId=cid,
                isSynthetic=False,
            )
        )

    # synthetic vectors in *4‑D* (The Fake QCS that we want to visualize on the plot)
    n_real   = len(real_ids)
    n_fake   = max(0, TARGET_TOTAL_POINTS - n_real)
    if n_fake:
        rng = np.random.default_rng()
        # 4‑D centroid of each *real* cluster (noise −1 is ignored)
        real_centroids = {}
        for cid, devs in real_cluster_map.items():
            if cid == "-1":
                continue
            idx = [real_ids.index(d) for d in devs]
            real_centroids[cid] = X_scaled[idx].mean(axis=0)

        # helper to convert 4‑D vector into PCA 2‑D
        pca_matrix = PCA(n_components=2).fit(X_scaled).components_.T

        total_real = sum(len(v) for v in real_cluster_map.values() if v)
        fake_id    = 0
        syn_cluster_idx = 1

        for cid, centre_4d in real_centroids.items():
            share = round(n_fake * len(real_cluster_map[cid]) / total_real)
            if share == 0:
                continue
            # generate share gaussian points in 4‑D, project to 2‑D
            noise_4d = rng.normal(scale=SYNTHETIC_SIGMA, size=(share, 4))
            pts_4d   = centre_4d + noise_4d
            pts_2d   = pts_4d @ pca_matrix

            for v4, (x2, y2) in zip(pts_4d, pts_2d):
                plot_data.append(
                    dict(
                        deviceUuid=f"synthetic_{fake_id:04d}",
                        x=float(x2),
                        y=float(y2),
                        clusterId=cid,
                        isSynthetic=True,
                    )
                )
                fake_id += 1
            syn_cluster_idx += 1

    # cluster list for the API
    clusters = [
        {"clusterId": cid, "deviceUuids": devs}
        for cid, devs in real_cluster_map.items()
    ] + [
        {"clusterId": f"S{k}", "deviceUuids": []}      # synthetic clusters are empty here so they won't be scheudled
        for k in range(1, syn_cluster_idx)
    ]

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

    # ---- build feasible windows per cluster
    jobs = []
    for c in clusters_input:
        devs = c["deviceUuids"]
        if not devs:
            continue
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

    # ---- schedule greedily then adjust
    jobs.sort(key=lambda j: j["windowStart"])
    scheduled = []
    current   = now

    for j in jobs:
        start = max(j["windowStart"], current, now + datetime.timedelta(seconds=CLUSTER_SETUP_SEC))
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
