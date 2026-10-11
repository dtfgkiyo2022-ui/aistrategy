function onTick(view) {
  var ownCore = view.factionId;
  var enemyCore = view.factionId === 1 ? 2 : 1;
  var soldiers = 0;
  for (var i = 0; i < view.ownArmies.length; i++) soldiers += view.ownArmies[i].count;
  var age = view.economy && (view.economy.age === "Age3" || view.economy.age === 3 ? 3 : view.economy.age === "Age2" || view.economy.age === 2 ? 2 : view.economy.age === "Age1" || view.economy.age === 1 ? 1 : 0);
  var attackGoal = null;
  if (age >= 3) attackGoal = { kind: "Core", id: enemyCore };
  else for (var k = 0; k < (view.visibleEnemies || []).length; k++) if (view.visibleEnemies[k].position) { attackGoal = { kind: "Point", id: 0, point: view.visibleEnemies[k].position }; break; }
  var attacking = soldiers > view.params.attackThreshold && attackGoal !== null;
  var commands = [];
  for (var j = 0; j < view.ownArmies.length; j++) {
    var army = view.ownArmies[j];
    if (army.count === 0) continue; // an empty army only collects EmptyArmy rejections (#343)
    commands.push({
      type: "policy",
      kind: attacking ? "Focus" : "Defend",
      target: { kind: "Army", id: army.id },
      goal: attacking ? attackGoal : { kind: "Core", id: ownCore },
      priority: attacking ? 100 : 80,
      allowedLossPermille: 1000,
      reservePermille: 0
    });
  }
  return { commands: commands };
}
