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
  if (economy.age === "Age3" || economy.age === 3) return 3;
  if (economy.age === "Age2" || economy.age === 2) return 2;
  if (economy.age === "Age1" || economy.age === 1) return 1;
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
  // 時代1は文明ごとに弓兵か軽騎兵のどちらか1種類だけ作れる。時代2からは弓兵も重い騎兵も作れる。
  var civ = view.economy && view.economy.civilisation;
  if (age === 1) return EARLY_ARCHER_CIVS.indexOf(civ) >= 0 ? "Archer" : "LightCavalry";
  return civ === "Agrarian" ? "Cavalry" : "Archer";
}

// 時代1に弓兵を作れる文明。ほかの文明は時代1に軽騎兵を作れる。
var EARLY_ARCHER_CIVS = ["Metallurgy", "Forestry", "Bridge", "Cult", "Mountain", "Metropolis"];

function buildingFor(unit) {
  if (unit === "Archer") return "ArcheryRange";
  if (unit === "Cavalry" || unit === "LightCavalry") return "Stable";
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
    if (army.count === 0) continue; // an empty army only collects EmptyArmy rejections (#343)
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
    // 村人だけを増やし続けると兵と時代が遅れるため、軍事寄りのお任せにする。
    commands.push({ type: "economy", kind: "SetEconomyPolicy", sequence: economySequence++, policy: "Military" });

    var secondsLeft = economy.nextAgeAutoTick ? (economy.nextAgeAutoTick - view.tick) / 20 : 0;
    var cost = economy.nextAgeCost || {};
    if (economy.agesEnabled && economy.canAdvanceNow && economy.advancingTo === "Primitive" && age < view.params.ageTarget && secondsLeft > 60 &&
        (economy.food || 0) >= (cost.food || 0) + 150 && (economy.wood || 0) >= (cost.wood || 0) + 100) {
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
