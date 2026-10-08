// 人の合図を戦況の切り替えに使う相棒。人が動かす部隊は常に尊重する。
var mode = "guard";
var rallyPoint = null;
var lastMode = "";

function humanFocus(view) {
  for (var i = 0; i < (view.orders || []).length; i++) {
    var order = view.orders[i];
    if (order.source === "Human" && order.kind === "Focus") return order.goal;
  }
  return null;
}

function applySignals(view) {
  for (var i = 0; i < (view.signals || []).length; i++) {
    var signal = view.signals[i];
    if (signal.name === "rally" && signal.point) { mode = "rally"; rallyPoint = signal.point; }
    else if (signal.name === "push") { mode = "push"; rallyPoint = null; }
    else if (signal.name === "fallback") { mode = "fallback"; rallyPoint = null; }
  }
}

function onTick(view) {
  applySignals(view);
  var humanGoal = humanFocus(view);
  var visibleMode = humanGoal && mode === "push" ? "push-with-human" : mode;
  if (visibleMode !== lastMode) {
    console.log(visibleMode === "guard" ? "合図待ちで守備と温存" :
      visibleMode === "rally" ? "集結地点へ移動" :
      visibleMode === "fallback" ? "撤退してコアを守る" : "合図を受けて押し込む");
    lastMode = visibleMode;
  }

  var commands = [{ type: "global", policy: mode === "push" ? "concentrate" : "maintain" }];
  var enemyCore = view.factionId === 1 ? 2 : 1;
  for (var j = 0; j < view.ownArmies.length; j++) {
    var army = view.ownArmies[j];
    if (army.count === 0) continue; // an empty army only collects EmptyArmy rejections (#343)
    if (army.controlledBy === "Human") continue;
    var kind = "Defend";
    var goal = { kind: "Core", id: view.factionId };
    var priority = view.params.guardPriority;
    // reservePermille は MaintainReserve の命令でしか使えないので、温存率は「損害をどれだけ避けるか」に置き換える。
    var loss = 1000 - view.params.reservePermille;
    if (mode === "rally" && rallyPoint) {
      kind = "Focus"; goal = { kind: "Point", id: 0, point: rallyPoint }; priority = view.params.signalPriority; loss = 1000;
    } else if (mode === "push") {
      kind = "Focus"; goal = humanGoal || { kind: "Core", id: enemyCore }; priority = view.params.signalPriority; loss = 1000;
    } else if (mode === "fallback") {
      loss = 0;
    }
    commands.push({ type: "policy", kind: kind, target: { kind: "Army", id: army.id }, goal: goal,
      priority: priority, allowedLossPermille: loss, reservePermille: 0 });
  }
  return { version: 1, commands: commands };
}
