// 前哨を取って守る時間と、攻めへ切り替える兵力を調整する定数。
const OUTPOST_ATTACK_TICK = 5200;
const ATTACK_SOLDIERS = 28;
const DEFEND_PRIORITY = 95;
const ATTACK_PRIORITY = 85;
var lastPhase = "";
var economySequence = 1;

function ownSoldiers(view) {
  var total = 0;
  for (var i = 0; i < view.ownArmies.length; i++) total += view.ownArmies[i].count;
  return total;
}

function findOwnedOutpost(view) {
  for (var i = 0; i < view.objectives.length; i++) {
    var o = view.objectives[i];
    if (o.kind === "Outpost" && o.ownerKnown && o.ownerFactionId === view.factionId) return o;
  }
  return null;
}

function onTick(view) {
  var enemyCore = view.factionId === 1 ? 2 : 1;
  var outpost = findOwnedOutpost(view);
  var attacking = view.tick >= OUTPOST_ATTACK_TICK && ownSoldiers(view) >= ATTACK_SOLDIERS;
  var phase = attacking ? "attack" : (outpost ? "outpost" : "core");
  if (phase !== lastPhase) {
    console.log("方針切替: " + phase + (outpost ? "（前哨を基準）" : "（守る前哨なし）"));
    lastPhase = phase;
  }
  var commands = [{ type: "global", policy: attacking ? "concentrate" : "maintain" }];
  for (var i = 0; i < view.ownArmies.length; i++) {
    var army = view.ownArmies[i];
    if (army.count === 0) continue; // an empty army only collects EmptyArmy rejections (#343)
    commands.push({
      type: "policy",
      kind: attacking ? "Focus" : "Defend",
      target: { kind: "Army", id: army.id },
      goal: { kind: attacking ? "Core" : (outpost ? "Outpost" : "Core"), id: attacking ? enemyCore : (outpost ? outpost.id : view.factionId) },
      priority: attacking ? ATTACK_PRIORITY : DEFEND_PRIORITY,
      allowedLossPermille: attacking ? 800 : 350,
      reservePermille: 0
    });
  }
  if (view.economy !== null && view.tick % 100 === 0) commands.push({ type: "economy", kind: "SetEconomyPolicy", sequence: economySequence++, policy: "Military" });
  return { commands: commands };
}
