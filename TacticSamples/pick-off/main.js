// 小さい接触を選ぶ基準と、攻撃・後退の命令優先度。
const SIZE_MARGIN = 2;
const PICK_PRIORITY = 100;
const RETREAT_PRIORITY = 95;
var phase = "retreat";
var phaseUntil = 0;
var targetId = null;
var targetPosition = null;
var lastDecision = "";

function soldiers(view) {
  var n = 0;
  for (var i = 0; i < view.ownArmies.length; i++) n += view.ownArmies[i].count;
  return n;
}

function compositionTotal(composition) {
  var n = 0;
  for (var kindName in composition) n += composition[kindName];
  return n;
}

function visibleKindCount(view, contact, kindName) {
  var n = 0;
  for (var i = 0; i < view.visibleEnemies.length; i++) {
    var enemy = view.visibleEnemies[i];
    var covered = contact.covered || [];
    var belongs = enemy.id === contact.id || covered.indexOf(enemy.id) >= 0;
    if (belongs && enemy.kindName === kindName) n++;
  }
  return n;
}

function bestSmallContact(view, ownCount) {
  var best = null;
  for (var i = 0; i < view.contacts.length; i++) {
    var c = view.contacts[i];
    if (!c.visible || c.strengthUnknown || c.absent) continue;
    var composition = c.visibleComposition || {};
    var visibleCount = compositionTotal(composition);
    var archerCount = visibleKindCount(view, c, "Archer");
    if (visibleCount <= 0 || visibleCount >= ownCount - SIZE_MARGIN) continue;
    if (archerCount * 3 > visibleCount) continue;
    if (best === null || visibleCount < best.visibleCount ||
        (visibleCount === best.visibleCount && c.id < best.id)) {
      best = { id: c.id, position: c.position, visibleCount: visibleCount };
    }
  }
  return best;
}

function choosePhase(view, target, now) {
  if (phase === "attack" && now < phaseUntil) return;
  if (phase === "retreat" && now < phaseUntil) return;

  if (target !== null) {
    phase = "attack";
    phaseUntil = now + view.params.commitTicks;
    targetId = target.id;
    targetPosition = target.position;
  } else {
    phase = "retreat";
    phaseUntil = now + view.params.retreatTicks;
    targetId = null;
    targetPosition = null;
  }
}

function onTick(view) {
  var ownCount = soldiers(view);
  var target = bestSmallContact(view, ownCount);
  var now = view.tick;
  choosePhase(view, target, now);
  var attacking = phase === "attack";
  var decision = attacking ? "pick:" + targetId : "retreat";
  if (decision !== lastDecision) {
    console.log(attacking ? "小さい接触へ集中: " + targetId : "攻撃を止めてコアへ後退");
    lastDecision = decision;
  }
  var commands = [{ type: "global", policy: attacking ? "concentrate" : "maintain" }];
  for (var i = 0; i < view.ownArmies.length; i++) {
    var army = view.ownArmies[i];
    if (army.count === 0) continue; // an empty army only collects EmptyArmy rejections (#343)
    commands.push(attacking ? {
      type: "policy", kind: "Focus", target: { kind: "Army", id: army.id }, goal: { kind: "Point", point: targetPosition }, priority: PICK_PRIORITY, allowedLossPermille: 500, reservePermille: 0
    } : {
      type: "policy", kind: "Retreat", target: { kind: "Army", id: army.id }, goal: { kind: "Core", id: view.factionId }, priority: RETREAT_PRIORITY, allowedLossPermille: 150, reservePermille: 0
    });
  }
  return { commands: commands };
}
