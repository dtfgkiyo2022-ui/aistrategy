// 三すくみ: Archer -> Infantry, Cavalry -> Archer, Infantry -> Cavalry.
// Scout は三すくみの外なので、見えない間は Infantry を標準にする。
var economySequence = 1;
var lastDecision = "";
function totalByKind(byKind) {
  var total = 0;
  for (var kind in byKind) total += byKind[kind] || 0;
  return total;
}

function dominantKind(byKind, total, threshold) {
  var kinds = ["Archer", "Cavalry", "Infantry", "Scout"];
  var best = "";
  var bestCount = 0;
  for (var i = 0; i < kinds.length; i++) {
    var count = byKind[kinds[i]] || 0;
    if (count > bestCount) {
      best = kinds[i];
      bestCount = count;
    }
  }
  return total > 0 && bestCount * 1000 >= total * threshold ? best : "";
}

function counterFor(enemyKind) {
  if (enemyKind === "Archer") return "Cavalry";
  if (enemyKind === "Cavalry") return "Archer";
  if (enemyKind === "Infantry") return "Cavalry";
  return "Infantry";
}

function ageNumber(economy) {
  if (!economy) return 0;
  if (economy.age === "Age3" || economy.age === 3) return 3;
  if (economy.age === "Age2" || economy.age === 2) return 2;
  if (economy.age === "Age1" || economy.age === 1) return 1;
  return 0;
}

function ownKindCount(view, kind) {
  var count = 0;
  for (var i = 0; i < view.ownArmies.length; i++) {
    var composition = view.ownArmies[i].composition || {};
    count += composition[kind] || 0;
  }
  return count;
}

function productionPlan(view) {
  var summary = view.enemySummary || { byKind: {}, visibleCount: 0 };
  var byKind = summary.byKind || {};
  var total = summary.visibleCount || totalByKind(byKind);
  var dominant = dominantKind(byKind, total, view.params.counterRatioPermille);
  var desired = dominant === "" ? "Infantry" : counterFor(dominant);

  // 時代2からは弓兵も騎兵も作れる。時代1は文明ごとに弓兵か軽騎兵のどちらか1種類だけ。
  var age = ageNumber(view.economy);
  var civ = view.economy && view.economy.civilisation;
  var earlyArcher = EARLY_ARCHER_CIVS.indexOf(civ) >= 0;
  if (age < 1) desired = "Infantry";
  else if (age === 1 && desired === "Archer" && !earlyArcher) desired = "Infantry";
  else if (age === 1 && desired === "Cavalry") desired = earlyArcher ? "Infantry" : "LightCavalry";
  // 相性兵が自軍の目標割合に達したら、歩兵を挟んで過度な単一兵種化を避ける。
  var ownCount = 0;
  for (var i = 0; i < view.ownArmies.length; i++) ownCount += view.ownArmies[i].count;
  if (desired !== "Infantry" && ownCount > 0 && ownKindCount(view, desired) * 1000 >= ownCount * view.params.counterSharePermille) {
    desired = "Infantry";
  }
  return { desired: desired, observed: dominant === "" ? "未確認/混成" : dominant };
}

// 時代1に弓兵を作れる文明。ほかの文明は時代1に軽騎兵を作れる。
var EARLY_ARCHER_CIVS = ["Metallurgy", "Forestry", "Bridge", "Cult", "Mountain", "Metropolis"];

function buildingFor(unit) {
  if (unit === "Archer") return "ArcheryRange";
  if (unit === "Cavalry" || unit === "LightCavalry") return "Stable";
  return "Barracks";
}

function findBuilding(economy, kind) {
  if (!economy || !economy.buildings) return null;
  for (var i = 0; i < economy.buildings.length; i++) {
    var building = economy.buildings[i];
    if (building.kind === kind && building.complete) return building;
  }
  return null;
}

function hasBuildingOrConstruction(economy, kind) {
  if (!economy || !economy.buildings) return false;
  for (var i = 0; i < economy.buildings.length; i++) {
    if (economy.buildings[i].kind === kind) return true;
  }
  return false;
}

function soldiers(view) {
  var count = 0;
  for (var i = 0; i < view.ownArmies.length; i++) count += view.ownArmies[i].count;
  return count;
}

function onTick(view) {
  var plan = productionPlan(view);
  var decision = plan.observed + "=>" + plan.desired;
  if (decision !== lastDecision) {
    console.log(plan.observed + "が優勢/未確認 → " + plan.desired + "を作る");
    lastDecision = decision;
  }

  var ownCount = soldiers(view);
  var pushing = ownCount >= view.params.attackThreshold;
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

  var economy = view.economy;
  if (economy !== null && economy !== undefined && view.tick % 100 === 0) {
    var age = ageNumber(economy);
    commands.push({ type: "economy", kind: "SetEconomyPolicy", sequence: economySequence++, policy: "Military" });
    var secondsLeft = economy.nextAgeAutoTick ? (economy.nextAgeAutoTick - view.tick) / 20 : 0;
    var cost = economy.nextAgeCost || {};
    if (economy.agesEnabled && economy.canAdvanceNow && economy.advancingTo === "Primitive" && age < 1 && secondsLeft > 60 &&
        (economy.food || 0) >= (cost.food || 0) + 150 && (economy.wood || 0) >= (cost.wood || 0) + 100) {
      commands.push({ type: "economy", kind: "AdvanceAge", sequence: economySequence++, civ: economy.civilisation === "Primitive" ? "Agrarian" : economy.civilisation });
      console.log("時代進行を開始");
    }
    var producerKind = buildingFor(plan.desired);
    var producer = findBuilding(economy, producerKind);
    if (producer === null && !hasBuildingOrConstruction(economy, producerKind) && (economy.wood || 0) >= 150) {
      commands.push({ type: "economy", kind: "PlaceBuilding", sequence: economySequence++, building: producerKind, cell: 0 });
    } else if (producer !== null && (economy.population || 0) < (economy.populationCap || 0)) {
      var foodCost = plan.desired === "Cavalry" || plan.desired === "Archer" ? 60 : 50;
      var woodCost = plan.desired === "Infantry" ? 20 : 30;
      var queueEmpty = producer.queued === undefined || producer.queued === 0;
      var enough = (economy.food || 0) >= foodCost && (economy.wood || 0) >= woodCost;
      if (queueEmpty && enough) {
        commands.push({ type: "economy", kind: "Train", sequence: economySequence++, producerId: producer.id, unit: plan.desired });
      }
    }
  }
  return { commands: commands };
}
