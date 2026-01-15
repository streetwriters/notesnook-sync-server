/**
 * Production-ready CORS Proxy Server
 * Built with Bun runtime
 */

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

// Handle proxied request with redirect support
async function proxyRequest(
  targetUrl: string,
  redirectCount = 0
): Promise<Response> {
  if (redirectCount >= MAX_REDIRECTS) {
    return new Response("Too many redirects", {
      status: 508,
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
            "https://example.com/image.jpg"
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
        }
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
  `🚀 CORS Proxy Server running on http://${server.hostname}:${server.port}`
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
    <iframe src="${transformYouTubeUrl(
      url
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

    return urlString;
  } catch {
    return urlString;
  }
}
