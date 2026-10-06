// Preview the source WebGL template with an existing build; does not run Unity or build a player.
const fs = require('node:fs'), path = require('node:path'), http = require('node:http');
const repo = path.resolve(__dirname, '../..');
const build = path.resolve(repo, process.argv[2] || 'Builds/WebGL-2026-10-06-BowUiAlignment');
if (!build.startsWith(repo + path.sep)) throw Error('Preview build must be within the project.');
const template = path.join(repo, 'Assets/WebGLTemplates/BattlePvp');
const names = fs.readdirSync(path.join(build, 'Build'));
const values = { PRODUCT_NAME: 'Battle PvP · 페이지 수정 미리보기', COMPANY_NAME: 'Junu', PRODUCT_VERSION: 'preview',
    LOADER_FILENAME: names.find(x => x.endsWith('.loader.js')), DATA_FILENAME: names.find(x => x.includes('.data.')),
    FRAMEWORK_FILENAME: names.find(x => x.includes('.framework.js.')), CODE_FILENAME: names.find(x => x.includes('.wasm.')) };
http.createServer((req, res) => {
    try {
        const route = decodeURIComponent(new URL(req.url, 'http://127.0.0.1').pathname);
        if (route === '/') {
            const html = fs.readFileSync(path.join(template, 'index.html'), 'utf8')
                .replace(/^#if DEVELOPMENT_PLAYER\r?\n[\s\S]*?^#endif\r?\n/gm, '')
                .replace(/\{\{\{\s*(\w+)\s*\}\}\}/g, (_, key) => values[key] || '');
            res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' }); res.end(html); return;
        }
        const root = route.startsWith('/Build/') || route.startsWith('/StreamingAssets/') ? build : template;
        const file = path.resolve(root, '.' + route);
        if (!file.startsWith(root + path.sep)) { res.writeHead(403); res.end(); return; }
        const extension = path.extname(file);
        const type = extension === '.js' ? 'text/javascript' : extension === '.css' ? 'text/css' : 'application/octet-stream';
        const stat = fs.statSync(file);
        res.writeHead(200, { 'Content-Type': type, 'Content-Length': stat.size, 'Cache-Control': 'no-store' }); fs.createReadStream(file).pipe(res);
    } catch (_) { res.writeHead(404); res.end('Not found'); }
}).listen(8774, '127.0.0.1', () => console.log('Web shell preview: http://127.0.0.1:8774/'));
