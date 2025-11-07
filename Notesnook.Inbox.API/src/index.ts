import express from "express";
import { z } from "zod";
import { rateLimit } from "express-rate-limit";
import * as openpgp from "openpgp";

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
  item: string;
}

async function encrypt(
  rawData: string,
  rawPublicKey: string
): Promise<EncryptedInboxItem> {
  const publicKey = await openpgp.readKey({ armoredKey: rawPublicKey });
  const message = await openpgp.createMessage({ text: rawData });
  const encrypted = await openpgp.encrypt({
    message,
    encryptionKeys: publicKey,
  });
  return {
    v: 1,
    item: encrypted,
  };
}

async function getInboxPublicEncryptionKey(apiKey: string) {
  const response = await fetch(
    `${NOTESNOOK_API_SERVER_URL}/inbox/public-encryption-key`,
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

    const inboxPublicKey = await getInboxPublicEncryptionKey(apiKey);
    if (!inboxPublicKey) {
      return res.status(403).json({ error: "inbox public key not found" });
    }
    console.log("[info] fetched inbox public key");

    const validationResult = RawInboxItemSchema.safeParse(req.body);
    if (!validationResult.success) {
      return res.status(400).json({
        error: "invalid item",
        details: validationResult.error.issues,
      });
    }

    const encryptedItem = await encrypt(
      JSON.stringify(validationResult.data),
      inboxPublicKey
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

const PORT = Number(process.env.PORT || "5181");
app.listen(PORT, () => {
  console.log(`📫 notesnook inbox api server running on port ${PORT}`);
});

export default app;
