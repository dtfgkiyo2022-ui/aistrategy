import { loadPyodide } from "./pyodide/pyodide.mjs";

// Permissions are enforced by Deno, including access through Python's js bridge.
// Keep protocol output separate from Python print / package loading messages.
const encoder = new TextEncoder();
const write = Deno.stdout.write.bind(Deno.stdout);
async function reply(value) {
  const bytes = encoder.encode(JSON.stringify(value) + "\n");
  for (let offset = 0; offset < bytes.length;) offset += await write(bytes.subarray(offset));
}
let logs = [];
function log(line) {
  if (logs.length < 20) logs.push(String(line).slice(0, 200));
}
console.log = log;
console.warn = log;
console.error = log;
const base = decodeURIComponent(new URL("./pyodide/", import.meta.url).pathname).replace(/^\/([A-Za-z]:)/, "$1");
const py = await loadPyodide({ indexURL: base, stdout: log, stderr: log });
await py.loadPackage(base + "numpy-2.4.6-cp314-cp314-pyemscripten_2026_0_wasm32.whl");
py.FS.mkdir("/tactic");
py.FS.mount(py.FS.filesystems.NODEFS, { root: Deno.args[0] }, "/tactic");
py.runPython(`
import os, sys, json, random
import numpy as np
os.chdir('/tactic')
sys.path.insert(0, '/tactic')
_tactic = {'__name__': '__tactic__', '__file__': '/tactic/main.py'}
def _start(payload):
    setup = json.loads(payload)
    seed = int(setup.get('matchSeed', 0))
    random.seed(seed)
    np.random.seed(seed % (2**32))
    _tactic['match_seed'] = seed
    with open('/tactic/main.py', encoding='utf-8-sig') as source:
        exec(compile(source.read(), '/tactic/main.py', 'exec'), _tactic)
    if not callable(_tactic.get('on_tick')):
        raise TypeError('main.py must define on_tick(view)')
    if callable(_tactic.get('on_start')):
        _tactic['on_start'](setup)
def _tick(payload):
    result = _tactic['on_tick'](json.loads(payload))
    if not isinstance(result, dict) or not isinstance(result.get('commands'), list):
        raise TypeError('on_tick must return a dict with a commands list')
    result.setdefault('version', 1)
    return json.dumps(result, ensure_ascii=False, separators=(',', ':'), allow_nan=False)
`);
const start = py.globals.get("_start");
const tick = py.globals.get("_tick");
logs = [];
await reply({ ready: true });
const decoder = new TextDecoder();
const buffer = new Uint8Array(16384);
let text = "";
while (true) {
  const count = await Deno.stdin.read(buffer);
  if (count === null) break;
  text += decoder.decode(buffer.subarray(0, count), { stream: true });
  let end;
  while ((end = text.indexOf("\n")) >= 0) {
    const line = text.slice(0, end);
    text = text.slice(end + 1);
    logs = [];
    try {
      const message = JSON.parse(line);
      if (message.op === "start") {
        start(message.payload);
        await reply({ logs });
      } else if (message.op === "tick") {
        const output = tick(message.payload);
        await reply({ output, logs });
      } else throw new Error("Unknown operation");
    } catch (error) {
      await reply({ error: String(error).slice(0, 4000), logs });
    }
  }
  if (text.length > 4 * 1024 * 1024) throw new Error("Request too large");
}
