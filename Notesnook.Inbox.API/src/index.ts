import express from "express";
import _sodium from "libsodium-wrappers-sumo";

const NOTESNOOK_API_SERVER_URL = process.env.NOTESNOOK_API_SERVER_URL;
if (!NOTESNOOK_API_SERVER_URL) {
  throw new Error("NOTESNOOK_API_SERVER_URL is not defined");
}

let sodium: typeof _sodium;

interface RawInboxItem {
  title: string;
  pinned?: boolean;
  favorite?: boolean;
  readonly?: boolean;
  archived?: boolean;
  notebookIds?: string[];
  tagIds?: string[];
}

interface EncryptedInboxItem {
  iv: string;
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
      iv: "iv",
      alg: "X25519 and XSalsa20-Poly1305",
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

    const inboxPublicKey = await getInboxPublicEncryptionKey(apiKey);
    if (!inboxPublicKey) {
      return res.status(403).json({ error: "inbox public key not found" });
    }
    console.log("[info] fetched inbox public key:", inboxPublicKey);

    const item: RawInboxItem = req.body.item;
    if (!item || typeof item !== "object") {
      return res.status(400).json({ error: "invalid item" });
    }

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
