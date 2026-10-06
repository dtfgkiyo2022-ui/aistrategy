// 長く内政するか、いつ全軍攻撃へ移るかを調整する定数。
const STRIKE_TICK = 7200;
const STRIKE_SOLDIERS = 42;
const GROWTH_PRIORITY = 70;
const STRIKE_PRIORITY = 100;
var lastPhase = "";
var economySequence = 1;

function soldiers(view) {
  var n = 0;
  for (var i = 0; i < view.ownArmies.length; i++) n += view.ownArmies[i].count;
  return n;
}

function onTick(view) {
  var enemyCore = view.factionId === 1 ? 2 : 1;
  var striking = view.tick >= STRIKE_TICK && soldiers(view) >= STRIKE_SOLDIERS;
  var phase = striking ? "strike" : "boom";
  if (phase !== lastPhase) {
    console.log("方針切替: " + phase + (striking ? "（条件達成）" : "（内政優先）"));
    lastPhase = phase;
  }
  var commands = [{ type: "global", policy: striking ? "concentrate" : "maintain" }];
  for (var i = 0; i < view.ownArmies.length; i++) {
    var army = view.ownArmies[i];
    commands.push({ type: "policy", kind: striking ? "Focus" : "Defend", target: { kind: "Army", id: army.id }, goal: { kind: striking ? "Core" : "Core", id: striking ? enemyCore : view.factionId }, priority: striking ? STRIKE_PRIORITY : GROWTH_PRIORITY, allowedLossPermille: striking ? 1000 : 250, reservePermille: 0 });
  }
  if (view.economy !== null && view.tick % 100 === 0) {
    commands.push({ type: "economy", kind: "SetEconomyPolicy", sequence: economySequence++, policy: striking ? "Military" : "Growth" });
    // 時代進行は資源条件を満たした回だけシミュレーション側で受理される。
    if (!striking && view.economy.age !== "Age3") commands.push({ type: "economy", kind: "AdvanceAge", sequence: economySequence++, civ: view.economy.civilisation });
  }
  return { commands: commands };
}
