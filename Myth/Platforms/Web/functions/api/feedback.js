const MaxMessageLength = 8000;
const MaxRequestLength = 12000;

export async function onRequestPost({ request, env }) {
	if (!request.headers.get("content-type")?.toLowerCase().startsWith("application/json"))
		return json({ error: "Expected JSON" }, 415);

	let body;
	try {
		const text = await request.text();
		if (text.length > MaxRequestLength) return json({ error: "Feedback is too long" }, 413);
		body = JSON.parse(text);
	} catch {
		return json({ error: "Invalid JSON" }, 400);
	}

	// Silently accept honeypot submissions so simple bots do not learn to retry.
	if (typeof body?.website === "string" && body.website) return json({ ok: true });

	const message = typeof body?.message === "string" ? body.message.trim() : "";
	if (!message) return json({ error: "Feedback is required" }, 400);
	if (message.length > MaxMessageLength) return json({ error: "Feedback is too long" }, 413);

	const buildSha =
		typeof env.CF_PAGES_COMMIT_SHA === "string" ? env.CF_PAGES_COMMIT_SHA : null;
	await env.FEEDBACK_DB.prepare(
		"INSERT INTO feedback (message, build_sha) VALUES (?, ?)",
	)
		.bind(message, buildSha)
		.run();

	return json({ ok: true });
}

function json(body, status = 200) {
	return Response.json(body, {
		status,
		headers: { "Cache-Control": "no-store" },
	});
}
