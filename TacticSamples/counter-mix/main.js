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

function ageAtLeastTwo(economy) {
  return economy && (economy.age === "Age2" || economy.age === "Age3" || economy.age === 2 || economy.age === 3);
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

  // 専用兵は2つ目の時代から。到達前に出せる標準兵へ戻す。
  if ((desired === "Archer" || desired === "Cavalry") && !ageAtLeastTwo(view.economy)) desired = "Infantry";
  // 相性兵が自軍の目標割合に達したら、歩兵を挟んで過度な単一兵種化を避ける。
  var ownCount = 0;
  for (var i = 0; i < view.ownArmies.length; i++) ownCount += view.ownArmies[i].count;
  if (desired !== "Infantry" && ownCount > 0 && ownKindCount(view, desired) * 1000 >= ownCount * view.params.counterSharePermille) {
    desired = "Infantry";
  }
  return { desired: desired, observed: dominant === "" ? "未確認/混成" : dominant };
}

function buildingFor(unit) {
  if (unit === "Archer") return "ArcheryRange";
  if (unit === "Cavalry") return "Stable";
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
  if (economy !== null && economy !== undefined) {
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
