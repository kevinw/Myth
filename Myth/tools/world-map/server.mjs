#!/usr/bin/env node

import http from 'node:http';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const data = path.resolve(here, '../../Production/World Map');
const port = Number(process.env.PORT ?? 8485);
const types = { '.html': 'text/html; charset=utf-8', '.json': 'application/json', '.png': 'image/png' };

http.createServer(async (req, res) => {
	try {
		const url = new URL(req.url, 'http://localhost');
		const relative = url.pathname === '/' ? null : decodeURIComponent(url.pathname.slice(1));
		const file = relative ? path.resolve(data, relative) : path.join(here, 'index.html');
		if (relative && file !== data && !file.startsWith(data + path.sep))
			throw new Error('invalid path');
		const body = await fs.readFile(file);
		res.writeHead(200, { 'content-type': types[path.extname(file)] ?? 'application/octet-stream' });
		res.end(body);
	} catch (error) {
		res.writeHead(404, { 'content-type': 'text/plain; charset=utf-8' });
		res.end(`not found: ${error.message}`);
	}
}).listen(port, '127.0.0.1', () => console.log(`world map: http://127.0.0.1:${port}`));
