// Age3に到達してから攻める。兵力の条件を上げるほど、より堅い亀になる。
const TURTLE_LOSS = 120;
const COUNTER_LOSS = 800;
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

function counterGoal(view, enemyCore) {
  if (ageNumber(view.economy) >= 3) return { kind: "Core", id: enemyCore };
  for (var i = 0; i < (view.visibleEnemies || []).length; i++) if (view.visibleEnemies[i].position) return { kind: "Point", id: 0, point: view.visibleEnemies[i].position };
  return null;
}

function onTick(view) {
  var enemyCore = view.factionId === 1 ? 2 : 1;
  var age = ageNumber(view.economy);
  var goal = counterGoal(view, enemyCore);
  var counter = age >= view.params.attackAge && soldiers(view) >= view.params.attackSoldiers && goal !== null;
  var phase = counter ? "counter-attack" : "turtle";
  if (phase !== lastPhase) {
    console.log("方針切替: " + phase + "（時代=" + age + "）");
    lastPhase = phase;
  }
  var commands = [{ type: "global", policy: counter ? "concentrate" : "maintain" }];
  for (var i = 0; i < view.ownArmies.length; i++) {
    var army = view.ownArmies[i];
    if (army.count === 0) continue; // an empty army only collects EmptyArmy rejections (#343)
    commands.push({ type: "policy", kind: counter ? "Focus" : "Defend", target: { kind: "Army", id: army.id }, goal: counter ? goal : { kind: "Core", id: view.factionId }, priority: 100, allowedLossPermille: counter ? COUNTER_LOSS : TURTLE_LOSS, reservePermille: 0 });
  }
  if (view.economy !== null && view.tick % 100 === 0) {
    commands.push({ type: "economy", kind: "SetEconomyPolicy", sequence: economySequence++, policy: counter ? "Military" : "Growth" });
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
