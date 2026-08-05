# Upgrading MongoDB from 7.0 to 8.0

The `docker-compose.garage.yml` file defaults to `mongo:8.0` for fresh installations.
**Crucial Warning**: If you have existing data running on MongoDB 7.0, you **cannot** simply switch to the 8.0 image. The database will fail to start.

You must follow this upgrade procedure.

## Prerequisites

1.  **Backup your Database**: Ensure you have a valid backup using `mongodump` or your preferred backup method.
2.  **Current Version**: Ensure your current database is running version **7.0**. (Upgrading from older versions requires stepping through 5.0 -> 6.0 -> 7.0 first).

## Step 1: Set Feature Compatibility Version (FCV) to 7.0

Before shutting down the old 7.0 container, you must ensure the FCV is explicitly set to 7.0.

1.  Connect to your running MongoDB container:
    ```bash
    docker exec -it notesnook-notesnook-db-1 mongosh
    ```
    *(Adjust container name if needed)*

2.  Check the current FCV:
    ```javascript
    db.adminCommand( { getParameter: 1, featureCompatibilityVersion: 1 } )
    ```

3.  If the version is not "7.0", set it:
    ```javascript
    db.adminCommand( { setFeatureCompatibilityVersion: "7.0" } )
    ```

4.  Exit the shell (`exit`).

## Step 2: Shutdown and Update Image

1.  Stop your stack:
    ```bash
    docker compose down
    ```

2.  Edit your `docker-compose.yml` (or `docker-compose.garage.yml`):
    *   Change `image: mongo:7.0.12` (or similar) to `image: mongo:8.0`.

## Step 3: Start and Verify

1.  Start the stack:
    ```bash
    docker compose up -d
    ```

2.  Check logs to ensure MongoDB started correctly:
    ```bash
    docker logs notesnook-notesnook-db-1
    ```

## Step 4: Set Feature Compatibility Version (FCV) to 8.0

To enable new features and complete the upgrade:

1.  Connect to the new MongoDB 8.0 container:
    ```bash
    docker exec -it notesnook-notesnook-db-1 mongosh
    ```

2.  Set the FCV to 8.0:
    ```javascript
    db.adminCommand( { setFeatureCompatibilityVersion: "8.0" } )
    ```

3.  Verify:
    ```javascript
    db.adminCommand( { getParameter: 1, featureCompatibilityVersion: 1 } )
    ```
    Output should show `version: '8.0'`.

## Troubleshooting

*   **"WiredTiger error"**: If the container loops with errors about WiredTiger versions, it means you skipped Step 1 or tried to jump multiple major versions (e.g., 6.0 -> 8.0). You must revert the image tag to the previous version, fix the FCV, and try again.
