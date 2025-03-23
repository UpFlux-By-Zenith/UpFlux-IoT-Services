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
from sklearn.preprocessing import StandardScaler
import numpy as np
import datetime
import random

app = Flask(__name__)

EPS = 3.5
MIN_SAMPLES = 2

@app.route("/ai/clustering", methods=["POST"])
def clustering():
    """
    Input: JSON array of { "deviceUuid": "...", "busyFraction": 0.7, "avgCpu": ..., "avgMem": ..., "avgNet": ... }
    This will create a Nx4 matrix, run DBSCAN, do PCA(2D), 
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
        clusters_obj = [{"clusterId":"0", "deviceUuids":deviceUuids}]
        return jsonify({
            "clusters": clusters_obj,
            "plotData": []
        })

    # Normalize the data to have zero mean and unit variance - this will help position at the origin.
    scaler = StandardScaler()
    arr_normalized = scaler.fit_transform(arr)
    
    # Run DBSCAN clustering on normalized data.
    db = DBSCAN(eps=EPS, min_samples=MIN_SAMPLES)
    labels = db.fit_predict(arr_normalized)

    # Gather clusters
    cluster_map = {}
    for i, label in enumerate(labels):
        lbl = str(label)
        cluster_map.setdefault(lbl, []).append(deviceUuids[i])

    # Perform PCA on normalized data for 2D plotting.
    pca = PCA(n_components=2)
    coords_2d = pca.fit_transform(arr_normalized)

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
    Expects a JSON object that includes:
      - clusters: List of clusters (each with "clusterId" and "deviceUuids")
      - aggregatorData: (optional) List of objects representing predicted idle window for each device:
            { "deviceUuid": "XYZ", "nextIdleTime": "2025-03-25T10:00:00Z", "idleDurationSecs": 45 }
      
    Returns a JSON object with:
      - clusters: List of scheduled clusters with "clusterId", "deviceUuids", and "updateTimeUtc"
    """
    data = request.get_json()
    if "clusters" not in data:
        return jsonify({"error": "No clusters found"}), 400

    clusters = data["clusters"]
    aggregatorData = data.get("aggregatorData", [])
    idle_map = {}
    for a in aggregatorData:
        devUuid = a["deviceUuid"]
        nxt = a["nextIdleTime"]
        idSec = a["idleDurationSecs"]
        idle_map[devUuid] = {"nextIdleTime": nxt, "idleDurationSecs": idSec}

    now_utc = datetime.datetime.now(datetime.timezone.utc)
    scheduled = []

    base_offset = 0
    for c in clusters:
        cId = c["clusterId"]
        devs = c["deviceUuids"]

        earliest_dt = None
        minIdleSec = float('inf')
        missing = False

        for d in devs:
            if d not in idle_map or idle_map[d]["nextIdleTime"] is None:
                missing = True
                break
            dt = datetime.datetime.fromisoformat(idle_map[d]["nextIdleTime"].replace("Z", "+00:00"))
            idleSec = idle_map[d]["idleDurationSecs"]
            if earliest_dt is None or dt < earliest_dt:
                earliest_dt = dt
            if idleSec < minIdleSec:
                minIdleSec = idleSec

        if missing or minIdleSec < 20 or earliest_dt is None:
            continue

        # Schedule the update at earliest idle time plus a base offset (naively)
        scheduled_time = earliest_dt + datetime.timedelta(minutes=base_offset)
        if scheduled_time < now_utc:
            scheduled_time = now_utc + datetime.timedelta(seconds=10)

        scheduled.append({
            "clusterId": cId,
            "deviceUuids": devs,
            "updateTimeUtc": scheduled_time.isoformat().replace("+00:00", "Z")
        })
        base_offset += 2

    # Apply a simple LNS (destroy and repair) to minimize overlap.
    def conflict_metric(sched):
        times = [datetime.datetime.fromisoformat(sc["updateTimeUtc"].replace("Z", "+00:00")) for sc in sched]
        times.sort()
        total_conflict = 0
        for i in range(len(times) - 1):
            diff = (times[i+1] - times[i]).total_seconds()
            if diff < 30:
                total_conflict += (30 - diff)
        return total_conflict

    best_sched = scheduled[:]
    best_conflict = conflict_metric(best_sched)

    for _ in range(10):
        if len(best_sched) < 2:
            break
        indices = random.sample(range(len(best_sched)), len(best_sched)//2)
        candidate = []
        for i, sc in enumerate(best_sched):
            if i not in indices:
                candidate.append(sc)
            else:
                dt = datetime.datetime.fromisoformat(sc["updateTimeUtc"].replace("Z", "+00:00"))
                shift = random.randint(-15, 15)
                new_dt = dt + datetime.timedelta(seconds=shift * 10)
                new_sc = dict(sc)
                new_sc["updateTimeUtc"] = new_dt.isoformat().replace("+00:00", "Z")
                candidate.append(new_sc)
        if conflict_metric(candidate) < best_conflict:
            best_conflict = conflict_metric(candidate)
            best_sched = candidate

    return jsonify({"clusters": best_sched})

if __name__ == "__main__":
    app.run(host="0.0.0.0", port=82, debug=False)