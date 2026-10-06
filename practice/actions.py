"""TacticCommandReader 版1の命令を組み立てる補助関数。"""


def focus(army_id: int, goal_kind: str, goal_id: int, *, priority: int = 100) -> dict:
    return {"type": "policy", "kind": "Focus", "target": {"kind": "Army", "id": army_id}, "goal": {"kind": goal_kind, "id": goal_id}, "priority": priority, "allowedLossPermille": 1000, "reservePermille": 0}


def defend(army_id: int, goal_kind: str = "Core", goal_id: int = 1, *, priority: int = 80) -> dict:
    return {"type": "policy", "kind": "Defend", "target": {"kind": "Army", "id": army_id}, "goal": {"kind": goal_kind, "id": goal_id}, "priority": priority, "allowedLossPermille": 1000, "reservePermille": 0}


def train(unit: str = "Infantry", producer_id: int = 0, *, sequence: int = 1) -> dict:
    command = {"type": "economy", "kind": "Train", "sequence": sequence, "unit": unit}
    if producer_id:
        command["producerId"] = producer_id
    return command


def place_building(building: str, cell: int, *, sequence: int = 1) -> dict:
    return {"type": "economy", "kind": "PlaceBuilding", "sequence": sequence, "building": building, "cell": cell}

