const ReleasePageSize = 20;

function json(value) {
    return new Response(JSON.stringify(value), {
        headers: {
            "Cache-Control": "no-store",
            "Content-Type": "application/json; charset=utf-8",
        },
    });
}

async function releases(bucket, cursor) {
    const page = await bucket.list({
        prefix: "releases/",
        limit: ReleasePageSize,
        ...(cursor ? { cursor } : {}),
    });
    const values = await Promise.all(page.objects.map(async ({ key }) => {
        const object = await bucket.get(key);
        return object ? object.json() : null;
    }));
    return json({
        releases: values.filter(Boolean),
        cursor: page.truncated ? page.cursor : null,
    });
}

export async function onRequest({ env, params, request }) {
    if (request.method !== "GET" && request.method !== "HEAD")
        return new Response("Method not allowed", { status: 405, headers: { Allow: "GET, HEAD" } });

    const path = Array.isArray(params.path) ? params.path.join("/") : params.path;
    if (path === "releases.json")
        return releases(env.MYTH_DOWNLOADS, new URL(request.url).searchParams.get("cursor"));
    if (!/^Myth-[0-9a-f]{8}\.zip$/.test(path))
        return new Response("Not found", { status: 404 });

    const object = request.method === "HEAD"
        ? await env.MYTH_DOWNLOADS.head(path)
        : await env.MYTH_DOWNLOADS.get(path);
    if (!object)
        return new Response("Not found", { status: 404 });

    const headers = new Headers();
    object.writeHttpMetadata(headers);
    headers.set("Cache-Control", "public, max-age=31536000, immutable");
    headers.set("ETag", object.httpEtag);
    headers.set("X-Content-Type-Options", "nosniff");
    return new Response(request.method === "HEAD" ? null : object.body, { headers });
}
