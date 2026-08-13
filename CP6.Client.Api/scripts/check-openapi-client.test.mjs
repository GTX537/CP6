import assert from "node:assert/strict";
import test from "node:test";

import {
  buildClientSurface,
  hashClientSurface,
  isClientPath,
} from "./check-openapi-client.mjs";

test("selects only native client routes", () => {
  assert.equal(isClientPath("/api/client-auth/login"), true);
  assert.equal(isClientPath("/api/client/bootstrap"), true);
  assert.equal(isClientPath("/api/v2/wms/tasks/T-1/claim"), true);
  assert.equal(isClientPath("/api/v2/wms/label-jobs"), true);
  assert.equal(isClientPath("/api/crm/leads"), false);
});

test("includes the reachable schema closure and excludes unrelated schemas", () => {
  const document = {
    paths: {
      "/api/client-auth/login": {
        post: {
          requestBody: {
            content: {
              "application/json": { schema: { $ref: "#/components/schemas/Login" } },
            },
          },
        },
      },
      "/api/crm/leads": {
        get: { responses: { 200: { $ref: "#/components/schemas/Unrelated" } } },
      },
    },
    components: {
      schemas: {
        Login: { properties: { context: { $ref: "#/components/schemas/Context" } } },
        Context: { type: "object" },
        Unrelated: { type: "object" },
      },
    },
  };

  const surface = buildClientSurface(document);
  assert.deepEqual(Object.keys(surface.paths), ["/api/client-auth/login"]);
  assert.deepEqual(new Set(Object.keys(surface.schemas)), new Set(["Login", "Context"]));
});

test("hash is stable across object property order", () => {
  const first = {
    paths: {
      "/api/client/bootstrap": { get: { responses: { 200: { description: "OK" } } } },
    },
    components: { schemas: { Ignored: { type: "string" } } },
  };
  const second = {
    components: { schemas: { Ignored: { type: "string" } } },
    paths: {
      "/api/client/bootstrap": { get: { responses: { 200: { description: "OK" } } } },
    },
  };

  assert.equal(hashClientSurface(first), hashClientSurface(second));
});

test("hash changes when a reachable client schema changes", () => {
  const createDocument = (type) => ({
    paths: {
      "/api/client/devices/activate": {
        post: { responses: { 200: { $ref: "#/components/schemas/Device" } } },
      },
    },
    components: { schemas: { Device: { type } } },
  });

  assert.notEqual(
    hashClientSurface(createDocument("object")),
    hashClientSurface(createDocument("string")),
  );
});
