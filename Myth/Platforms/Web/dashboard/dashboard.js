const palette = ["#9fd36f", "#73e0ad", "#e5c66d", "#e98568", "#b89ae8", "#79b7e6", "#d7e093", "#eaa0bd"];
const number = new Intl.NumberFormat("en");
const compact = new Intl.NumberFormat("en", { notation: "compact", maximumFractionDigits: 1 });
const dateLabel = new Intl.DateTimeFormat("en", { month: "short", year: "2-digit" });
const fullDate = new Intl.DateTimeFormat("en", { day: "numeric", month: "short", year: "numeric" });
const tooltip = document.querySelector("#tooltip");

let data;
let world = "All";
let range = "all";

const sumWorlds = snapshot => {
  if (!Object.keys(snapshot.worlds).length) return null;
  const total = { rooms: 0, tileCapacity: 0, solidTiles: 0, actors: 0, actorTypes: {} };
  for (const item of Object.values(snapshot.worlds)) {
    total.rooms += item.rooms;
    total.tileCapacity += item.tileCapacity;
    total.solidTiles += item.solidTiles;
    total.actors += item.actors;
    for (const [type, count] of Object.entries(item.actorTypes)) total.actorTypes[type] = (total.actorTypes[type] || 0) + count;
  }
  total.terrainPressure = total.tileCapacity ? total.solidTiles / total.tileCapacity * 100 : 0;
  const entropy = total.actors ? -Object.values(total.actorTypes).reduce((sum, count) => sum + count / total.actors * Math.log(count / total.actors), 0) : 0;
  total.castDiversity = total.actors ? Math.exp(entropy) : 0;
  total.newNovelCombinations = Object.values(snapshot.worlds).reduce((sum, item) => sum + item.newNovelCombinations, 0);
  total.newReuseCombinations = Object.values(snapshot.worlds).reduce((sum, item) => sum + item.newReuseCombinations, 0);
  total.novelCombinations = Object.values(snapshot.worlds).reduce((sum, item) => sum + item.novelCombinations, 0);
  total.reuseCombinations = Object.values(snapshot.worlds).reduce((sum, item) => sum + item.reuseCombinations, 0);
  total.designDistance = total.rooms ? Object.values(snapshot.worlds).reduce((sum, item) => sum + item.designDistance * item.rooms, 0) / total.rooms : 0;
  total.interactionTriangles = Object.values(snapshot.worlds).reduce((sum, item) => sum + item.interactionTriangles, 0);
  return total;
};

const worldAt = snapshot => world === "All" ? sumWorlds(snapshot) : Object.keys(snapshot.worlds).length ? snapshot.worlds[world] || { rooms: 0, solidTiles: 0, actors: 0, actorTypes: {} } : null;
const snapshots = () => {
  if (range === "all") return data.snapshots;
  const cutoff = new Date(data.snapshots.at(-1).date + "T00:00:00Z");
  cutoff.setUTCDate(cutoff.getUTCDate() - Number(range));
  return data.snapshots.filter(item => new Date(item.date + "T00:00:00Z") >= cutoff);
};
const delta = (value, previous) => previous == null || value === previous ? "" : `<i>${value > previous ? "+" : ""}${number.format(value - previous)}</i>`;

function makePicker() {
  const picker = document.querySelector("#world-picker");
  const worlds = [...new Set(data.snapshots.flatMap(item => Object.keys(item.worlds)))];
  picker.innerHTML = ["All", ...worlds].map(name => `<button data-world="${name}" class="${name === world ? "active" : ""}">${name}</button>`).join("");
  picker.addEventListener("click", event => {
    const button = event.target.closest("button");
    if (!button) return;
    world = button.dataset.world;
    picker.querySelectorAll("button").forEach(item => item.classList.toggle("active", item === button));
    render();
  });
  document.querySelector("#range-picker").addEventListener("click", event => {
    const button = event.target.closest("button");
    if (!button) return;
    range = button.dataset.range;
    button.parentElement.querySelectorAll("button").forEach(item => item.classList.toggle("active", item === button));
    render();
  });
}

function stats(current, previous) {
  const now = worldAt(current);
  const before = previous ? worldAt(previous) || {} : {};
  const values = [
    ["worlds", world === "All" ? Object.keys(current.worlds).length : 1, null],
    ["rooms", now.rooms, before.rooms],
    ["solid tiles", now.solidTiles, before.solidTiles],
    ["placed actors", now.actors, before.actors],
    ["actor types", Object.keys(now.actorTypes).length, before.actorTypes ? Object.keys(before.actorTypes).length : null],
  ];
  document.querySelector("#stats").innerHTML = values.map(([label, value, old]) => `<div class="stat"><span>${label}</span><strong>${number.format(value)}${delta(value, old)}</strong></div>`).join("");
}

function formatAxis(value) { return Math.abs(value) >= 1000 ? compact.format(value) : number.format(Math.round(value)); }

function lineChart(target, series, options = {}) {
  const element = document.querySelector(target);
  const points = snapshots();
  if (!points.length) return;
  const width = 800, height = 250, left = 48, right = 12, top = 12, bottom = 28;
  const allValues = series.flatMap(item => points.map((point, index) => item.value(point, index))).filter(Number.isFinite);
  let min = options.zero ? 0 : Math.min(...allValues);
  let max = Math.max(...allValues);
  const spread = max - min || Math.max(1, max * .1);
  if (!options.zero) min = Math.max(0, min - spread * .12);
  max += spread * .12;
  const x = index => left + (points.length === 1 ? 0 : index / (points.length - 1)) * (width - left - right);
  const y = value => top + (max - value) / (max - min || 1) * (height - top - bottom);
  const ticks = [0, .5, 1].map(t => ({ y: top + t * (height - top - bottom), value: max - t * (max - min) }));
  const grid = ticks.map(t => `<line class="grid-line" x1="${left}" x2="${width - right}" y1="${t.y}" y2="${t.y}"/><text class="axis-label" x="${left - 8}" y="${t.y + 3}" text-anchor="end">${formatAxis(t.value)}</text>`).join("");
  const labels = [0, Math.floor((points.length - 1) / 2), points.length - 1].filter((item, index, array) => array.indexOf(item) === index).map(index => `<text class="axis-label" x="${x(index)}" y="${height - 4}" text-anchor="${index === 0 ? "start" : index === points.length - 1 ? "end" : "middle"}">${dateLabel.format(new Date(points[index].date + "T00:00:00Z"))}</text>`).join("");
  const paths = series.map((item, seriesIndex) => {
    let drawing = false;
    let first = -1, last = -1;
    const path = points.map((point, index) => {
      const value = item.value(point, index);
      if (!Number.isFinite(value)) { drawing = false; return ""; }
      if (first < 0) first = index;
      last = index;
      const command = drawing ? "L" : "M";
      drawing = true;
      return `${command}${x(index).toFixed(1)},${y(value).toFixed(1)}`;
    }).join(" ");
    const area = options.area && seriesIndex === 0 && first >= 0 ? `<path class="series-area" fill="${item.color}" d="${path} L${x(last)},${height - bottom} L${x(first)},${height - bottom} Z"/>` : "";
    return `${area}<path class="series-line" stroke="${item.color}" d="${path}"/>`;
  }).join("");
  const hitWidth = (width - left - right) / Math.max(1, points.length - 1);
  const hits = points.map((point, index) => `<rect class="hit-target" data-index="${index}" x="${Math.max(left, x(index) - hitWidth / 2)}" y="${top}" width="${Math.min(hitWidth, width - right - Math.max(left, x(index) - hitWidth / 2))}" height="${height - top - bottom}"/>`).join("");
  element.innerHTML = `<svg viewBox="0 0 ${width} ${height}" preserveAspectRatio="none" aria-hidden="true">${grid}${paths}${labels}<g class="focus"></g>${hits}</svg>`;
  element.onpointermove = event => {
    const hit = event.target.closest(".hit-target");
    if (!hit) return;
    const index = Number(hit.dataset.index);
    const point = points[index];
    element.querySelector(".focus").innerHTML = `<line class="focus-line" x1="${x(index)}" x2="${x(index)}" y1="${top}" y2="${height - bottom}"/>`;
    const rows = series.map(item => [item, item.value(point, index)]).filter(([, value]) => Number.isFinite(value));
    tooltip.innerHTML = `<time>${fullDate.format(new Date(point.date + "T00:00:00Z"))} · ${point.commit}</time>${rows.map(([item, value]) => `<div><span style="color:${item.color}">${item.name}</span><b>${item.format ? item.format(value) : number.format(value)}</b></div>`).join("")}`;
    tooltip.classList.add("visible");
    const box = tooltip.getBoundingClientRect();
    tooltip.style.left = `${Math.min(innerWidth - box.width - 10, event.clientX + 14)}px`;
    tooltip.style.top = `${Math.max(10, event.clientY - box.height - 12)}px`;
  };
  element.onpointerleave = () => { tooltip.classList.remove("visible"); element.querySelector(".focus").innerHTML = ""; };
}

function render() {
  const points = snapshots();
  const current = points.at(-1);
  const previous = points.at(-2);
  const now = worldAt(current);
  stats(current, previous);
  const charts = [
    ["#rooms-chart", "#rooms-value", "Rooms", item => worldAt(item)?.rooms ?? NaN, now.rooms, palette[0]],
    ["#tiles-chart", "#tiles-value", "Solid tiles", item => worldAt(item)?.solidTiles ?? NaN, now.solidTiles, palette[1]],
    ["#actors-chart", "#actors-value", "Actors", item => worldAt(item)?.actors ?? NaN, now.actors, palette[2]],
    ["#pressure-chart", "#pressure-value", "Terrain pressure", item => worldAt(item)?.terrainPressure ?? NaN, now.terrainPressure, palette[3], value => `${value.toFixed(1)}%`],
    ["#diversity-chart", "#diversity-value", "Cast diversity", item => worldAt(item)?.castDiversity ?? NaN, now.castDiversity, palette[5], value => value.toFixed(1)],
    ["#novel-chart", "#novel-value", "Novel pairings", item => worldAt(item)?.newNovelCombinations ?? NaN, now.newNovelCombinations, palette[0], null, { zero: true }],
    ["#reuse-chart", "#reuse-value", "Reuse leverage", item => worldAt(item)?.newReuseCombinations ?? NaN, now.newReuseCombinations, palette[2], null, { zero: true }],
    ["#distance-chart", "#distance-value", "Design distance", item => worldAt(item)?.designDistance ?? NaN, now.designDistance, palette[4], value => value.toFixed(1)],
    ["#triangles-chart", "#triangles-value", "Interaction triangles", item => worldAt(item)?.interactionTriangles ?? NaN, now.interactionTriangles, palette[3], null, { zero: true }],
    ["#lines-chart", "#lines-value", "Lines", item => item.code.lines, current.code.lines, palette[4]],
    ["#complexity-chart", "#complexity-value", "Complexity", item => item.code.complexity, current.code.complexity, palette[3]],
    ["#density-chart", "#density-value", "Decision density", item => item.code.decisionDensity, current.code.decisionDensity, palette[1], value => value.toFixed(1)],
    ["#gravity-chart", "#gravity-value", "Top-10 gravity", item => item.code.topTenGravity, current.code.topTenGravity, palette[2], value => `${value.toFixed(1)}%`],
  ];
  for (const [target, valueTarget, name, value, latest, color, format, options] of charts) {
    document.querySelector(valueTarget).textContent = format ? format(latest) : number.format(latest);
    lineChart(target, [{ name, value, color, format }], { area: true, ...options });
  }

  const allTypes = Object.keys(now.actorTypes).sort((a, b) => now.actorTypes[b] - now.actorTypes[a] || a.localeCompare(b));
  const types = allTypes.slice(0, 8);
  const typeSeries = types.map((type, index) => ({
    name: type,
    color: palette[index],
    value: item => { const selected = worldAt(item); return selected ? selected.actorTypes[type] || 0 : NaN; },
  }));
  lineChart("#types-chart", typeSeries, { zero: true });
  const largest = now.actorTypes[allTypes[0]] || 1;
  document.querySelector("#types-legend").innerHTML = allTypes.map((type, index) => {
    const color = index < palette.length ? palette[index] : "#617064";
    const share = now.actorTypes[type] / largest * 100;
    return `<div class="legend-row" style="--color:${color};--bar:${share}%"><b style="background:${color};color:${color}"></b><span>${type}</span><strong>${number.format(now.actorTypes[type])}</strong></div>`;
  }).join("");
}

async function start() {
  try {
    const response = await fetch("data.json", { cache: "no-store" });
    if (!response.ok) throw new Error(`data.json returned ${response.status}`);
    data = await response.json();
    makePicker();
    render();
    const generated = new Date(data.generatedAt);
    document.querySelector("#freshness").textContent = `${data.snapshots.length} historical snapshots · refreshed ${fullDate.format(generated)}`;
    document.querySelector("#methodology").innerHTML = Object.values(data.methodology).map(value => `<p>${value}</p>`).join("") + `<p>${data.sample}</p>`;
  } catch (error) {
    document.querySelector("#stats").innerHTML = `<div class="error">Dashboard data could not be loaded: ${error.message}</div>`;
  }
}

start();
