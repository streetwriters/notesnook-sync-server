import express from "express";
import _sodium, { base64_variants } from "libsodium-wrappers-sumo";
import { z } from "zod";
import { rateLimit } from "express-rate-limit";

const NOTESNOOK_API_SERVER_URL = process.env.NOTESNOOK_API_SERVER_URL;
if (!NOTESNOOK_API_SERVER_URL) {
  throw new Error("NOTESNOOK_API_SERVER_URL is not defined");
}

let sodium: typeof _sodium;

const RawInboxItemSchema = z.object({
  title: z.string().min(1, "Title is required"),
  pinned: z.boolean().optional(),
  favorite: z.boolean().optional(),
  readonly: z.boolean().optional(),
  archived: z.boolean().optional(),
  notebookIds: z.array(z.string()).optional(),
  tagIds: z.array(z.string()).optional(),
  type: z.enum(["note"]),
  source: z.string(),
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
  key: Omit<EncryptedInboxItem, "key" | "iv" | "v" | "salt">;
  iv: string;
  alg: string;
  cipher: string;
  length: number;
  salt: string;
}

function encrypt(rawData: string, publicKey: string): EncryptedInboxItem {
  try {
    const password = sodium.crypto_aead_xchacha20poly1305_ietf_keygen();
    const saltBytes = sodium.randombytes_buf(sodium.crypto_pwhash_SALTBYTES);
    const key = sodium.crypto_pwhash(
      sodium.crypto_aead_xchacha20poly1305_ietf_KEYBYTES,
      password,
      saltBytes,
      3, // operations limit
      1024 * 1024 * 8, // memory limit (8MB)
      sodium.crypto_pwhash_ALG_ARGON2I13
    );
    const nonce = sodium.randombytes_buf(
      sodium.crypto_aead_xchacha20poly1305_ietf_NPUBBYTES
    );
    const data = sodium.from_string(rawData);
    const cipher = sodium.crypto_aead_xchacha20poly1305_ietf_encrypt(
      data,
      null,
      null,
      nonce,
      key
    );
    const inboxPublicKey = sodium.from_base64(
      publicKey,
      base64_variants.URLSAFE_NO_PADDING
    );
    const encryptedKey = sodium.crypto_box_seal(key, inboxPublicKey);

    return {
      v: 1,
      key: {
        cipher: sodium.to_base64(
          encryptedKey,
          base64_variants.URLSAFE_NO_PADDING
        ),
        alg: `xsal-x25519-${base64_variants.URLSAFE_NO_PADDING}`,
        length: password.length,
      },
      iv: sodium.to_base64(nonce, base64_variants.URLSAFE_NO_PADDING),
      alg: `xcha-argon2i13-${base64_variants.URLSAFE_NO_PADDING}`,
      cipher: sodium.to_base64(cipher, base64_variants.URLSAFE_NO_PADDING),
      length: data.length,
      salt: sodium.to_base64(saltBytes, base64_variants.URLSAFE_NO_PADDING),
    };
  } catch (error) {
    throw new Error(`encryption failed: ${error}`);
  }
}

async function getInboxPublicEncryptionKey(apiKey: string) {
  const response = await fetch(
    `${NOTESNOOK_API_SERVER_URL}inbox/public-encryption-key`,
    {
      headers: {
        Authorization: apiKey,
      },
    }
  );
  if (!response.ok) {
    throw new Error(
      `failed to fetch inbox public encryption key: ${await response.text()}`
    );
  }

  const data = (await response.json()) as unknown as any;
  return (data?.key as string) || null;
}

async function postEncryptedInboxItem(
  apiKey: string,
  item: EncryptedInboxItem
) {
  const response = await fetch(`${NOTESNOOK_API_SERVER_URL}inbox/items`, {
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

const app = express();
app.use(express.json({ limit: "10mb" }));
app.use(
  rateLimit({
    windowMs: 1 * 60 * 1000, // 1 minute
    limit: 60,
  })
);
app.post("/inbox", async (req, res) => {
  try {
    const apiKey = req.headers["authorization"];
    if (!apiKey) {
      return res.status(401).json({ error: "unauthorized" });
    }
    if (!req.body.item) {
      return res.status(400).json({ error: "item is required" });
    }

    const validationResult = RawInboxItemSchema.safeParse(req.body.item);
    if (!validationResult.success) {
      return res.status(400).json({
        error: "invalid item",
        details: validationResult.error.issues,
      });
    }

    const inboxPublicKey = await getInboxPublicEncryptionKey(apiKey);
    if (!inboxPublicKey) {
      return res.status(403).json({ error: "inbox public key not found" });
    }
    console.log("[info] fetched inbox public key:", inboxPublicKey);

    const item = validationResult.data;
    const encryptedItem = encrypt(JSON.stringify(item), inboxPublicKey);
    console.log("[info] encrypted item:", encryptedItem);
    await postEncryptedInboxItem(apiKey, encryptedItem);
    return res.status(200).json({ message: "inbox item posted" });
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

(async () => {
  await _sodium.ready;
  sodium = _sodium;

  const PORT = Number(process.env.PORT || "5181");
  app.listen(PORT, () => {
    console.log(`📫 notesnook inbox api server running on port ${PORT}`);
  });
})();

export default app;
