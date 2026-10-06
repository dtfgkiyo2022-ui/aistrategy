// 小さい敵と見なす上限、攻撃時の損害許容を調整する定数。
const SIZE_MARGIN = 2;
const PICK_PRIORITY = 100;
const RETREAT_PRIORITY = 95;
var lastDecision = "";

function soldiers(view) {
  var n = 0;
  for (var i = 0; i < view.ownArmies.length; i++) n += view.ownArmies[i].count;
  return n;
}

function bestSmallContact(view, ownCount) {
  var best = null;
  for (var i = 0; i < view.contacts.length; i++) {
    var c = view.contacts[i];
    if (!c.visible || c.strengthUnknown || c.absent || c.max >= ownCount - SIZE_MARGIN) continue;
    if (best === null || c.max < best.max) best = c;
  }
  return best;
}

function onTick(view) {
  var ownCount = soldiers(view);
  var target = bestSmallContact(view, ownCount);
  var decision = target ? "pick:" + target.id : "retreat";
  if (decision !== lastDecision) {
    console.log(target ? "小さい接触へ集中: " + target.id : "有利な接触なし: コアへ後退");
    lastDecision = decision;
  }
  var commands = [{ type: "global", policy: target ? "concentrate" : "maintain" }];
  for (var i = 0; i < view.ownArmies.length; i++) {
    var army = view.ownArmies[i];
    commands.push(target ? {
      type: "policy", kind: "Focus", target: { kind: "Army", id: army.id }, goal: { kind: "Point", point: target.position }, priority: PICK_PRIORITY, allowedLossPermille: 500, reservePermille: 0
    } : {
      type: "policy", kind: "Retreat", target: { kind: "Army", id: army.id }, goal: { kind: "Core", id: view.factionId }, priority: RETREAT_PRIORITY, allowedLossPermille: 150, reservePermille: 0
    });
  }
  return { commands: commands };
}
