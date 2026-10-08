function onTick(view) {
  var ownCore = view.factionId;
  var enemyCore = view.factionId === 1 ? 2 : 1;
  var soldiers = 0;
  for (var i = 0; i < view.ownArmies.length; i++) soldiers += view.ownArmies[i].count;
  var pushing = soldiers > view.params.attackThreshold;
  var commands = [];
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
