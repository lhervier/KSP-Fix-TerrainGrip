"""Plays the rocket on its legs on a slope of grass (Kerbin) or of ice (Vall), and says whether it slides.

It drives KSP through KSP-Util-MCPServer, a mod that answers HTTP requests on 127.0.0.1, and needs nothing but
Python 3: no AI, no package to install. Copy TerrainGripLegsSlope.sfs (next to this script) into the folder of a
sandbox game, start KSP with KSP-Util-MCPServer installed (and this fix, or not, to compare), wait for the main
menu, then run, for instance:

    python run-legs-slope.py --folder <your sandbox game>
    python run-legs-slope.py --folder <your sandbox game> --save TerrainGripLegsIce

TerrainGripLegsSlope holds a rocket of 12 t standing on four LT-2 landing legs, brakes on, on a grassy slope of
Kerbin, and Valentina on EVA 15 m away, the active vessel. TerrainGripLegsIce holds the same rocket on a slope of Vall,
which is ice, Valentina 55 m away. Only the feet of the legs touch the ground: their springs are set a little stiffer
than the game's automatic setting, so that the engines at the bottom of the rocket stay clear of it.

For each trial it loads the save, makes the rocket the active vessel, as the [ and ] keys do, then reads every 10 s,
for --seconds seconds, where it stands and its speed over the ground: how far it slid from where it was loaded.
It prints one line per trial, and never saves the game.
"""
import argparse
import json
import math
import sys
import time
import urllib.request

URL = None
SAVE = "TerrainGripLegsSlope"
SAVE_ICE = "TerrainGripLegsIce"
ROCKET = "TerrainGrip Rocket"


def call(name, **args):
    """Calls one tool of KSP-Util-MCPServer and returns its answer, decoded from JSON when it is JSON."""
    body = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "tools/call",
                       "params": {"name": name, "arguments": args}}).encode()
    request = urllib.request.Request(URL, body, {"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=900) as response:
        result = json.loads(response.read())["result"]
    text = result["content"][0].get("text", "") if result["content"] else ""
    if result.get("isError"):
        raise RuntimeError(name + ": " + text)
    try:
        return json.loads(text)
    except ValueError:
        return text


def ground_distance(radius, a, b):
    """Metres over the ground between two states of get_state, on a body of the given radius."""
    lat1, lat2 = math.radians(a["latitude"]), math.radians(b["latitude"])
    dlat = lat2 - lat1
    dlon = math.radians(b["longitude"] - a["longitude"])
    h = math.sin(dlat / 2) ** 2 + math.cos(lat1) * math.cos(lat2) * math.sin(dlon / 2) ** 2
    return 2 * radius * math.asin(math.sqrt(h))


def trial(n, options):
    """Loads the save, follows the rocket, and returns what it read."""
    call("load_save", folder=options.folder, save=options.save)
    call("switch_vessel", vessel=ROCKET)
    start = call("get_state")["vessel"]
    terrain = call("get_terrain", latitude=start["latitude"], longitude=start["longitude"])
    radius = terrain["radius"] + terrain["height"]
    slid, speeds = [0.0], [start["surfaceSpeed"]]
    for _ in range(int(options.seconds // 10)):
        call("wait", seconds=10)
        state = call("get_state")["vessel"]
        slid.append(ground_distance(radius, start, state))
        speeds.append(state["surfaceSpeed"])
    return dict(trial=n, seconds=options.seconds, slope_deg=round(terrain["slope"], 1),
                slid_m=[round(d, 2) for d in slid], speed_mm_s=[round(1000 * v, 1) for v in speeds],
                verdict="slides" if slid[-1] > 1 else "holds")


def main():
    global URL
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--folder", required=True, help="the sandbox game under saves/ the save was copied into")
    parser.add_argument("--save", default=SAVE, choices=(SAVE, SAVE_ICE),
                        help="the grass of Kerbin (default) or the ice of Vall")
    parser.add_argument("--trials", type=int, default=1, help="how many trials, the save loaded before each")
    parser.add_argument("--seconds", type=float, default=60, help="how long to follow the rocket (default 60)")
    parser.add_argument("--port", type=int, default=8770, help="the port of KSP-Util-MCPServer")
    options = parser.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    URL = "http://127.0.0.1:%d/mcp/" % options.port
    # KSP-Util-MCPServer loads a save from the space centre or in flight only, as a player does: from the main
    # menu, the game is opened at the space centre first.
    if call("get_state")["scene"] == "MAINMENU":
        call("open_game", folder=options.folder)
    for n in range(1, options.trials + 1):
        print(time.strftime("%H:%M:%S"), json.dumps(trial(n, options)), flush=True)


if __name__ == "__main__":
    main()
