import json
import urllib.request

UA = {"User-Agent": "Mozilla/5.0 DesynthLens"}


def get(url):
    request = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


probes = {
    "Green Viola Corsage": 16619,
    "Twitchbeard": None,
    "Shadeshifter": None,
    "Rinascita Poleyns of Aiming": 37780,
}

items = json.load(
    urllib.request.urlopen(
        "https://raw.githubusercontent.com/ffxiv-teamcraft/ffxiv-teamcraft/staging/libs/data/src/lib/json/items.json",
        timeout=60,
    ).read()
)


def find_id(name):
    for key, value in items.items():
        if value.get("en") == name:
            return int(key)
    return None


for name, known in probes.items():
    item_id = known or find_id(name)
    if item_id is None:
        print(name, "-> id not found")
        continue

    try:
        data = get(f"https://www.garlandtools.org/db/doc/item/en/3/{item_id}.json")
    except Exception as exc:
        print(name, item_id, "garland failed:", exc)
        continue

    item = data.get("item", {})
    desynth = item.get("desynth")
    partial = item.get("partials", {}).get("desynth")

    print(f"{name} ({item_id}) desynth={json.dumps(desynth)[:300]} partials={json.dumps(partial)[:120]}")
