// 大きい歩兵軍団で攻め、小さい偵察軍団は自コアの警戒に残す戦術。
const ATTACK_LOSS_PERMILLE = 1000;

function ageNumber(economy) {
  if (!economy) return 0;
  if (economy.age === "Age3" || economy.age === 3) return 3;
  if (economy.age === "Age2" || economy.age === 2) return 2;
  if (economy.age === "Age1" || economy.age === 1) return 1;
  return 0;
}

function attackGoal(view, enemyCore) {
  if (ageNumber(view.economy) >= 3) return { kind: "Core", id: enemyCore };
  for (var i = 0; i < (view.visibleEnemies || []).length; i++) {
    if (view.visibleEnemies[i].position) return { kind: "Point", id: 0, point: view.visibleEnemies[i].position };
  }
  for (var j = 0; j < (view.objectives || []).length; j++) {
    var objective = view.objectives[j];
    if (objective.kind === "Outpost" && objective.ownerKnown && objective.ownerFactionId !== view.factionId) return { kind: "Outpost", id: objective.id };
  }
  return null;
}

function onTick(view) {
  var enemyCore = view.factionId === 1 ? 2 : 1;
  var commands = [{ type: "global", policy: "concentrate" }];
  var allIn = view.signals.some(function (signal) { return signal.name === "allIn"; });
  var hold = view.signals.filter(function (signal) { return signal.name === "holdHere"; })[0];
  var enemyGoal = attackGoal(view, enemyCore);
  for (var i = 0; i < view.ownArmies.length; i++) {
    var army = view.ownArmies[i];
    if (army.count === 0) continue; // an empty army only collects EmptyArmy rejections (#343)
    var spearhead = enemyGoal !== null && (allIn || army.count >= view.params.spearheadMinimum);
    if (hold && hold.point) {
      commands.push({
        type: "policy", kind: "Focus", target: { kind: "Army", id: army.id },
        goal: { kind: "Point", id: 0, point: hold.point }, priority: 100,
        allowedLossPermille: 1000
      });
      continue;
    }
    commands.push({
      type: "policy",
      kind: spearhead ? "Focus" : "Defend",
      target: { kind: "Army", id: army.id },
      goal: { kind: spearhead ? enemyGoal.kind : "Core", id: spearhead ? enemyGoal.id : view.factionId, point: spearhead ? enemyGoal.point : undefined },
      priority: spearhead ? view.params.attackPriority : view.params.guardPriority,
      allowedLossPermille: spearhead ? ATTACK_LOSS_PERMILLE : view.params.guardLossPermille
    });
  }
  return { commands: commands };
}
