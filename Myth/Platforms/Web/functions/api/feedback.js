const MaxMessageLength = 8000;
const MaxRequestLength = 12000;

export async function onRequestPost({ request, env, ctx }) {
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

	ctx.waitUntil(
		sendNotification(env, message, buildSha).catch((err) =>
			console.error("feedback email failed:", err),
		),
	);

	return json({ ok: true });
}

async function sendNotification(env, message, buildSha) {
	if (
		!env.CLOUDFLARE_ACCOUNT_ID ||
		!env.CLOUDFLARE_EMAIL_API_TOKEN ||
		!env.FEEDBACK_FROM ||
		!env.FEEDBACK_TO
	)
		return;

	const response = await fetch(
		`https://api.cloudflare.com/client/v4/accounts/${env.CLOUDFLARE_ACCOUNT_ID}/email/sending/send`,
		{
			method: "POST",
			headers: {
				Authorization: `Bearer ${env.CLOUDFLARE_EMAIL_API_TOKEN}`,
				"Content-Type": "application/json",
			},
			body: JSON.stringify({
				to: env.FEEDBACK_TO,
				from: { address: env.FEEDBACK_FROM, name: "Myth Feedback" },
				subject: "New Myth feedback",
				text: `${message}\n\n— build ${buildSha ?? "unknown"}`,
			}),
		},
	);
	if (!response.ok) throw new Error(`Cloudflare Email API returned ${response.status}`);
}

function json(body, status = 200) {
	return Response.json(body, {
		status,
		headers: { "Cache-Control": "no-store" },
	});
}
