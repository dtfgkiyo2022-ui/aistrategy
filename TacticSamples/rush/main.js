var economySequence = 1;

function onTick(view) {
  var enemyCore = view.factionId === 1 ? 2 : 1;
  var commands = [];
  for (var i = 0; i < view.ownArmies.length; i++) {
    var army = view.ownArmies[i];
    commands.push({
      type: "policy",
      kind: "Focus",
      target: { kind: "Army", id: army.id },
      goal: { kind: "Core", id: enemyCore },
      priority: 100,
      allowedLossPermille: 1000,
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
