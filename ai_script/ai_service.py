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
import random

app = Flask(__name__)

EPS = 0.35
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
    data = request.get_json()
    if "clusters" not in data:
        return jsonify({"error":"No clusters found"}), 400
    
    clusters = data["clusters"]
    aggregatorData = data.get("aggregatorData",[])

    # aggregatorData => array of objects:
    # { "deviceUuid":"ABC","nextIdleTime":"2025-02-20T10:00:00Z","idleDurationSecs":40}, ...
    idle_map = {}
    for a in aggregatorData:
        devUuid = a["deviceUuid"]
        nxt = a["nextIdleTime"]
        idSec= a["idleDurationSecs"]
        idle_map[devUuid] = {
            "nextIdleTime": nxt,
            "idleDurationSecs": idSec
        }

    now_utc = datetime.datetime.now(datetime.UTC)
    scheduled = []

    # Heuristic
    base_offset = 0
    for c in clusters:
        cId = c["clusterId"]
        devs = c["deviceUuids"]
        # find minimal idle among them
        earliest_dt=None
        minIdleSec=999999
        anyMissing=False

        for d in devs:
            if d not in idle_map:
                anyMissing=True
                break
            if idle_map[d]["nextIdleTime"] is None:
                anyMissing=True
                break

        if anyMissing:
            # aggregator says "no idle window" => skip scheduling
            continue

        # all present => pick earliest nextIdleTime
        for d in devs:
            dtstr= idle_map[d]["nextIdleTime"]
            dtp= datetime.datetime.fromisoformat(dtstr.replace("Z",""))
            idsec= idle_map[d]["idleDurationSecs"]
            if dtp<earliest_dt or earliest_dt is None:
                earliest_dt=dtp
            if idsec<minIdleSec:
                minIdleSec=idsec

        # if minIdleSec<20 => skip
        if minIdleSec<20 or earliest_dt is None:
            continue

        # let's schedule it at earliest_dt + base_offset
        # this is naive => we will use LNS to refine it
        st= earliest_dt + datetime.timedelta(minutes=base_offset)
        # if st<now => shift it out
        if st<now_utc:
            st= now_utc + datetime.timedelta(seconds=10)

        scheduled.append({
            "clusterId": cId,
            "deviceUuids": devs,
            "updateTimeUtc": st.isoformat()+"Z"
        })

        base_offset+=2 # naive stepping

    # 2) LNS => reduce concurrency
    def conflict_metric(sched):
        times=[]
        for sc in sched:
            dtp= datetime.datetime.fromisoformat(sc["updateTimeUtc"].replace("Z",""))
            times.append(dtp)
        times.sort()
        c=0
        for i in range(len(times)-1):
            diff= (times[i+1]-times[i]).total_seconds()
            if diff<30:
                c+=(30-diff)
        return c

    best= scheduled[:]
    best_conf= conflict_metric(best)

    for _ in range(10):
        if len(best)<2:
            break
        destroyCount= len(best)//2
        destroyed= random.sample(range(len(best)), destroyCount)
        candidate=[]
        for i,sc in enumerate(best):
            if i not in destroyed:
                candidate.append(sc)
            else:
                dtp= datetime.datetime.fromisoformat(sc["updateTimeUtc"].replace("Z",""))
                shift= random.randint(-15,15)
                new_dt= dtp+ datetime.timedelta(seconds=shift*10) # shift in multiples of 10s
                new_sc= dict(sc)
                new_sc["updateTimeUtc"]= new_dt.isoformat()+"Z"
                candidate.append(new_sc)
        cconf= conflict_metric(candidate)
        if cconf<best_conf:
            best_conf=cconf
            best= candidate

    return jsonify({
        "clusters": best
    })

if __name__=="__main__":
    app.run(host="0.0.0.0",port=82, debug=False)