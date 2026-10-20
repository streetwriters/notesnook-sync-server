/**
 * Production-ready CORS Proxy Server
 * Built with Bun runtime
 */

import { lookup } from "node:dns/promises";
import { isIP } from "node:net";

const PORT = Bun.env.PORT || 3000;
const HOST = Bun.env.HOST || "localhost";
const ALLOWED_ORIGINS = Bun.env.ALLOWED_ORIGINS?.split(",") || ["*"];
const MAX_REDIRECTS = 5;

// CORS headers configuration
const corsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods":
    "GET, POST, PUT, DELETE, OPTIONS, HEAD, PATCH",
  "Access-Control-Allow-Headers":
    "Content-Type, Authorization, X-Requested-With, Accept, Accept-Language, Referer, Origin",
  "Access-Control-Max-Age": "86400",
  "Access-Control-Expose-Headers":
    "Content-Length, Content-Type, Date, Server, X-Powered-By",
};

// Log request for monitoring
function logRequest(method: string, url: string, status: number) {
  const timestamp = new Date().toISOString();
  console.log(`[${timestamp}] ${method} ${url} - ${status}`);
}

// Validate URL
function isValidUrl(urlString: string): boolean {
  try {
    const url = new URL(urlString);
    return url.protocol === "http:" || url.protocol === "https:";
  } catch {
    return false;
  }
}

function isAllowedOrigin(origin: string): boolean {
  return ALLOWED_ORIGINS.includes("*") || ALLOWED_ORIGINS.includes(origin);
}

// Only allow hosts that resolve exclusively to public addresses so the proxy
// can't be used to reach internal services or cloud metadata endpoints
async function isPublicHost(hostname: string): Promise<boolean> {
  const host = hostname.replace(/^\[|\]$/g, "");
  try {
    const addresses = isIP(host)
      ? [{ address: host }]
      : await lookup(host, { all: true, verbatim: true });
    return (
      addresses.length > 0 &&
      addresses.every(({ address }) => isPublicAddress(address))
    );
  } catch {
    return false;
  }
}

function isPublicAddress(address: string): boolean {
  if (isIP(address) === 4)
    return isPublicIPv4(...(address.split(".").map(Number) as IPv4Octets));

  const groups = parseIPv6(address);
  if (!groups) return false;
  const [g0, g1, g2, g3, g4, g5, g6, g7] = groups;
  const embeddedIPv4: IPv4Octets = [g6 >> 8, g6 & 0xff, g7 >> 8, g7 & 0xff];

  // ::/96 (includes :: and ::1), ::ffff:0:0/96 (IPv4-mapped)
  if (!g0 && !g1 && !g2 && !g3 && !g4 && (!g5 || g5 === 0xffff))
    return isPublicIPv4(...embeddedIPv4);
  // 64:ff9b::/96 (NAT64)
  if (g0 === 0x64 && g1 === 0xff9b && !g2 && !g3 && !g4 && !g5)
    return isPublicIPv4(...embeddedIPv4);
  // 2002::/16 (6to4)
  if (g0 === 0x2002)
    return isPublicIPv4(g1 >> 8, g1 & 0xff, g2 >> 8, g2 & 0xff);

  return !(
    (g0 & 0xfe00) === 0xfc00 || // fc00::/7 unique local
    (g0 & 0xffc0) === 0xfe80 || // fe80::/10 link local
    (g0 & 0xff00) === 0xff00 // ff00::/8 multicast
  );
}

function isPublicIPv4(a: number, b: number, c: number, _d: number): boolean {
  return !(
    a === 0 ||
    a === 10 ||
    a === 127 ||
    a >= 224 ||
    (a === 100 && b >= 64 && b <= 127) ||
    (a === 169 && b === 254) ||
    (a === 172 && b >= 16 && b <= 31) ||
    (a === 192 && b === 168) ||
    (a === 192 && b === 0 && c === 0) ||
    (a === 198 && (b === 18 || b === 19))
  );
}

type IPv4Octets = [number, number, number, number];
type IPv6Groups = [number, number, number, number, number, number, number, number];

function parseIPv6(address: string): IPv6Groups | null {
  let ip = address.toLowerCase().replace(/%.*$/, "");
  const ipv4Tail = ip.match(/(\d+)\.(\d+)\.(\d+)\.(\d+)$/);
  if (ipv4Tail) {
    const [a, b, c, d] = ipv4Tail.slice(1).map(Number) as IPv4Octets;
    ip =
      ip.slice(0, ipv4Tail.index) +
      ((a << 8) | b).toString(16) +
      ":" +
      ((c << 8) | d).toString(16);
  }

  const [head, tail, ...rest] = ip.split("::");
  if (rest.length) return null;
  const headGroups = head ? head.split(":") : [];
  const tailGroups = tail ? tail.split(":") : [];
  const groups =
    tail === undefined
      ? headGroups
      : [
          ...headGroups,
          ...Array(8 - headGroups.length - tailGroups.length).fill("0"),
          ...tailGroups,
        ];
  if (groups.length !== 8 || groups.some((g) => !/^[0-9a-f]{1,4}$/.test(g)))
    return null;
  return groups.map((g) => parseInt(g, 16)) as IPv6Groups;
}

// Handle proxied request with redirect support
async function proxyRequest(
  targetUrl: string,
  redirectCount = 0,
): Promise<Response> {
  if (redirectCount >= MAX_REDIRECTS) {
    return new Response("Too many redirects", {
      status: 508,
      headers: corsHeaders,
    });
  }

  // checked on every hop so redirects can't lead to internal hosts
  if (!isValidUrl(targetUrl) || !(await isPublicHost(new URL(targetUrl).hostname))) {
    return new Response("Forbidden target URL", {
      status: 403,
      headers: corsHeaders,
    });
  }

  try {
    const response = await fetch(targetUrl, {
      method: "GET",
      redirect: "manual",
    });

    // Handle redirects manually
    if (response.status >= 300 && response.status < 400) {
      const location = response.headers.get("Location");
      if (location) {
        const redirectUrl = new URL(location, targetUrl).toString();
        return proxyRequest(redirectUrl, redirectCount + 1);
      }
    }

    // Get response headers
    const responseHeaders = new Headers(corsHeaders);

    // Forward important headers (but NOT content-encoding since fetch auto-decompresses)
    const headersToForward = [
      "content-type",
      "content-length",
      "cache-control",
      "etag",
      "last-modified",
      "content-disposition",
      "content-range",
      "accept-ranges",
      "vary",
      "date",
      "expires",
      "age",
    ];

    headersToForward.forEach((header) => {
      const value = response.headers.get(header);
      if (value) {
        responseHeaders.set(header, value);
      }
    });

    // Remove headers that might reveal proxy usage
    responseHeaders.delete("x-powered-by");
    responseHeaders.delete("server");
    responseHeaders.delete("via");
    responseHeaders.delete("x-proxy");
    responseHeaders.delete("x-cache");

    // upstream content is served from the proxy's origin so it must never be
    // able to run scripts (e.g. HTML or SVG opened directly)
    responseHeaders.set("Content-Security-Policy", "sandbox");
    responseHeaders.set("X-Content-Type-Options", "nosniff");

    return new Response(response.body, {
      status: response.status,
      statusText: response.statusText,
      headers: responseHeaders,
    });
  } catch (error) {
    const errorMessage =
      error instanceof Error ? error.message : "Unknown error";
    console.error(`Proxy error: ${errorMessage}`);
    return new Response(`Proxy error: ${errorMessage}`, {
      status: 502,
      headers: corsHeaders,
    });
  }
}

// Main server
const server = Bun.serve({
  port: PORT,
  hostname: HOST,
  async fetch(req) {
    const url = new URL(req.url);

    const origin = req.headers.get("Origin");
    if (origin && !isAllowedOrigin(origin)) {
      logRequest(req.method, url.pathname, 403);
      return new Response("Origin not allowed", { status: 403 });
    }

    // Health check endpoint
    if (url.pathname === "/health") {
      logRequest(req.method, url.pathname, 200);
      return new Response("OK", {
        status: 200,
        headers: corsHeaders,
      });
    }

    // Handle CORS preflight
    if (req.method === "OPTIONS") {
      logRequest(req.method, url.pathname, 204);
      return new Response(null, {
        status: 204,
        headers: corsHeaders,
      });
    }

    // Root endpoint with usage info
    if (url.pathname === "/") {
      const usage = {
        service: "CORS Proxy Server",
        version: "1.0.0",
        usage: {
          method1: "GET /<url>",
          method2: "GET /?url=<encoded-url>",
          example1: `${url.origin}/https://example.com/image.jpg`,
          example2: `${url.origin}/?url=${encodeURIComponent(
            "https://example.com/image.jpg",
          )}`,
        },
        endpoints: {
          health: "/health",
          proxy: "/<target-url> or /?url=<target-url>",
        },
      };

      logRequest(req.method, url.pathname, 200);
      return new Response(JSON.stringify(usage, null, 2), {
        status: 200,
        headers: {
          ...corsHeaders,
          "Content-Type": "application/json",
        },
      });
    }

    // Get target URL from path or query parameter
    let targetUrl: string | null = null;

    // Method 1: Direct path (preferred) - http://localhost:3000/https://example.com/image.jpg
    if (url.pathname !== "/" && url.pathname !== "/health") {
      // Remove leading slash and reconstruct the full URL with query string
      targetUrl = url.pathname.slice(1);
      if (url.search) {
        targetUrl += url.search;
      }
    }

    // Method 2: Query parameter - http://localhost:3000/?url=https://example.com/image.jpg
    if (!targetUrl) {
      targetUrl = url.searchParams.get("url");
    }

    if (!targetUrl) {
      logRequest(req.method, url.pathname, 400);
      return new Response(
        "Missing URL. Use http://localhost:3000/<url> or /?url=<url>",
        {
          status: 400,
          headers: corsHeaders,
        },
      );
    }

    // Decode URL if needed
    try {
      targetUrl = decodeURIComponent(targetUrl);
    } catch {
      // URL might not be encoded, use as is
    }

    // Validate URL
    if (!isValidUrl(targetUrl)) {
      logRequest(req.method, targetUrl, 400);
      return new Response("Invalid URL provided", {
        status: 400,
        headers: corsHeaders,
      });
    }

    // Check if it's a YouTube URL and redirect instead of proxying
    if (isYouTubeEmbed(targetUrl)) {
      // YouTube URL detected, redirect to youtube-nocookie.com
      logRequest(req.method, targetUrl, 200);
      return new Response(serveYouTubeEmbed(targetUrl), {
        status: 200,
        headers: {
          "Content-Type": "text/html; charset=utf-8",
          // "Content-Security-Policy": "frame-ancestors *",
          // "X-Frame-Options": "ALLOWALL",
        },
      });
    }

    // Proxy the request for non-YouTube URLs
    const response = await proxyRequest(targetUrl);
    logRequest(req.method, targetUrl, response.status);
    return response;
  },
  error(error) {
    console.error("Server error:", error);
    return new Response("Internal Server Error", {
      status: 500,
      headers: corsHeaders,
    });
  },
});

console.log(
  `🚀 CORS Proxy Server running on http://${server.hostname}:${server.port}`,
);
console.log(`📋 Health check: http://${server.hostname}:${server.port}/health`);
console.log(`🌍 Environment: ${Bun.env.NODE_ENV || "development"}`);

/**
 * This is required to bypass YouTube's Referrer Policy restrictions when
 * embedding videos on the mobile app. It basically "proxies" the Referrer and
 * allows any YouTube video to be embedded anywhere without restrictions.
 */
function serveYouTubeEmbed(url: string) {
  return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width,initial-scale=1">
    <meta name="referrer" content="strict-origin-when-cross-origin">
    <meta name="robots" content="noindex,nofollow">
    <title>YouTube Video Embed</title>
    <style>
    * {
        margin: 0;
        padding: 0;
        box-sizing:border-box
    }

    body, html {
        overflow: hidden;
        background:#000
    }

    iframe {
        border: 0;
        width: 100vw;
        height: 100vh;
        display: block
    }
    </style>
</head>
<body>
    <iframe src="${escapeHtmlAttr(
      transformYouTubeUrl(url),
    )}" allow="accelerometer;autoplay;clipboard-write;encrypted-media;gyroscope;picture-in-picture;web-share" allowfullscreen referrerpolicy="strict-origin-when-cross-origin" title="Video player"></iframe>
</body>
</html>`;
}

// Check if URL is a YouTube embed (including youtube-nocookie.com)
function isYouTubeEmbed(urlString: string) {
  const url = new URL(urlString);
  return (
    (url.hostname === "www.youtube.com" ||
      url.hostname === "youtube.com" ||
      url.hostname === "m.youtube.com" ||
      url.hostname === "www.youtube-nocookie.com" ||
      url.hostname === "youtube-nocookie.com") &&
    url.pathname.startsWith("/embed/")
  );
}

function escapeHtmlAttr(str: string): string {
  return str
    .replace(/&/g, "&amp;")
    .replace(/"/g, "&quot;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

// Transform YouTube URLs to use youtube-nocookie.com for enhanced privacy
function transformYouTubeUrl(urlString: string): string {
  try {
    const url = new URL(urlString);

    // Check if it's a YouTube domain
    if (
      url.hostname === "www.youtube.com" ||
      url.hostname === "youtube.com" ||
      url.hostname === "m.youtube.com"
    ) {
      // Replace with youtube-nocookie.com
      url.hostname = "www.youtube-nocookie.com";
      return url.toString();
    }

    return url.toString();
  } catch {
    return urlString;
  }
}
