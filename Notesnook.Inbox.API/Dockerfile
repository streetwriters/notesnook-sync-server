FROM oven/bun:1.2.21-slim

RUN mkdir -p /home/bun/app && chown -R bun:bun /home/bun/app

WORKDIR /home/bun/app

USER bun

COPY --chown=bun:bun package.json bun.lock .

RUN bun install --frozen-lockfile

COPY --chown=bun:bun . .

RUN bun run build

EXPOSE 5181

CMD ["bun", "run", "start"]
