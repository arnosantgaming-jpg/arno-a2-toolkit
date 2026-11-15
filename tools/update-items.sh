#!/usr/bin/env bash
# Refreshes data/Items.tsv (item names, categories and grades for the item log) from
# aion2data.com's public item list, e.g. after a game patch. See THIRD-PARTY-NOTICES.txt.
set -euo pipefail
cd "$(dirname "$0")/.."
curl -fsSL https://aion2data.com/raw/items.json | python3 -I -c '
import json, sys
d = json.load(sys.stdin)
clean = lambda s: " ".join(str(s).split())
print("# AION 2 items from https://aion2data.com (client " + d["clientVersion"] + "). Game data (c) NCSOFT.")
print("# id\tname\tcategory\tgrade")
for r in sorted(d["records"], key=lambda r: r["id"]):
    print("\t".join([str(r["id"]), clean(r["name"]), clean(r["category"]), clean(r["grade"])]))
' > data/Items.tsv
echo "Wrote $(grep -vc '^#' data/Items.tsv) items to data/Items.tsv"
