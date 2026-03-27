#!/bin/bash

GNUPGHOME=$(mktemp -d)

curl -s http://localhost:5264/inbox/public-encryption-key -H "Authorization: $API_KEY" | jq -r .key > "$GNUPGHOME"/pubkey.asc && gpg --batch --homedir "$GNUPGHOME" --import "$GNUPGHOME"/pubkey.asc >/dev/null 2>&1 && KEYID=$(gpg --homedir "$GNUPGHOME" --list-keys --with-colons | awk -F: '/^pub:/ {print $5; exit}') && printf '%s' '{"title":"Test title CLIE S","type":"note","source":"cli","version":1}' | gpg --batch --homedir "$GNUPGHOME" --trust-model always --armor --encrypt -r "$KEYID" | jq -Rs --arg alg "pgp-aes256" '{v:1, cipher:., alg:$alg}' | curl -s -X POST http://localhost:5264/inbox/items -H "Content-Type: application/json" -H "Authorization: $API_KEY" -d @- && rm -rf "$GNUPGHOME"