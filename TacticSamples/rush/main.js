var economySequence = 1;

function ageNumber(economy) {
  if (!economy) return 0;
  if (economy.age === "Age3" || economy.age === 3) return 3;
  if (economy.age === "Age2" || economy.age === 2) return 2;
  if (economy.age === "Age1" || economy.age === 1) return 1;
  return 0;
}

function rushGoal(view, enemyCore) {
  if (ageNumber(view.economy) >= 3) return { kind: "Core", id: enemyCore };
  for (var i = 0; i < (view.visibleEnemies || []).length; i++) if (view.visibleEnemies[i].position) return { kind: "Point", id: 0, point: view.visibleEnemies[i].position };
  for (var j = 0; j < (view.objectives || []).length; j++) {
    var objective = view.objectives[j];
    if (objective.kind === "Outpost" && objective.ownerKnown && objective.ownerFactionId !== view.factionId) return { kind: "Outpost", id: objective.id };
  }
  return null;
}

function onTick(view) {
  var enemyCore = view.factionId === 1 ? 2 : 1;
  var goal = rushGoal(view, enemyCore);
  var commands = [];
  for (var i = 0; i < view.ownArmies.length; i++) {
    var army = view.ownArmies[i];
    if (army.count === 0) continue; // an empty army only collects EmptyArmy rejections (#343)
    if (goal === null) continue; // 時代3までは、見えていないコアへ決め打ちで送らない。
    commands.push({
      type: "policy",
      kind: "Focus",
      target: { kind: "Army", id: army.id },
      goal: goal,
      priority: 100,
      allowedLossPermille: view.params.lossBudgetPermille,
      reservePermille: 0
    });
  }
  if (view.economy !== null) {
    var barracks = 0;
    for (var j = 0; j < view.economy.buildings.length; j++) {
      if (view.economy.buildings[j].kind === "Barracks") {
        barracks = view.economy.buildings[j].id;
        break;
      }
    }
    if (barracks === 0) {
      commands.push({ type: "economy", kind: "PlaceBuilding", sequence: economySequence++, building: "Barracks", cell: 0 });
    } else {
      commands.push({ type: "economy", kind: "Train", sequence: economySequence++, producerId: barracks, unit: "Infantry" });
    }
  }
  return { commands: commands };
}
