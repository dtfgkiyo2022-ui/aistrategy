// Age3に到達してから攻める。兵力の条件を上げるほど、より堅い亀になる。
const ATTACK_AGE = "Age3";
const ATTACK_SOLDIERS = 36;
const TURTLE_LOSS = 120;
const COUNTER_LOSS = 800;
var lastPhase = "";
var economySequence = 1;

function soldiers(view) {
  var n = 0;
  for (var i = 0; i < view.ownArmies.length; i++) n += view.ownArmies[i].count;
  return n;
}

function onTick(view) {
  var enemyCore = view.factionId === 1 ? 2 : 1;
  var age = view.economy === null ? "Unknown" : view.economy.age;
  var counter = age === ATTACK_AGE && soldiers(view) >= ATTACK_SOLDIERS;
  var phase = counter ? "counter-attack" : "turtle";
  if (phase !== lastPhase) {
    console.log("方針切替: " + phase + "（時代=" + age + "）");
    lastPhase = phase;
  }
  var commands = [{ type: "global", policy: counter ? "concentrate" : "maintain" }];
  for (var i = 0; i < view.ownArmies.length; i++) {
    var army = view.ownArmies[i];
    commands.push({ type: "policy", kind: counter ? "Focus" : "Defend", target: { kind: "Army", id: army.id }, goal: { kind: "Core", id: counter ? enemyCore : view.factionId }, priority: counter ? 100 : 100, allowedLossPermille: counter ? COUNTER_LOSS : TURTLE_LOSS, reservePermille: 0 });
  }
  if (view.economy !== null && view.tick % 100 === 0) {
    commands.push({ type: "economy", kind: "SetEconomyPolicy", sequence: economySequence++, policy: counter ? "Military" : "Growth" });
    if (!counter && age !== ATTACK_AGE) commands.push({ type: "economy", kind: "AdvanceAge", sequence: economySequence++, civ: view.economy.civilisation });
  }
  return { commands: commands };
}
