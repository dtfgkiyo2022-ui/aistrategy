// Age2まで内政と守備を優先し、弓兵と騎兵を目標割合で混ぜる。
var economySequence = 1;
var lastProduction = "";
var lastPhase = "";

function soldiers(view) {
  var total = 0;
  for (var i = 0; i < view.ownArmies.length; i++) total += view.ownArmies[i].count;
  return total;
}

function ownKindCount(view, kind) {
  var total = 0;
  for (var i = 0; i < view.ownArmies.length; i++) {
    total += (view.ownArmies[i].composition || {})[kind] || 0;
  }
  return total;
}

function ageNumber(economy) {
  if (!economy) return 0;
  if (economy.age === "Age3" || economy.age === 3) return 2;
  if (economy.age === "Age2" || economy.age === 2) return 1;
  return 0;
}

function findBuilding(economy, kind, completeOnly) {
  if (!economy || !economy.buildings) return null;
  for (var i = 0; i < economy.buildings.length; i++) {
    var building = economy.buildings[i];
    if (building.kind === kind && (!completeOnly || building.complete)) return building;
  }
  return null;
}

function hasBuildingOrConstruction(economy, kind) {
  return findBuilding(economy, kind, false) !== null;
}

function desiredUnit(view, age) {
  if (age < view.params.ageTarget) return "Infantry";
  var total = soldiers(view);
  if (total === 0) return "Archer";
  var archers = ownKindCount(view, "Archer");
  return archers * 1000 < total * view.params.archerPermille ? "Archer" : "Cavalry";
}

function buildingFor(unit) {
  if (unit === "Archer") return "ArcheryRange";
  if (unit === "Cavalry") return "Stable";
  return "Barracks";
}

function onTick(view) {
  var economy = view.economy;
  var age = ageNumber(economy);
  var total = soldiers(view);
  var pushing = total >= view.params.attackThreshold && age >= view.params.ageTarget;
  var phase = pushing ? "attack" : "defend";
  var unit = desiredUnit(view, age);
  var decision = phase + ":" + unit;

  if (decision !== lastProduction || phase !== lastPhase) {
    console.log("方針切替: " + (pushing ? "反攻" : "守備") + " / " + unit);
    lastProduction = decision;
    lastPhase = phase;
  }

  var enemyCore = view.factionId === 1 ? 2 : 1;
  var commands = [{ type: "global", policy: pushing ? "concentrate" : "maintain" }];
  for (var i = 0; i < view.ownArmies.length; i++) {
    var army = view.ownArmies[i];
    commands.push({
      type: "policy",
      kind: pushing ? "Focus" : "Defend",
      target: { kind: "Army", id: army.id },
      goal: { kind: "Core", id: pushing ? enemyCore : view.factionId },
      priority: pushing ? 100 : 90,
      allowedLossPermille: pushing ? 700 : 200,
      reservePermille: 0
    });
  }

  if (economy !== null && economy !== undefined && view.tick % 100 === 0) {
    // Age-up and army production need a reserve of food/wood.  Growth keeps
    // training villagers indefinitely, so use the military target while this
    // tactic is building the mixed army.
    commands.push({ type: "economy", kind: "SetEconomyPolicy", sequence: economySequence++, policy: "Military" });

    if (economy.agesEnabled && economy.canAdvanceNow && economy.advancingTo === "Primitive" && age < view.params.ageTarget) {
      commands.push({ type: "economy", kind: "AdvanceAge", sequence: economySequence++, civ: economy.civilisation === "Primitive" ? "Agrarian" : economy.civilisation });
      console.log("時代進行を開始");
    }

    var producerKind = buildingFor(unit);
    var producer = findBuilding(economy, producerKind, true);
    if (producer === null && !hasBuildingOrConstruction(economy, producerKind) && (economy.wood || 0) >= 150 && (unit === "Infantry" || age >= view.params.ageTarget)) {
      commands.push({ type: "economy", kind: "PlaceBuilding", sequence: economySequence++, building: producerKind, cell: 0 });
    } else if (producer !== null && (economy.population || 0) < (economy.populationCap || 0) && (producer.queued === undefined || producer.queued === 0)) {
      var foodCost = unit === "Infantry" ? 50 : 60;
      var woodCost = unit === "Infantry" ? 20 : 30;
      if ((economy.food || 0) >= foodCost && (economy.wood || 0) >= woodCost) {
        commands.push({ type: "economy", kind: "Train", sequence: economySequence++, producerId: producer.id, unit: unit });
      }
    }
  }
  return { version: 1, commands: commands };
}
