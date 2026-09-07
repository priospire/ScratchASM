const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const cp = require('node:child_process');
const JSZip = require('jszip');
const VM = require(path.resolve(path.dirname(require.resolve('@scratch/scratch-vm')), '../../src/index.js'));
const { ScratchStorage } = require('scratch-storage');
const root = path.resolve(__dirname, '../..');
const output = path.join(root, 'artifacts/project-tools-smoke');
const executable = process.env.SCRATCHASM_EXE || path.join(root, 'src/OpenCTS.App/bin/Debug/net10.0-windows/OpenCTS.App.exe');
fs.mkdirSync(output, { recursive: true });
function cli(args, success = true) {
  const result = cp.spawnSync(executable, args[0] === '--provenance' ? args : [...args, '--overwrite'], { encoding: 'utf8', windowsHide: true, timeout: 60000 });
  assert.equal(result.status === 0, success, (result.stdout || '') + (result.stderr || '') + (result.error || ''));
  return result.stdout;
}
async function run(file) {
  const vm = new VM(); vm.attachStorage(new ScratchStorage());
  try {
    await vm.loadProject(fs.readFileSync(file));
    vm.runtime.currentStepTime = 1000 / 30;
    vm.greenFlag();
    for (let i = 0; i < 200 && vm.runtime.threads.length; i++) vm.runtime._step();
    return Object.fromEntries(Object.values(vm.runtime.getTargetForStage().variables).map(value => [value.name, value.value]));
  } finally { vm.quit(); }
}
async function main() {
  const source = path.join(output, 'arithmetic.sasm');
  fs.writeFileSync(source, 'stage {\n  var total = 0\n  list items = [10,20,30]\n  @greenflag:\n    total = 4 * 5\n    items.replace (1 + 1) 40\n}\n');
  const before = path.join(output, 'before.sb3'), after = path.join(output, 'optimized.sb3');
  cli([source, before]); cli(['--optimize', source, after]);
  const original = await run(before), optimized = await run(after);
  assert.deepEqual(optimized, original);
  assert.equal(Number(optimized.total), 20);
  assert.deepEqual(optimized.items.map(Number), [10, 40, 30]);
  // These are the numeric operations in the official bitwise.js extension.
  const evaluators = {
    bitwiseAnd: (a, b) => a & b, bitwiseOr: (a, b) => a | b,
    bitwiseXor: (a, b) => a ^ b, bitwiseNot: a => ~a,
    bitwiseLeftShift: (a, b) => a << b, bitwiseRightShift: (a, b) => a >> b,
    bitwiseLogicalRightShift: (a, b) => a >>> b
  };
  const edges = [-2147483648, -65537, -33, -32, -1, 0, 1, 6, 31, 32, 33, 65537, 2147483647];
  let seed = 0x13579bdf;
  const randomInt = () => (seed = (Math.imul(seed, 1664525) + 1013904223) | 0);
  const operations = Object.entries(evaluators).flatMap(([opcode, evaluate]) => {
    const pairs = opcode === 'bitwiseNot' ? edges.map(a => [a, 0]) : edges.flatMap(a => edges.map(b => [a, b]));
    for (let i = 0; i < 100; i++) pairs.push([randomInt(), randomInt()]);
    return pairs.map(([a, b]) => [opcode, a, b, evaluate(a, b)]);
  });
  const bitwise = path.join(output, 'bitwise.sasm');
  fs.writeFileSync(bitwise, 'stage {\n  extension Bitwise "https://extensions.turbowarp.org/bitwise.js"\n' +
    operations.map((_, i) => '  var value' + i + ' = 0\n').join('') + '  @greenflag:\n' +
    operations.map(([opcode, a, b], i) => '    value' + i + ' = [Bitwise_' + opcode +
      (opcode === 'bitwiseNot' ? ' input CENTRAL=' + a : ' input LEFT=' + a + ' input RIGHT=' + b) + ']\n').join('') + '}\n');
  const vanilla = path.join(output, 'vanilla.sb3');
  cli(['--vanilla', bitwise, vanilla]);
  const values = await run(vanilla);
  operations.forEach((operation, i) => assert.equal(Number(values['value' + i]), operation[3], operation[0]));
  const blocked = path.join(output, 'blocked.sasm');
  fs.writeFileSync(blocked, 'stage {\n  extension custom "https://example.com/custom.js"\n}\n');
  const blockedOutput = path.join(output, 'blocked-' + Date.now() + '.sb3');
  cli(['--vanilla', blocked, blockedOutput], false);
  assert.equal(fs.existsSync(blockedOutput), false);
  const many = await JSZip.loadAsync(fs.readFileSync(before));
  for (let i = 0; i < 5000; i++) many.file('extra' + i + '.bin', Buffer.from([42]));
  const largeInput = path.join(output, 'many-entries.sb3');
  const largeSource = path.join(output, 'many-entries.sasm');
  const largeOutput = path.join(output, 'many-entries-roundtrip.sb3');
  fs.writeFileSync(largeInput, await many.generateAsync({ type: 'nodebuffer', compression: 'DEFLATE' }));
  cli([largeInput, largeSource]); cli([largeSource, largeOutput]);
  const restored = await JSZip.loadAsync(fs.readFileSync(largeOutput));
  assert.equal(Object.keys(restored.files).length, Object.keys(many.files).length);
  assert.deepEqual(await restored.file('extra4999.bin').async('nodebuffer'), Buffer.from([42]));
  for (const file of [before, vanilla, largeSource, largeOutput]) {
    const provenance = JSON.parse(cli(['--provenance', file]));
    assert.equal(provenance.recognized, true, file);
    assert.equal(provenance.contentHashMatches, true, file);
  }
  fs.appendFileSync(largeSource, '\n# external edit\n');
  assert.equal(JSON.parse(cli(['--provenance', largeSource])).contentHashMatches, false);
  console.log('PASS: published CLI imports and round-trips more than 5,000 ZIP entries without dropping extra assets.');
  console.log(`PASS: native Scratch execution matches optimized variable/list operations and ${operations.length} boundary/seeded cases across seven literal Bitwise workarounds; unsupported output is refused.`);
  console.log('PASS: archive/source provenance verifies, and an external source edit is detected.');
}
main().then(() => process.exit(0)).catch(error => { console.error(error.stack); process.exit(1); });
