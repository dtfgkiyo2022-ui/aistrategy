function onTick(view) {
  var ownCore = view.factionId;
  var enemyCore = view.factionId === 1 ? 2 : 1;
  var soldiers = 0;
  for (var i = 0; i < view.ownArmies.length; i++) soldiers += view.ownArmies[i].count;
  var attacking = soldiers > view.params.attackThreshold;
  var commands = [];
  for (var j = 0; j < view.ownArmies.length; j++) {
    var army = view.ownArmies[j];
    commands.push({
      type: "policy",
      kind: attacking ? "Focus" : "Defend",
      target: { kind: "Army", id: army.id },
      goal: { kind: "Core", id: attacking ? enemyCore : ownCore },
      priority: attacking ? 100 : 80,
      allowedLossPermille: 1000,
      reservePermille: 0
    });
  }
  return { commands: commands };
}
