function onTick(view) {
  var ownCore = view.factionId;
  var enemyCore = view.factionId === 1 ? 2 : 1;
  var soldiers = 0;
  for (var i = 0; i < view.ownArmies.length; i++) soldiers += view.ownArmies[i].count;
  var pushing = soldiers > view.params.attackThreshold;
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
      goal: { kind: "Core", id: pushing ? enemyCore : ownCore },
      priority: pushing ? 100 : 80,
      allowedLossPermille: 1000,
      reservePermille: 0
    });
  }
  return { commands: commands };
}
