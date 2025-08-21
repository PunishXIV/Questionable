Certain areas are visited quite frequently, and especially if you enter/leave buildings it often makes sense to navigate
via a waypoint on the outside (with `"Mount": true` if coming from the inside, and `"Fly": true` if coming from the
outside).

This vastly improves the pathfinding performance, and avoids attempting to fly e.g. under the map or into the building
that can sometimes be found as valid paths.

## Ul'dah

```json
        {
          "Position": {
            "X": -119.1183,
            "Y": 3.7999938,
            "Z": -104.33473
          },
          "TerritoryId": 130,
          "InteractionType": "WalkTo",
          "AetheryteShortcut": "Ul'dah",
          "$": "Ul'dah Aetheryte to Immortal Flames"
        }
```

## Western Thanalan

Vesper Bay side of the gate:

```json
        {
          "Position": {
            "X": -408.92343,
            "Y": 23.167036,
            "Z": -351.16223
          },
          "TerritoryId": 140,
          "InteractionType": "WalkTo",
          "$": "Vesper Bay Gate"
        }
```

Horizon side of the gate:

```json

```

## Mor Dhona

```json
        {
          "Position": {
            "X": 30.917934,
            "Y": 20.495003,
            "Z": -656.1909
          },
          "TerritoryId": 156,
          "InteractionType": "WalkTo",
          "Fly": true,
          "$": "Rising Stones Door"
        }
```

## Coerthas Central Highlands

```json
        {
          "Position": {
            "X": 240.2761,
            "Y": 302.6276,
            "Z": -199.78418
          },
          "TerritoryId": 155,
          "InteractionType": "WalkTo",
          "$": "Camp Dragonhead, door to Haurchefant",
          "Fly": true,
          "AetheryteShortcut": "Coerthas Central Highlands - Camp Dragonhead"
        }
```

## Coerthas Western Highlands

```json
        {
          "Position": {
            "X": 454.75964,
            "Y": 164.27075,
            "Z": -535.00354
          },
          "TerritoryId": 397,
          "InteractionType": "WalkTo",
          "$": "Gorgagne Mills (outside)",
          "Mount": true
        },
        {
          "Position": {
            "X": 454.9128,
            "Y": 164.30827,
            "Z": -542.1735
          },
          "TerritoryId": 397,
          "InteractionType": "WalkTo",
          "$": "Gorgagne Mills (inside)",
          "DisableNavmesh": true
        },
```
