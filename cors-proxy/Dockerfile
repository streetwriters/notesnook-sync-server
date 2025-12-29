FROM oven/bun:1.3.5-slim

RUN mkdir -p /home/bun/app && chown -R bun:bun /home/bun/app

WORKDIR /home/bun/app

USER bun

COPY --chown=bun:bun package.json bun.lock .

RUN bun install --frozen-lockfile

COPY --chown=bun:bun . .

RUN bun run build

EXPOSE 3000

CMD ["bun", "run", "start"]
