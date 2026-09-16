// Serves /figures/* with HTTP byte ranges, which Pages static assets don't support and
// Safari requires to play <video>. Deploy from this folder so Pages picks up functions/:
//   ./deploy.sh
export async function onRequest({ request, env }) {
	const asset = await env.ASSETS.fetch(new URL(request.url));
	const headers = new Headers(asset.headers);
	headers.set('Accept-Ranges', 'bytes');

	const range = /^bytes=(\d*)-(\d*)$/.exec(request.headers.get('Range') ?? '');
	if (!asset.ok || !['GET', 'HEAD'].includes(request.method) || !range || (range[1] === '' && range[2] === ''))
		return new Response(request.method === 'HEAD' ? null : asset.body, { status: asset.status, headers });

	const body = await asset.arrayBuffer();
	const size = body.byteLength;
	const start = range[1] === '' ? Math.max(0, size - Number(range[2])) : Number(range[1]);
	const end = range[1] === '' || range[2] === '' ? size - 1 : Math.min(Number(range[2]), size - 1);
	if (start > end) {
		headers.set('Content-Range', `bytes */${size}`);
		headers.delete('Content-Length');
		return new Response(null, { status: 416, headers });
	}

	headers.set('Content-Range', `bytes ${start}-${end}/${size}`);
	headers.set('Content-Length', String(end - start + 1));
	return new Response(request.method === 'HEAD' ? null : body.slice(start, end + 1), { status: 206, headers });
}
