const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const cp = require('node:child_process');
const JSZip = require('jszip');
const VM = require(path.resolve(path.dirname(require.resolve('@scratch/scratch-vm')), '../../src/index.js'));
const { ScratchStorage: Storage } = require('scratch-storage');
const root = path.resolve(__dirname, '../..');
const directory = path.join(root, 'artifacts/readable-import');
const executable = process.env.SCRATCHASM_EXE || path.join(root, 'src/OpenCTS.App/bin/Debug/net10.0-windows/OpenCTS.App.exe');

function convert(input, output) {
  const result = cp.spawnSync(executable, [input, output, '--overwrite'], { encoding: 'utf8', timeout: 60000, windowsHide: true });
  assert.equal(result.status, 0, (result.stdout || '') + (result.stderr || '') + (result.error || ''));
}

async function run(file, expected) {
  const vm = new VM();
  vm.attachStorage(new Storage());
  try {
    await vm.loadProject(fs.readFileSync(file));
    vm.runtime.currentStepTime = 1000 / 30;
    vm.greenFlag();
    for (let i = 0; i < 100 && vm.runtime.threads.length; i++) vm.runtime._step();
    const variables = Object.values(vm.runtime.getTargetForStage().variables);
    const result = variables.find(variable => variable.name === 'result').value;
    const items = variables.find(variable => variable.name === 'items').value;
    assert.equal(Number(result), expected, file);
    assert.deepEqual(items.map(Number), [0.5, expected], file);
    assert.equal(Number(variables.find(variable => variable.name === 'total').value), 11, 'legacy loop bodies');
  } finally { vm.quit(); }
}

async function main() {
  fs.mkdirSync(directory, { recursive: true });
  const authored = path.join(directory, 'authored.sasm');
  const native = path.join(directory, 'native.sb3');
  const imported = path.join(directory, 'imported.sasm');
  fs.writeFileSync(authored, `stage {
  var result = 0
  var total = 0
  var index = 0
  list items = []
  proc compute(amount: num, enabled: bool) as "compute %n when %b" warp:
    if enabled:
      result = (amount * 2)
    else:
      result = 999
  @greenflag:
    call compute(7, (1 == 1))
    repeat 2:
      items.add result
    items.replace 1, sin(30)
    legacy.control.foreach index, (1 + 2):
      total += index
    legacy.control.while (total < 10):
      total += 1
    legacy.control.allatonce:
      total += 1
  stack:
    result = -999
  reporter (result + 1)
}
`);
  convert(authored, native);
  const zip = await JSZip.loadAsync(fs.readFileSync(native));
  const project = JSON.parse(await zip.file('project.json').async('string'));
  const blocks = Object.values(project.targets[0].blocks);
  const prototype = blocks.find(block => block.opcode === 'procedures_prototype');
  const previous = JSON.parse(prototype.mutation.argumentids);
  const ids = ['native-number-parameter', 'native-boolean-parameter'];
  for (const block of blocks) {
    for (let i = 0; i < previous.length; i++) {
      if (Object.hasOwn(block.inputs, previous[i])) {
        block.inputs[ids[i]] = block.inputs[previous[i]];
        delete block.inputs[previous[i]];
      }
    }
    if (block.mutation?.argumentids) block.mutation.argumentids = JSON.stringify(ids);
  }
  prototype.mutation.argumentnames = JSON.stringify(['input amount', 'is enabled?']);
  for (const block of blocks) {
    if (block.shadow === false) delete block.shadow;
    if (block.opcode.startsWith('argument_reporter_'))
      block.fields.VALUE[0] = block.fields.VALUE[0] === 'amount' ? 'input amount' : 'is enabled?';
  }
  zip.file('project.json', JSON.stringify(project));
  fs.writeFileSync(native, await zip.generateAsync({ type: 'nodebuffer' }));
  convert(native, imported);
  const text = fs.readFileSync(imported, 'utf8');
  assert.doesNotMatch(text, /rawblocks|"opcode"|procedures_|operator_|\[shadow/);
  assert.match(text, /proc compute/);
  assert.match(text, /call compute/);
  assert.match(text, /stack:/);
  await run(native, 14);

  // Compile without the companion to exercise the emitted language, not preservation.
  const standalone = path.join(directory, 'standalone.sasm');
  fs.writeFileSync(standalone, text.replace(/^project .*\r?\n/gm, ''));
  const standaloneOutput = path.join(directory, 'standalone.sb3');
  convert(standalone, standaloneOutput);
  await run(standaloneOutput, 14);

  for (const [name, from, to, expected] of [
    ['body', 'input_amount * 2', 'input_amount * 3', 21],
    ['call', 'call compute__when(7,', 'call compute__when(8,', 16]
  ]) {
    const edited = text.replace(from, to);
    assert.notEqual(edited, text, `Editable ${name} is visible in source`);
    fs.writeFileSync(imported, edited);
    const output = path.join(directory, name + '.sb3');
    convert(imported, output);
    await run(output, expected);
  }
  console.log('PASS: native custom parameter IDs/names, readable standalone recompilation, edited bodies/calls, loops, lists, math, and disconnected blocks execute correctly.');
}
main().then(() => process.exit(0)).catch(error => { console.error(error.stack); process.exit(1); });
