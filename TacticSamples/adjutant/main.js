// 人の部隊を空けておき、残りの部隊で守る副官。
var lastMode = "";

function humanIsAttacking(view) {
  for (var i = 0; i < (view.orders || []).length; i++) {
    var order = view.orders[i];
    if (order.source === "Human" && order.kind === "Focus") return true;
  }
  return false;
}

function ownsOutpost(view, id) {
  for (var i = 0; i < view.objectives.length; i++) {
    var objective = view.objectives[i];
    if (objective.kind === "Outpost" && objective.id === id && objective.ownerKnown && objective.ownerFactionId === view.factionId) return true;
  }
  return false;
}

function guardGoal(view, army, humanAttacking) {
  if (humanAttacking && view.params.preferOutpost && army.homeObjective.kind === "Outpost" && ownsOutpost(view, army.homeObjective.id)) {
    return { kind: "Outpost", id: army.homeObjective.id };
  }
  return { kind: "Core", id: view.factionId };
}

function onTick(view) {
  var humanAttacking = humanIsAttacking(view);
  var mode = humanAttacking ? "human-attack" : "quiet-guard";
  if (mode !== lastMode) {
    console.log(humanAttacking ? "人の攻めを支援" : "守備と内政を維持");
    lastMode = mode;
  }

  var commands = [{ type: "global", policy: "maintain" }];
  for (var i = 0; i < view.ownArmies.length; i++) {
    var army = view.ownArmies[i];
    if (army.controlledBy === "Human") continue;
    commands.push({
      type: "policy",
      kind: "Defend",
      target: { kind: "Army", id: army.id },
      goal: guardGoal(view, army, humanAttacking),
      priority: view.params.guardPriority,
      allowedLossPermille: view.params.guardLossPermille,
      reservePermille: 0
    });
  }
  return { version: 1, commands: commands };
}
