import express, {
  type ErrorRequestHandler,
  type RequestHandler,
} from "express";
import { z } from "zod";
import { ipKeyGenerator, rateLimit } from "express-rate-limit";
import * as openpgp from "openpgp";
import { isIP } from "node:net";

const NOTESNOOK_API_SERVER_URL = process.env.NOTESNOOK_API_SERVER_URL;
if (!NOTESNOOK_API_SERVER_URL) {
  throw new Error("NOTESNOOK_API_SERVER_URL is not defined");
}

const RawInboxItemSchema = z.object({
  title: z.string().min(1, "Title is required"),
  pinned: z.boolean().optional(),
  favorite: z.boolean().optional(),
  readonly: z.boolean().optional(),
  archived: z.boolean().optional(),
  notebookIds: z.array(z.string()).optional(),
  tagIds: z.array(z.string()).optional(),
  type: z.enum(["note"]),
  source: z.string().min(1, "Source is required"),
  version: z.literal(1),
  content: z
    .object({
      type: z.enum(["html"]),
      data: z.string(),
    })
    .optional(),
});

interface EncryptedInboxItem {
  v: 1;
  cipher: string;
  alg: string;
}

/**
 * Encrypts raw data using OpenPGP with the recipient's public key
 *
 * @param {string} rawData - The plaintext data to encrypt
 * @param {string} rawPublicKey - The recipient's OpenPGP public key
 */
async function encrypt(
  rawData: string,
  rawPublicKey: string,
): Promise<EncryptedInboxItem> {
  const publicKey = await openpgp.readKey({ armoredKey: rawPublicKey });
  const message = await openpgp.createMessage({ text: rawData });
  const encrypted = await openpgp.encrypt({
    message,
    encryptionKeys: publicKey,
  });
  return {
    v: 1,
    cipher: encrypted,
    alg: "pgp-aes256",
  };
}

async function getInboxPublicEncryptionKey(
  apiKey: string,
): Promise<{ status: "unauthorized" } | { status: "ok"; key: string | null }> {
  const response = await fetch(
    `${NOTESNOOK_API_SERVER_URL}/inbox/public-encryption-key`,
    {
      headers: {
        Authorization: apiKey,
      },
    },
  );
  if (response.status === 401) {
    return { status: "unauthorized" };
  }
  if (!response.ok) {
    throw new Error(
      `failed to fetch inbox public encryption key: ${await response.text()}`,
    );
  }

  const data = (await response.json()) as unknown as any;
  return { status: "ok", key: (data?.key as string) || null };
}

async function postEncryptedInboxItem(
  apiKey: string,
  item: EncryptedInboxItem,
) {
  const response = await fetch(`${NOTESNOOK_API_SERVER_URL}/inbox/items`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: apiKey,
    },
    body: JSON.stringify({ ...item }),
  });
  if (!response.ok) {
    throw new Error(`failed to post inbox item: ${await response.text()}`);
  }
}

// Only a proxy on the private network may say who the client is; a client reaching the server directly could send any address.
function clientIp(req: express.Request): string {
  const connection = req.socket.remoteAddress || "";
  const forwarded = req.headers["cf-connecting-ip"];
  if (typeof forwarded === "string" && isIP(forwarded) && isPrivateOrLoopback(connection)) return forwarded;
  return connection;
}

function isPrivateOrLoopback(address: string): boolean {
  const ip = address.startsWith("::ffff:") ? address.slice(7) : address;
  if (isIP(ip) === 4) {
    const [a, b] = ip.split(".").map(Number);
    return a === 10 || a === 127 || (a === 172 && b >= 16 && b <= 31) || (a === 192 && b === 168) || (a === 169 && b === 254);
  }
  const lower = ip.toLowerCase();
  return lower === "::1" || lower.startsWith("fc") || lower.startsWith("fd") || lower.startsWith("fe80:");
}

const app = express();

// Before the rate limiter, so load balancer health checks are never throttled.
app.get("/health", (_, res) => {
  return res.status(200).json({ status: "ok" });
});

app.use(
  rateLimit({
    windowMs: 1 * 60 * 1000, // 1 minute
    limit: 60,
    keyGenerator: (req) => ipKeyGenerator(clientIp(req)),
    // X-Forwarded-For only ever holds the proxy's address here
    validate: { xForwardedForHeader: false },
  }),
);

// Authenticate before parsing the body, so unauthenticated requests never buffer up to 10 MB.
const authenticate: RequestHandler = async (req, res, next) => {
  try {
    const apiKey = req.headers["authorization"];
    if (!apiKey) {
      res.status(401).json({ error: "unauthorized" });
      return;
    }

    const encryptionKeyResult = await getInboxPublicEncryptionKey(apiKey);
    if (encryptionKeyResult.status === "unauthorized") {
      res.status(401).json({ error: "unauthorized" });
      return;
    }
    if (!encryptionKeyResult.key) {
      res.status(404).json({ error: "inbox public key not found" });
      return;
    }
    console.log("[info] fetched inbox public key");
    res.locals.apiKey = apiKey;
    res.locals.inboxPublicKey = encryptionKeyResult.key;
    next();
  } catch (error) {
    next(error);
  }
};

app.post("/", authenticate, express.json({ limit: "10mb" }), async (req, res) => {
  try {
    const apiKey: string = res.locals.apiKey;
    const inboxPublicKey: string = res.locals.inboxPublicKey;

    const validationResult = RawInboxItemSchema.safeParse(req.body);
    if (!validationResult.success) {
      return res.status(400).json({
        error: "invalid item",
        details: validationResult.error.issues,
      });
    }

    const encryptedItem = await encrypt(
      JSON.stringify(validationResult.data),
      inboxPublicKey,
    );
    console.log("[info] encrypted item");

    await postEncryptedInboxItem(apiKey, encryptedItem);
    console.log("[info] posted encrypted inbox item successfully");

    return res.status(200).json({ success: true });
  } catch (error) {
    if (error instanceof Error) {
      console.log("[error]", error.message);
      return res
        .status(500)
        .json({ error: "internal server error", description: error.message });
    } else {
      console.log("[error] unknown error occured:", error);
      return res.status(500).json({
        error: "internal server error",
        description: `unknown error occured: ${error}`,
      });
    }
  }
});

const handleErrors: ErrorRequestHandler = (error, _req, res, next) => {
  if (res.headersSent) return next(error);
  if (error?.type === "entity.parse.failed") {
    res.status(400).json({ error: "invalid json" });
    return;
  }
  if (error?.type === "entity.too.large") {
    res.status(413).json({ error: "item too large" });
    return;
  }
  console.log("[error]", error instanceof Error ? error.message : error);
  res.status(500).json({ error: "internal server error" });
};
app.use(handleErrors);

const PORT = Number(process.env.PORT || "5181");
app.listen(PORT, () => {
  console.log(`📫 notesnook inbox api server running on port ${PORT}`);
});

export default app;
