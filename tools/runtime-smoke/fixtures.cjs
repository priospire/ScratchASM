const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const cp = require('node:child_process');
const JSZip = require('jszip');
const root = path.resolve(__dirname, '../..');
const directory = path.join(root, 'artifacts/scratch-fixtures');
const executable = process.env.SCRATCHASM_EXE || path.join(root, 'src/OpenCTS.App/bin/Debug/net10.0-windows/OpenCTS.App.exe');
const revision = 'd4eb4878970a4958020946018669c8e5d255e5fc';
const names = ['default', 'top-level-reporters', 'variable_characters', 'broadcast_special_chars',
  'comments', 'comments_no_duplicate_id_serialization', 'draggable', 'list-monitor-rename', 'monitors'];

function convert(input, output) {
  const result = cp.spawnSync(executable, [input, output, '--overwrite'], { encoding: 'utf8', timeout: 60000, windowsHide: true });
  assert.equal(result.status, 0, `${path.basename(input)}: ${result.stderr || result.stdout || result.error}`);
}
async function compare(original, rebuilt) {
  const before = await JSZip.loadAsync(fs.readFileSync(original));
  const after = await JSZip.loadAsync(fs.readFileSync(rebuilt));
  assert.deepEqual(Object.keys(after.files).sort(), Object.keys(before.files).sort());
  for (const name of Object.keys(before.files))
    assert.deepEqual(await after.files[name].async('nodebuffer'), await before.files[name].async('nodebuffer'), name);
}
async function main() {
  fs.mkdirSync(directory, { recursive: true });
  for (const name of names) {
    const original = path.join(directory, name + '.sb3');
    if (!fs.existsSync(original)) {
      const response = await fetch(`https://raw.githubusercontent.com/scratchfoundation/scratch-editor/${revision}/packages/scratch-vm/test/fixtures/${name}.sb3`);
      assert.ok(response.ok, `Fixture download failed: ${name}`);
      fs.writeFileSync(original, Buffer.from(await response.arrayBuffer()));
    }
    const source = path.join(directory, name + '.sasm');
    const rebuilt = path.join(directory, name + '.rebuilt.sb3');
    const edited = path.join(directory, name + '.edited.sb3');
    convert(original, source);
    convert(source, rebuilt);
    await compare(original, rebuilt);
    fs.appendFileSync(source, '\n# Round-trip merge check\n');
    convert(source, edited);
    const beforeZip = await JSZip.loadAsync(fs.readFileSync(original));
    const editedZip = await JSZip.loadAsync(fs.readFileSync(edited));
    const before = JSON.parse(await beforeZip.file('project.json').async('string'));
    const after = JSON.parse(await editedZip.file('project.json').async('string'));
    assert.equal(after.targets.length, before.targets.length);
    for (let i = 0; i < before.targets.length; i++) {
      for (const property of ['name', 'costumes', 'sounds', 'currentCostume', 'x', 'y', 'direction', 'size', 'volume', 'draggable'])
        assert.deepEqual(after.targets[i][property], before.targets[i][property], `${name}: ${property}`);
      assert.equal(Object.keys(after.targets[i].blocks).length, Object.keys(before.targets[i].blocks).length, `${name}: block count`);
    }
    for (const asset of Object.keys(beforeZip.files).filter(file => file !== 'project.json'))
      assert.deepEqual(await editedZip.files[asset].async('nodebuffer'), await beforeZip.files[asset].async('nodebuffer'));
    console.log(`PASS: ${name} exact and edited round trips`);
  }
}
main().catch(error => { console.error(error.stack); process.exitCode = 1; });
