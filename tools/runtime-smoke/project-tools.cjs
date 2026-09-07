const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const cp = require('node:child_process');
const VM = require(path.resolve(path.dirname(require.resolve('@scratch/scratch-vm')), '../../src/index.js'));
const { ScratchStorage } = require('scratch-storage');
const root = path.resolve(__dirname, '../..');
const output = path.join(root, 'artifacts/project-tools-smoke');
const executable = process.env.SCRATCHASM_EXE || path.join(root, 'src/OpenCTS.App/bin/Debug/net10.0-windows/OpenCTS.App.exe');
fs.mkdirSync(output, { recursive: true });
function cli(args, success = true) {
  const result = cp.spawnSync(executable, [...args, '--overwrite'], { encoding: 'utf8', windowsHide: true, timeout: 60000 });
  assert.equal(result.status === 0, success, (result.stdout || '') + (result.stderr || '') + (result.error || ''));
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
  const operations = [
    ['bitwiseAnd', 6, 3, 6 & 3], ['bitwiseOr', -7, 3, -7 | 3],
    ['bitwiseXor', 2147483647, -1, 2147483647 ^ -1],
    ['bitwiseNot', 6, 0, ~6], ['bitwiseLeftShift', 3, 31, 3 << 31],
    ['bitwiseRightShift', -2147483648, -1, -2147483648 >> -1],
    ['bitwiseLogicalRightShift', -1, 0, -1 >>> 0]
  ];
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
  console.log('PASS: native Scratch execution matches optimized variable/list operations and all seven literal Bitwise workarounds; unsupported output is refused.');
}
main().then(() => process.exit(0)).catch(error => { console.error(error.stack); process.exit(1); });
