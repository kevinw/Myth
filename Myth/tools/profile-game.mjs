#!/usr/bin/env node
// Runs Myth under dotnet-trace until the game exits, then writes trace artifacts and a
// folded-stack summary. Cross-platform (Windows / macOS / Linux) - needs only Node +
// dotnet + dotnet-trace; the Speedscope analysis is done in JS (no Python).
//
// Usage:
//   node tools/profile-game.mjs [options] [-- game args...]
//
// Options:
//   -c, --configuration <Debug|Release>  Build/run configuration. Default: Release
//   -p, --profile <name>                 Optional dotnet-trace profile. Default: tool default
//   -o, --output-dir <dir>               Output root. Default: Profiles
//       --no-build                       Skip dotnet build before tracing
//       --no-convert                     Do not convert .nettrace to Speedscope JSON
//       --no-summary                     Do not write summary.txt
//   -h, --help                           Show this help
//
// Artifacts: Profiles/<timestamp>/{myth.nettrace, myth.speedscope.json, summary.txt}
//
// If dotnet-trace is missing:
//   dotnet tool install --global dotnet-trace
//   # then ensure the global tools dir (~/.dotnet/tools) is on PATH

import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import fs from 'node:fs';
import path from 'node:path';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoDir = path.resolve(scriptDir, '..');

const opts = {
	configuration: 'Release',
	profile: '',
	profilesDir: path.join(repoDir, 'Profiles'),
	convertSpeedscope: true,
	writeSummary: true,
	buildFirst: true,
	gameArgs: [],
};

function usage() {
	// The header comment above is the canonical usage text.
	const src = fs.readFileSync(fileURLToPath(import.meta.url), 'utf8');
	const lines = src.split(/\r?\n/);
	for (const line of lines.slice(1)) {
		if (!line.startsWith('//')) break;
		console.log(line.replace(/^\/\/ ?/, ''));
	}
}

const argv = process.argv.slice(2);
for (let i = 0; i < argv.length; i++) {
	const arg = argv[i];
	switch (arg) {
		case '-c':
		case '--configuration':
			opts.configuration = argv[++i] ?? '';
			break;
		case '-p':
		case '--profile':
			opts.profile = argv[++i] ?? '';
			break;
		case '-o':
		case '--output-dir':
			opts.profilesDir = path.resolve(argv[++i] ?? '');
			break;
		case '--no-build':
			opts.buildFirst = false;
			break;
		case '--no-convert':
			opts.convertSpeedscope = false;
			break;
		case '--no-summary':
			opts.writeSummary = false;
			break;
		case '-h':
		case '--help':
			usage();
			process.exit(0);
			break;
		case '--':
			opts.gameArgs = argv.slice(i + 1);
			i = argv.length;
			break;
		default:
			console.error(`Unknown option: ${arg}`);
			usage();
			process.exit(2);
	}
}

function commandExists(cmd) {
	// CreateProcess (Windows) appends .exe for extensionless names, so dotnet/dotnet-trace
	// resolve via PATH without a shell. ENOENT means not found.
	const res = spawnSync(cmd, ['--version'], { stdio: 'ignore' });
	return !(res.error && res.error.code === 'ENOENT');
}

function run(cmd, args, extra = {}) {
	const res = spawnSync(cmd, args, { stdio: 'inherit', ...extra });
	if (res.error) {
		console.error(`Failed to run ${cmd}: ${res.error.message}`);
		process.exit(1);
	}
	return res;
}

function capture(cmd, args) {
	const res = spawnSync(cmd, args, { encoding: 'utf8' });
	if (res.error || res.status !== 0) return null;
	return (res.stdout ?? '').trim();
}

if (!commandExists('dotnet')) {
	console.error('dotnet is not on PATH.');
	process.exit(1);
}

if (!commandExists('dotnet-trace')) {
	console.error(
		'dotnet-trace is not installed.\n\n' +
		'Install it with:\n' +
		'  dotnet tool install --global dotnet-trace\n\n' +
		'If the install succeeds but this script still cannot find it, add the global\n' +
		'tools directory to PATH (~/.dotnet/tools on Unix, %USERPROFILE%\\.dotnet\\tools on Windows).'
	);
	process.exit(1);
}

const now = new Date();
const pad = (n) => String(n).padStart(2, '0');
const timestamp =
	`${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}` +
	`-${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;

const runDir = path.join(opts.profilesDir, timestamp);
const tracePath = path.join(runDir, 'myth.nettrace');
const summaryPath = path.join(runDir, 'summary.txt');
const speedscopePath = path.join(runDir, 'myth.speedscope.json');
const projectPath = path.join(repoDir, 'Myth.csproj');

if (!fs.existsSync(projectPath)) {
	console.error(`Project not found: ${projectPath}`);
	process.exit(1);
}

const csproj = fs.readFileSync(projectPath, 'utf8');
const tfmMatch = csproj.match(/<TargetFramework>(.*?)<\/TargetFramework>/);
if (!tfmMatch) {
	console.error(`Could not read <TargetFramework> from ${projectPath}`);
	process.exit(1);
}
const targetFramework = tfmMatch[1];
const targetDll = path.join(repoDir, 'bin', opts.configuration, targetFramework, 'Myth.dll');

fs.mkdirSync(runDir, { recursive: true });

if (opts.buildFirst) {
	console.log(`Building Myth (${opts.configuration}) ...`);
	const res = run('dotnet', ['build', projectPath, '-c', opts.configuration]);
	if (res.status !== 0) {
		console.error('Build failed.');
		process.exit(res.status ?? 1);
	}
}

if (!fs.existsSync(targetDll)) {
	console.error(`Expected build output not found: ${targetDll}`);
	console.error('Run without --no-build, or check the project output path.');
	process.exit(1);
}

const gameArgsDisplay = opts.gameArgs.join(' ');

if (opts.writeSummary) {
	const header = [
		'Myth profile run',
		`Started: ${now.toString()}`,
		`Repository: ${repoDir}`,
		`Configuration: ${opts.configuration}`,
		`Profile: ${opts.profile || 'dotnet-trace default'}`,
		`Command: dotnet ${targetDll} ${gameArgsDisplay}`.trimEnd(),
		'',
	];
	const commit = capture('git', ['-C', repoDir, 'rev-parse', '--short', 'HEAD']);
	if (commit) {
		header.push(`Git commit: ${commit}`);
		const status = capture('git', ['-C', repoDir, 'status', '--short']);
		header.push('Git status:');
		if (status) header.push(status);
	}
	fs.writeFileSync(summaryPath, header.join('\n') + '\n');
}

console.log('Tracing until the game exits ...');
console.log(`Trace: ${tracePath}`);
const traceArgs = ['collect', '--output', tracePath];
if (opts.profile) traceArgs.push('--profile', opts.profile);
traceArgs.push('--', 'dotnet', targetDll, ...opts.gameArgs);
run('dotnet-trace', traceArgs);

if (opts.convertSpeedscope && fs.existsSync(tracePath)) {
	console.log('Converting to Speedscope JSON ...');
	// dotnet-trace writes "<input-basename>.speedscope.json" next to the input, which is
	// exactly speedscopePath. Handle stray output names defensively.
	run('dotnet-trace', ['convert', tracePath, '--format', 'speedscope']);
	const expected = tracePath.replace(/\.nettrace$/, '.speedscope.json');
	if (fs.existsSync(expected) && expected !== speedscopePath) {
		fs.renameSync(expected, speedscopePath);
	} else if (!fs.existsSync(speedscopePath)) {
		const stray = fs
			.readdirSync(runDir)
			.find((f) => f.endsWith('.speedscope.json'));
		if (stray) fs.renameSync(path.join(runDir, stray), speedscopePath);
	}
}

if (opts.writeSummary) {
	const footer = ['', `Finished: ${new Date().toString()}`, 'Artifacts:', `  nettrace: ${tracePath}`];
	if (fs.existsSync(speedscopePath)) footer.push(`  speedscope: ${speedscopePath}`);
	footer.push('');
	fs.appendFileSync(summaryPath, footer.join('\n') + '\n');

	if (fs.existsSync(speedscopePath)) {
		try {
			fs.appendFileSync(summaryPath, summarizeSpeedscope(speedscopePath) + '\n');
		} catch (e) {
			fs.appendFileSync(summaryPath, `Speedscope summary failed: ${e.message}\n`);
		}
	}
}

console.log('');
console.log('Profile complete.');
console.log(`Summary: ${summaryPath}`);
console.log(`Trace:   ${tracePath}`);
if (fs.existsSync(speedscopePath)) {
	console.log(`Open:    ${speedscopePath}`);
	console.log('         https://www.speedscope.app');
}

// --- Speedscope analysis (port of the former inline Python) ---
function summarizeSpeedscope(file) {
	const data = JSON.parse(fs.readFileSync(file, 'utf8'));
	const frames = data.shared?.frames ?? [];
	const frameNames = frames.map((f, i) => f.name || f.file || `<frame ${i}>`);

	const clean = (name) => {
		for (const prefix of ['Myth.', 'Foster.Framework.']) {
			if (name.startsWith(prefix)) return name.slice(prefix.length);
		}
		return name;
	};

	const leaf = new Map();
	const stacks = new Map();
	let total = 0;
	const add = (map, key, w) => map.set(key, (map.get(key) ?? 0) + w);

	for (const profile of data.profiles ?? []) {
		if (profile.samples) {
			const weights = profile.weights ?? profile.samples.map(() => 1);
			for (let s = 0; s < profile.samples.length; s++) {
				const sample = profile.samples[s];
				if (!sample || sample.length === 0) continue;
				const names = sample
					.filter((i) => i >= 0 && i < frameNames.length)
					.map((i) => clean(frameNames[i]));
				if (names.length === 0) continue;
				const w = Number(weights[s]) || 0;
				total += w;
				add(leaf, names[names.length - 1], w);
				add(stacks, names.join(';'), w);
			}
			continue;
		}

		// Evented Speedscope profiles: count closed stack samples.
		const openStack = [];
		for (const event of profile.events ?? []) {
			if (event.type === 'O' && event.frame != null) {
				if (event.frame >= 0 && event.frame < frameNames.length)
					openStack.push(clean(frameNames[event.frame]));
			} else if (event.type === 'C' && openStack.length) {
				total += 1;
				add(leaf, openStack[openStack.length - 1], 1);
				add(stacks, openStack.join(';'), 1);
				openStack.pop();
			}
		}
	}

	if (total <= 0) return 'No samples found in Speedscope JSON.';

	const section = (title, map, limit) => {
		const top = [...map.entries()].sort((a, b) => b[1] - a[1]).slice(0, limit);
		const lines = [title];
		for (const [name, count] of top) {
			const pct = ((count * 100) / total).toFixed(2).padStart(6);
			lines.push(`  ${pct}%  ${name}`);
		}
		lines.push('');
		return lines.join('\n');
	};

	return section('Top leaf frames:', leaf, 30) + '\n' + section('Top folded stacks:', stacks, 40);
}
