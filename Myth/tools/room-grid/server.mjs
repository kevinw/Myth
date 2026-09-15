#!/usr/bin/env node
// Weight room screenshots in the browser and export a seeded grid image for the pitch deck.
import http from 'node:http';
import fs from 'node:fs/promises';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const production = path.resolve(here, '../../Production');
const shotsDir = path.join(production, 'Room Screenshots');
const stateFile = path.join(shotsDir, 'grid.json');
const deckDir = path.join(production, 'Pitch Deck');
const exportFile = path.join(deckDir, 'figures', 'rooms.jpg');
const port = Number(process.env.PORT ?? 8484);

const send = (res, status, type, body) => { res.writeHead(status, { 'content-type': type }); res.end(body); };
const sendJson = (res, value) => send(res, 200, 'application/json', JSON.stringify(value));

async function readBody(req) {
	const chunks = [];
	for await (const chunk of req) chunks.push(chunk);
	return Buffer.concat(chunks);
}

async function writeAtomic(file, data) {
	await fs.writeFile(`${file}.tmp`, data);
	await fs.rename(`${file}.tmp`, file);
}

async function listRooms() {
	const rooms = [];
	for (const world of (await fs.readdir(shotsDir, { withFileTypes: true })).filter(d => d.isDirectory()).map(d => d.name).sort())
		for (const file of (await fs.readdir(path.join(shotsDir, world))).filter(f => f.endsWith('.png')).sort())
			rooms.push(`${world}/${file.slice(0, -4)}`);
	return rooms;
}

function rebuildDeck() {
	return new Promise(resolve => {
		let log = '';
		const child = spawn('tectonic', ['--synctex', '--keep-logs', 'pitchdeck.tex'], { cwd: deckDir });
		child.stdout.on('data', d => log += d);
		child.stderr.on('data', d => log += d);
		child.on('error', e => resolve({ code: -1, log: String(e) }));
		child.on('close', code => resolve({ code, log: log.split('\n').filter(l => /^error|Writing `pitchdeck.pdf`/.test(l)).join('\n') }));
	});
}

const routes = {
	'GET /': async (req, res) => send(res, 200, 'text/html', await fs.readFile(path.join(here, 'index.html'))),
	'GET /api/rooms': async (req, res) => sendJson(res, await listRooms()),
	'GET /api/state': async (req, res) => send(res, 200, 'application/json', await fs.readFile(stateFile).catch(() => '{}')),
	'PUT /api/state': async (req, res) => {
		const body = await readBody(req);
		JSON.parse(body);
		await writeAtomic(stateFile, body);
		sendJson(res, { ok: true });
	},
	'POST /api/export': async (req, res) => {
		await writeAtomic(exportFile, await readBody(req));
		sendJson(res, await rebuildDeck());
	},
};

http.createServer(async (req, res) => {
	try {
		const url = new URL(req.url, 'http://localhost');
		const route = routes[`${req.method} ${url.pathname}`];
		if (route)
			return await route(req, res);
		if (req.method === 'GET' && url.pathname.startsWith('/shots/')) {
			const file = path.resolve(shotsDir, decodeURIComponent(url.pathname.slice('/shots/'.length)));
			if (file.startsWith(shotsDir + path.sep) && file.endsWith('.png'))
				return send(res, 200, 'image/png', await fs.readFile(file));
		}
		send(res, 404, 'text/plain', 'not found');
	} catch (e) {
		send(res, 500, 'text/plain', String(e));
	}
}).listen(port, '127.0.0.1', () => console.log(`room grid: http://127.0.0.1:${port}`));
