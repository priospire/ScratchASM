const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const cp = require('node:child_process');
const JSZip = require('jszip');
// Use the published source entry: the bundled Node entry omits a jsdom stylesheet.
const VM = require(path.resolve(path.dirname(require.resolve('@scratch/scratch-vm')), '../../src/index.js'));
const { ScratchStorage: Storage } = require('scratch-storage');
const root = path.resolve(__dirname, '../..');
const output = path.join(root, 'artifacts/runtime-smoke');
const executable = process.env.SCRATCHASM_EXE || path.join(root, 'src/OpenCTS.App/bin/Debug/net10.0-windows/OpenCTS.App.exe');
fs.mkdirSync(output, { recursive: true });

function convert(...args) {
  const result = cp.spawnSync(executable, args, { encoding: 'utf8', windowsHide: true, timeout: 60000 });
  assert.equal(result.status, 0, (result.stdout || '') + (result.stderr || '') + (result.error || ''));
}
async function readZip(file) {
  const zip = await JSZip.loadAsync(fs.readFileSync(file));
  const entries = {};
  for (const [name, entry] of Object.entries(zip.files)) entries[name] = await entry.async('nodebuffer');
  return entries;
}
async function run(file, expectedX = 45) {
  const vm = new VM();
  vm.attachStorage(new Storage());
  try {
    await vm.loadProject(fs.readFileSync(file));
    const stage = vm.runtime.getTargetForStage();
    vm.runtime.currentStepTime = 1000 / 30;
    vm.greenFlag();
    for (let i = 0; i < 300; i++) {
      vm.runtime._step();
      if (vm.runtime.threads.length === 0) break;
    }
    const variable = name => Object.values(stage.variables).find(value => value.name === name)?.value;
    assert.equal(Number(variable('result')), 16, `procedure locals and global variables: ${file}`);
    assert.equal(Number(variable('power')), 256, `power lowering: ${file}`);
    assert.equal(variable('label'), '001', `numeric-looking string: ${file}`);
    assert.deepEqual(variable('values').map(Number), [5, 5, 5], `list operations: ${file}`);
    const sprite = vm.runtime.targets.find(target => target.sprite.name === 'Calculator');
    assert.equal(sprite.x, expectedX);
    for (const variable of Object.values(sprite.variables))
      if (variable.name.startsWith('__sasm_') && Array.isArray(variable.value)) assert.equal(variable.value.length, 0, 'local frames cleaned up');
  } finally { vm.quit(); }
}

async function main() {
  const original = path.join(output, 'original.sb3');
  const source = path.join(output, 'imported.sasm');
  const rebuilt = path.join(output, 'rebuilt.sb3');
  const edited = path.join(output, 'edited.sb3');
  convert(path.join(root, 'samples/roundtrip.sasm'), original, '--overwrite');
  convert(original, source, '--overwrite');
  convert(source, rebuilt, '--overwrite');
  const before = await readZip(original);
  const after = await readZip(rebuilt);
  assert.deepEqual(Object.keys(after).sort(), Object.keys(before).sort());
  for (const name of Object.keys(before)) assert.deepEqual(after[name], before[name], `unchanged entry: ${name}`);
  fs.writeFileSync(source, fs.readFileSync(source, 'utf8').replace('state x=45', 'state x=46'));
  convert(source, edited, '--overwrite');
  await run(original);
  await run(rebuilt);
  await run(edited, 46);
  console.log('PASS: Scratch VM loaded and executed original, portable round trip, and edited round trip.');
  console.log('PASS: custom procedures, scoped locals, global variables, lists, power, trig, sprite state, assets, and exact archive entries.');
}
main().then(() => process.exit(0)).catch(error => { console.error(error.stack); process.exit(1); });
