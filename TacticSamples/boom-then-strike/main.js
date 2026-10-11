// 長く内政するか、いつ全軍攻撃へ移るかを調整する定数。
const GROWTH_PRIORITY = 70;
const STRIKE_PRIORITY = 100;
var lastPhase = "";
var economySequence = 1;

function soldiers(view) {
  var n = 0;
  for (var i = 0; i < view.ownArmies.length; i++) n += view.ownArmies[i].count;
  return n;
}

function ageNumber(economy) {
  if (!economy) return 0;
  if (economy.age === "Age3" || economy.age === 3) return 3;
  if (economy.age === "Age2" || economy.age === 2) return 2;
  if (economy.age === "Age1" || economy.age === 1) return 1;
  return 0;
}

function strikeGoal(view, enemyCore, age) {
  if (age >= 3) return { kind: "Core", id: enemyCore };
  for (var i = 0; i < (view.visibleEnemies || []).length; i++) {
    var enemy = view.visibleEnemies[i];
    if (enemy.position) return { kind: "Point", id: 0, point: enemy.position };
  }
  for (var j = 0; j < (view.objectives || []).length; j++) {
    var objective = view.objectives[j];
    if (objective.kind === "Outpost" && objective.ownerKnown && objective.ownerFactionId !== view.factionId) {
      return { kind: "Outpost", id: objective.id };
    }
  }
  return null;
}

function onTick(view) {
  var enemyCore = view.factionId === 1 ? 2 : 1;
  var age = ageNumber(view.economy);
  var striking = view.tick >= view.params.strikeTick && soldiers(view) >= view.params.strikeSoldiers && age >= 1;
  var strikeGoalValue = striking ? strikeGoal(view, enemyCore, age) : null;
  if (striking && strikeGoalValue === null) striking = false;
  var phase = striking ? "strike" : "boom";
  if (phase !== lastPhase) {
    console.log("方針切替: " + phase + (striking ? "（条件達成）" : "（内政優先）"));
    lastPhase = phase;
  }
  var commands = [{ type: "global", policy: striking ? "concentrate" : "maintain" }];
  for (var i = 0; i < view.ownArmies.length; i++) {
    var army = view.ownArmies[i];
    if (army.count === 0) continue; // an empty army only collects EmptyArmy rejections (#343)
    commands.push({ type: "policy", kind: striking ? "Focus" : "Defend", target: { kind: "Army", id: army.id }, goal: striking ? strikeGoalValue : { kind: "Core", id: view.factionId }, priority: striking ? STRIKE_PRIORITY : GROWTH_PRIORITY, allowedLossPermille: striking ? 1000 : 250, reservePermille: 0 });
  }
  if (view.economy !== null && view.tick % 100 === 0) {
    commands.push({ type: "economy", kind: "SetEconomyPolicy", sequence: economySequence++, policy: striking ? "Military" : "Growth" });
    var secondsLeft = view.economy.nextAgeAutoTick ? (view.economy.nextAgeAutoTick - view.tick) / 20 : 0;
    var cost = view.economy.nextAgeCost || {};
    if (view.economy.agesEnabled && view.economy.canAdvanceNow && secondsLeft > 60 &&
        (view.economy.food || 0) >= (cost.food || 0) + 150 && (view.economy.wood || 0) >= (cost.wood || 0) + 100) {
      commands.push({ type: "economy", kind: "AdvanceAge", sequence: economySequence++, civ: view.economy.civilisation === "Primitive" ? "Agrarian" : view.economy.civilisation });
      console.log("時代進行を開始");
    }
  }
  return { commands: commands };
}
