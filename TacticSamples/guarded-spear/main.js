// 大きい歩兵軍団で攻め、小さい偵察軍団は自コアの警戒に残す戦術。
const ATTACK_LOSS_PERMILLE = 1000;

function onTick(view) {
  var enemyCore = view.factionId === 1 ? 2 : 1;
  var commands = [{ type: "global", policy: "concentrate" }];
  for (var i = 0; i < view.ownArmies.length; i++) {
    var army = view.ownArmies[i];
    var spearhead = army.count >= view.params.spearheadMinimum;
    commands.push({
      type: "policy",
      kind: spearhead ? "Focus" : "Defend",
      target: { kind: "Army", id: army.id },
      goal: { kind: "Core", id: spearhead ? enemyCore : view.factionId },
      priority: spearhead ? view.params.attackPriority : view.params.guardPriority,
      allowedLossPermille: spearhead ? ATTACK_LOSS_PERMILLE : view.params.guardLossPermille
    });
  }
  return { commands: commands };
}
