const fs = require('node:fs');
const path = require('node:path');

async function main() {
  const base = 'https://raw.githubusercontent.com/TurboWarp/extensions/master/extensions/';
  async function read(url) {
    const response = await fetch(url);
    if (!response.ok) throw new Error(response.status + ': ' + url);
    return response.text();
  }
  // The gallery index is JSONC, not JavaScript. Extension source is read as metadata, never executed.
  const index = JSON.parse((await read(base + 'extensions.json')).replace(/\/\/[^\r\n]*/g, ''));
  const entries = [];
  for (const file of index) {
    const source = await read(base + file + '.js');
    const id = source.match(/^\/\/ ID:\s*(\S+)/m)?.[1] ??
      source.match(/\bid:\s*['"]([A-Za-z][A-Za-z0-9]*)['"]/)?.[1];
    const name = source.match(/^\/\/ Name:\s*(.+)$/m)?.[1]?.trim() ?? file;
    const color = source.match(/\bcolor1:\s*['"](#[0-9a-fA-F]{6})['"]/)?.[1] ?? '#0FBD8C';
    if (!id || !/^[A-Za-z0-9]+$/.test(id)) throw new Error('Review extension ID: ' + file);
    entries.push({ Id: id, Name: name, Url: 'https://extensions.turbowarp.org/' + file + '.js', Color: color });
  }
  fs.writeFileSync(path.join(__dirname, '../src/OpenCTS.Core/turbowarp-catalog.json'), JSON.stringify(entries, null, 2) + '\n');
  console.log('Indexed ' + entries.length + ' released gallery extensions without executing their code.');
}
main().catch(error => { console.error(error.message); process.exitCode = 1; });
