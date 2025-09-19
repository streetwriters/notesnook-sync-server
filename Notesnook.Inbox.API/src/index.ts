import express from "express";
import _sodium, { base64_variants } from "libsodium-wrappers-sumo";
import { z } from "zod";

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
  iv: null;
  alg: string;
  cipher: string;
  length: number;
}

function encryptData(data: string, publicKey: string): EncryptedInboxItem {
  try {
    const recipientPublicKey = sodium.from_base64(publicKey);
    const dataBytes = sodium.from_string(data);
    const ciphertext = sodium.crypto_box_seal(dataBytes, recipientPublicKey);
    return {
      // iv is explicitely set to null because crypto_box_seal does not require nonce
      iv: null,
      alg: `xsal-x25519-${base64_variants.URLSAFE_NO_PADDING}`,
      cipher: sodium.to_base64(ciphertext),
      length: dataBytes.length,
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
  const data = (await response.json()) as unknown as any;
  return (data?.key as string) || null;
}

async function postEncryptedInboxItem(
  apiKey: string,
  item: EncryptedInboxItem
) {
  await fetch(`${NOTESNOOK_API_SERVER_URL}inbox/items`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: apiKey,
    },
    body: JSON.stringify({ ...item }),
  });
}

const app = express();
app.use(express.json({ limit: "10mb" }));
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
    const encryptedItem = encryptData(JSON.stringify(item), inboxPublicKey);
    console.log("[info] encrypted item:", encryptedItem);
    await postEncryptedInboxItem(apiKey, encryptedItem);
    return res.status(200).json({ message: "inbox item posted" });
  } catch (error) {
    console.log("[error] inbox endpoint error:", error);
    return res.status(500).json({ error: "internal server error" });
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
