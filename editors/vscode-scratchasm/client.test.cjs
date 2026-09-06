const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const { EventEmitter } = require('node:events');

function createClient() {
  const child = new EventEmitter();
  child.stdout = new EventEmitter();
  child.stderr = new EventEmitter();
  child.stdin = Object.assign(new EventEmitter(), { writable: true, write() {} });
  child.kill = () => child.emit('exit', 0);
  const vscode = {
    workspace: { getConfiguration: () => ({ get: () => 'host.exe' }), textDocuments: [] },
    window: { showWarningMessage() {} }
  };
  const context = {
    module: { exports: {} }, Buffer, setTimeout, clearTimeout, process,
    console: { warn() {} },
    require(name) {
      if (name === 'vscode') return vscode;
      if (name === 'child_process') return { spawn: () => child };
      if (name === 'fs') return { existsSync: () => true };
      return require(name);
    }
  };
  vm.runInNewContext(fs.readFileSync(require.resolve('./extension.js'), 'utf8') + '\nmodule.exports.Client = ScratchAsmClient;', context);
  return { client: new context.module.exports.Client(__dirname, { delete() {} }), child };
}

test('host exit rejects outstanding requests without throwing in the exit handler', async () => {
  const { client, child } = createClient();
  client.start();
  const pending = client.request('textDocument/completion', {});
  const rejected = assert.rejects(pending, /exited/);
  assert.doesNotThrow(() => child.emit('exit', 1));
  await rejected;
  assert.equal(client.pending.size, 0);
});

test('dispose rejects pending requests and requests to a stopped host fail immediately', async () => {
  const { client } = createClient();
  await assert.rejects(client.request('completion', {}), /not running/);
  client.start();
  const rejected = assert.rejects(client.request('completion', {}), /stopped/);
  client.dispose();
  await rejected;
});

test('fragmented UTF-8 responses resolve their matching request', async () => {
  const { client, child } = createClient();
  client.process = child;
  const result = client.request('completion', {});
  const payload = Buffer.from(JSON.stringify({ jsonrpc: '2.0', id: 1, result: '\u4f60\u597d' }));
  const frame = Buffer.concat([Buffer.from(`Content-Length: ${payload.length}\r\n\r\n`), payload]);
  for (const byte of frame) client.accept(Buffer.from([byte]));
  assert.equal(await result, '\u4f60\u597d');
  assert.equal(client.pending.size, 0);
});

test('malformed host JSON rejects pending requests', async () => {
  const { client, child } = createClient();
  client.process = child;
  const rejected = assert.rejects(client.request('completion', {}));
  client.accept(Buffer.from('Content-Length: 1\r\n\r\n{'));
  await rejected;
});
