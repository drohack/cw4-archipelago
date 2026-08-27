# Minimal headless Archipelago client used only by tools/msgfilter.sh to
# produce real other-player events (a Join notice, and optional LocationChecks)
# that the DrohaCW4 game client should classify as relevant=0.
import asyncio, json, sys, uuid
import websockets

HOST = "ws://localhost:38281"
NAME = "Player2CW4"
GAME = "Creeper World 4"
N_CHECKS = int(sys.argv[1]) if len(sys.argv) > 1 else 0

async def main():
    async with websockets.connect(HOST, max_size=None) as ws:
        await ws.recv()  # RoomInfo
        await ws.send(json.dumps([{
            "cmd": "Connect", "game": GAME, "name": NAME, "uuid": str(uuid.uuid4()),
            "version": {"major": 0, "minor": 5, "build": 0, "class": "Version"},
            "items_handling": 7, "tags": [], "slot_data": False, "password": None,
        }]))
        missing = []
        for _ in range(30):
            for m in json.loads(await ws.recv()):
                if m["cmd"] == "Connected":
                    missing = m.get("missing_locations", [])
                elif m["cmd"] == "ConnectionRefused":
                    print("REFUSED", m.get("errors")); return
            if missing is not None:
                break
        print("connected; missing:", len(missing))
        if N_CHECKS and missing:
            locs = missing[:N_CHECKS]
            await ws.send(json.dumps([{"cmd": "LocationChecks", "locations": locs}]))
            print("checked:", locs)
        await asyncio.sleep(3)
    print("player2 done")

asyncio.run(main())
