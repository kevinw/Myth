#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";

const [source, output] = process.argv.slice(2);
if (!source || !output)
  throw new Error("Usage: generate-acknowledgements.mjs <ACKNOWLEDGEMENTS.txt> <output/index.html>");

const text = fs.readFileSync(source, "utf8").trim();
const escape = (value) => value
  .replaceAll("&", "&amp;")
  .replaceAll("<", "&lt;")
  .replaceAll(">", "&gt;")
  .replaceAll('"', "&quot;");
const content = text.split(/(https?:\/\/[^\s]+)/g)
  .map((part) => part.startsWith("http")
    ? `<a href="${escape(part)}">${escape(part)}</a>`
    : escape(part))
  .join("");

const html = `<!doctype html>
<html lang="en">
    <head>
        <meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <title>Acknowledgements — Myth</title>
        <style>
            :root {
                color-scheme: dark;
                --backdrop: #150e22;
                --panel: #2f213f;
                --accent: #e0d3a7;
                --text: #f5e9c8;
                --muted: #b9acc2;
                --mono: ui-monospace, "SF Mono", "Cascadia Code", "Source Code Pro",
                    Menlo, Consolas, "Liberation Mono", monospace;
            }
            * { box-sizing: border-box; }
            body {
                margin: 0;
                min-height: 100vh;
                padding: 48px 20px;
                background: var(--backdrop);
                color: var(--text);
                font-family: var(--mono);
            }
            main { width: min(840px, 100%); margin: 0 auto; }
            nav { margin-bottom: 28px; }
            a { color: var(--accent); }
            pre {
                margin: 0;
                padding: 24px;
                border: 1px solid rgb(224 211 167 / 55%);
                background: var(--panel);
                color: var(--text);
                font: 13px / 1.55 var(--mono);
                overflow-wrap: anywhere;
                white-space: pre-wrap;
            }
            @media (max-width: 560px) {
                body { padding: 24px 12px; }
                pre { padding: 16px; font-size: 12px; }
            }
        </style>
    </head>
    <body>
        <main>
            <nav><a href="/">← Back</a></nav>
            <pre>${content}</pre>
        </main>
    </body>
</html>
`;

fs.mkdirSync(path.dirname(output), { recursive: true });
fs.writeFileSync(output, html);
