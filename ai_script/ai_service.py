#!/usr/bin/env python3
"""
Python microservice that:
  1) /ai/clustering - performs DBSCAN on (BusyFraction, AvgCpu, AvgMem, AvgNet)
      and returns clusters plus 2D coords for plotting
  2) /ai/scheduling - given cluster assignments, does a scheduling approach
"""

from flask import Flask, request, jsonify
from sklearn.cluster import DBSCAN
from sklearn.decomposition import PCA
import numpy as np
import datetime

app = Flask(__name__)

# For demonstration, we store DBSCAN EPS + MIN_SAMPLES
EPS = 0.4
MIN_SAMPLES = 2

@app.route("/ai/clustering", methods=["POST"])
def clustering():
    """
    Input: JSON array of { "deviceUuid": "...", "busyFraction": 0.7, "avgCpu": ..., "avgMem": ..., "avgNet": ... }
    We'll create a Nx4 matrix, run DBSCAN, do PCA(2D), 
    return { "clusters": [ { "clusterId":"0", "deviceUuids":[] }, ...],
             "plotData": [ { "deviceUuid":"...", "x":..., "y":..., "clusterId":"..." }, ... ] }
    """
    data = request.get_json()
    if not data or not isinstance(data, list):
        return jsonify({"error":"Invalid input"}), 400

    deviceUuids = []
    vectors = []
    for item in data:
        deviceUuids.append(item["deviceUuid"])
        vec = [
            item["busyFraction"], 
            item["avgCpu"], 
            item["avgMem"], 
            item["avgNet"]
        ]
        vectors.append(vec)

    arr = np.array(vectors)
    if arr.shape[0] < 2:
        # trivial, can't cluster
        clusters_obj = [{"clusterId":"0", "deviceUuids":deviceUuids}]
        return jsonify({
            "clusters": clusters_obj,
            "plotData": []
        })

    # DBSCAN
    db = DBSCAN(eps=EPS, min_samples=MIN_SAMPLES)
    labels = db.fit_predict(arr)

    # Gather clusters
    cluster_map = {}
    for i, label in enumerate(labels):
        lbl = str(label)
        cluster_map.setdefault(lbl, []).append(deviceUuids[i])

    # do a PCA(2) for plotting
    pca = PCA(n_components=2)
    coords_2d = pca.fit_transform(arr)

    plot_data = []
    for i, label in enumerate(labels):
        plot_data.append({
            "deviceUuid": deviceUuids[i],
            "x": float(coords_2d[i,0]),
            "y": float(coords_2d[i,1]),
            "clusterId": str(label)
        })

    # build result
    cluster_list = []
    for label, devs in cluster_map.items():
        cluster_list.append({
            "clusterId": label,
            "deviceUuids": devs
        })

    return jsonify({
        "clusters": cluster_list,
        "plotData": plot_data
    })


@app.route("/ai/scheduling", methods=["POST"])
def scheduling():
    """
    Input: AiClusteringResult:
      {
        "clusters": [
          { "clusterId":"0", "deviceUuids":["ABC","DEF"] },
          { "clusterId":"1", "deviceUuids":["XYZ"] }
        ],
        "plotData": [...]
      }
      
      1) Heuristic: For each cluster, pick a base UpdateTime that tries to avoid known busy fractions.
      2) LNS: randomly shift half the clusters' times by +/- a few minutes, 
         if it reduces overall "conflict" with busy usage.

    Return:
      {
        "clusters": [
          { "clusterId":"0", "deviceUuids":["ABC","DEF"], "updateTimeUtc":"2025-02-20T02:00:00Z" },
          ...
        ]
      }
    """
    data = request.get_json()
    if not data or "clusters" not in data:
        return jsonify({"error":"invalid input"}), 400

    clusters = data["clusters"]
    
    usage_map = {}  # deviceUuid -> busy fraction
    # Let's assume we appended usage in "usageVectors"
    if "usageVectors" in data:
        for uv in data["usageVectors"]:
            usage_map[uv["deviceUuid"]] = uv

    import datetime
    now_utc = datetime.datetime.utcnow()

    # 1) Basic Heuristic: 
    # For each cluster, we find an estimated UpdateTime that doesn't overlap with busy usage.
    # We'll just guess "now + offset" based on average busy fraction. 
    # e.g. if average busy fraction is high => we delay more
    scheduled_clusters = []
    base_offset_minutes = 0
    for idx, c in enumerate(clusters):
        cluster_id = c["clusterId"]
        devs = c["deviceUuids"]
        # compute average busy fraction
        sum_busy = 0.0
        dcount = 0
        for d in devs:
            if d in usage_map:
                sum_busy += usage_map[d]["busyFraction"]
                dcount += 1
        avg_busy = (sum_busy / dcount) if dcount>0 else 0.5  # fallback
        # if avg_busy is 0.8 => they're quite busy, let's offset more 
        # e.g. offset = 10 + (avg_busy*20)
        offset_this_cluster = 10 + (avg_busy * 20)
        update_time = now_utc + datetime.timedelta(minutes=base_offset_minutes + offset_this_cluster)

        scheduled_clusters.append({
            "clusterId": cluster_id,
            "deviceUuids": devs,
            "updateTimeUtc": update_time.isoformat() + "Z"
        })

        # increment base_offset_minutes for next cluster 
        base_offset_minutes += 5  # naive stepping

    # 2) LNS step:
    # We'll do a small "destroy and repair" 
    # to try to reduce collisions if clusters have similar times 
    # (real LNS might be more complex)
    import random

    def conflict_metric(sched):
        """
        Simple conflict metric:
         sum of overlap if times are within 5 minutes of each other 
         ignoring the device usage detail for brevity
        """
        stimes = []
        for sc in sched:
            dt_parsed = datetime.datetime.fromisoformat(sc["updateTimeUtc"].replace("Z",""))
            stimes.append(dt_parsed)
        stimes.sort()
        conflicts = 0
        for i in range(len(stimes)-1):
            diff = (stimes[i+1] - stimes[i]).total_seconds()/60.0
            if diff < 5:
                conflicts += (5 - diff)
        return conflicts

    original_conflict = conflict_metric(scheduled_clusters)
    best_sched = scheduled_clusters[:]
    best_conflict = original_conflict

    for iteration in range(10):  # 10 tries
        # randomly pick half of them to "destroy" 
        destroyed_indices = random.sample(range(len(best_sched)), len(best_sched)//2)
        # for each destroyed, random shift by +/- 10-20 minutes
        candidate = []
        for i, sc in enumerate(best_sched):
            if i not in destroyed_indices:
                candidate.append(sc)
            else:
                # shift
                dt_parsed = datetime.datetime.fromisoformat(sc["updateTimeUtc"].replace("Z",""))
                shift = random.randint(-20, 20)
                new_time = dt_parsed + datetime.timedelta(minutes=shift)
                new_sc = dict(sc)
                new_sc["updateTimeUtc"] = new_time.isoformat() + "Z"
                candidate.append(new_sc)

        cconf = conflict_metric(candidate)
        if cconf < best_conflict:
            best_conflict = cconf
            best_sched = candidate

    # now best_sched is our final
    return jsonify({ "clusters": best_sched })


if __name__ == "__main__":
    # run on port 82, accessible by "http://127.0.0.1:82"
    app.run(host="0.0.0.0", port=82, debug=False)
