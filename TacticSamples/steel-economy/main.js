// 冶金の文明に入った時期と第2時代に入った時期を覚え、ラインを一度ずつ頼む見本。
var metallurgyStartTick = -1;
var secondAgeStartTick = -1;
var requestedCoreMetal = false;
var requestedSteel = false;

function distanceSquared(a, b) {
  var dx = a.x - b.x;
  var dz = a.z - b.z;
  return dx * dx + dz * dz;
}

function ownCorePoint(view) {
  for (var i = 0; i < (view.objectives || []).length; i++) {
    var objective = view.objectives[i];
    if (objective.kind === "Core" && objective.ownerKnown && objective.ownerFactionId === view.factionId) return objective.position;
  }
  return { x: 0, z: 0 };
}

function nearestRegion(regions, point) {
  var best = null;
  for (var i = 0; i < regions.length; i++) {
    var region = regions[i];
    if (region.centerKind === "None") continue;
    var distance = distanceSquared(region.center, point);
    if (!best || distance < best.distance || distance === best.distance && region.id < best.region.id) {
      best = { region: region, distance: distance };
    }
  }
  return best ? best.region : null;
}

function resourceRichRegion(regions, resources, core) {
  var totals = {};
  for (var i = 0; i < regions.length; i++) totals[regions[i].id] = 0;
  for (var r = 0; r < resources.length; r++) {
    var resource = resources[r];
    var closest = nearestRegion(regions, resource.position);
    if (closest) totals[closest.id] += resource.remaining;
  }
  var best = null;
  for (var j = 0; j < regions.length; j++) {
    var region = regions[j];
    if (region.centerKind === "None") continue;
    var score = totals[region.id] || 0;
    var distance = distanceSquared(region.center, core);
    if (!best || score > best.score || score === best.score && (distance < best.distance || distance === best.distance && region.id < best.region.id)) {
      best = { region: region, score: score, distance: distance };
    }
  }
  return best ? best.region : null;
}

function chooseRegion(view) {
  var regions = view.regions || [];
  if (regions.length === 0) return null;
  var core = ownCorePoint(view);
  if (view.params.regionChoice === "resourceRich") return resourceRichRegion(regions, (view.economy && view.economy.resources) || [], core);
  return nearestRegion(regions, core);
}

function requestLine(view, line) {
  var region = chooseRegion(view);
  // The place is a region; a map without regions has nowhere to name, so nothing is asked.
  if (!region) return null;
  return { type: "economy", kind: "RequestLine", line: line, region: region.id };
}

function onTick(view) {
  var economy = view.economy;
  var commands = [{ type: "global", policy: "maintain" }];
  if (!economy) return { version: 1, commands: commands };

  if (economy.civilisation === "Metallurgy") {
    if (metallurgyStartTick < 0) metallurgyStartTick = view.tick;
    if (economy.age >= 2 && secondAgeStartTick < 0) secondAgeStartTick = view.tick;
    var ticksPerSecond = 20;
    if (!requestedCoreMetal && view.tick >= metallurgyStartTick + view.params.metalDelaySeconds * ticksPerSecond) {
      var metal = requestLine(view, "CoreMetal");
      if (metal) { commands.push(metal); requestedCoreMetal = true; }
    }
    if (economy.agesEnabled && secondAgeStartTick >= 0 && !requestedSteel &&
        view.tick >= secondAgeStartTick + view.params.steelDelaySeconds * ticksPerSecond) {
      var steel = requestLine(view, "Steel");
      if (steel) { commands.push(steel); requestedSteel = true; }
    }
  }
  return { version: 1, commands: commands };
}
