function onTick(view) {
  var ownCore = view.factionId;
  var enemyCore = view.factionId === 1 ? 2 : 1;
  var soldiers = 0;
  for (var i = 0; i < view.ownArmies.length; i++) soldiers += view.ownArmies[i].count;
  var age = view.economy && (view.economy.age === "Age3" || view.economy.age === 3 ? 3 : view.economy.age === "Age2" || view.economy.age === 2 ? 2 : view.economy.age === "Age1" || view.economy.age === 1 ? 1 : 0);
  var attackGoal = null;
  if (age >= 3) attackGoal = { kind: "Core", id: enemyCore };
  else for (var k = 0; k < (view.visibleEnemies || []).length; k++) if (view.visibleEnemies[k].position) { attackGoal = { kind: "Point", id: 0, point: view.visibleEnemies[k].position }; break; }
  var pushing = soldiers > view.params.attackThreshold && attackGoal !== null;
  var commands = [];
  if ((view.signals || []).some(function (signal) { return signal.name === "buildSteel"; }) && !onTick.steelRequested) {
    var core = (view.objectives || []).filter(function (objective) {
      return objective.kind === "Core" && objective.ownerKnown && objective.ownerFactionId === view.factionId;
    })[0];
    var selected = null;
    (view.regions || []).forEach(function (region) {
      if (region.centerKind === "None") return;
      var point = core ? core.position : { x: 0, z: 0 };
      var dx = region.center.x - point.x;
      var dz = region.center.z - point.z;
      var distance = dx * dx + dz * dz;
      if (!selected || distance < selected.distance || distance === selected.distance && region.id < selected.id) {
        selected = { id: region.id, distance: distance };
      }
    });
    if (selected) {
      commands.push({ type: "economy", kind: "RequestLine", line: "Steel", region: selected.id });
      onTick.steelRequested = true;
    }
  }
  for (var j = 0; j < view.ownArmies.length; j++) {
    var army = view.ownArmies[j];
    if (army.count === 0) continue; // an empty army only collects EmptyArmy rejections (#343)
    commands.push({
      type: "policy",
      kind: pushing ? "Focus" : "Defend",
      target: { kind: "Army", id: army.id },
      goal: pushing ? attackGoal : { kind: "Core", id: ownCore },
      priority: pushing ? 100 : 80,
      allowedLossPermille: 1000,
      reservePermille: 0
    });
  }
  return { commands: commands };
}
